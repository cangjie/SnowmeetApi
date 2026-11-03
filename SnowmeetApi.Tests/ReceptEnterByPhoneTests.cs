using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// QrCode/EnterReceptByPhone：店员输手机号进入时，一次建好接待记录、写手机号、判授权，
/// 不再依赖出码页先生成的那张二维码（11-03 杨兴的手机上，二维码页空白时手机号开单就卡住了）。
/// 授权规则和老接口 QueryCell 一致（QrCodeQueryCellTests 钉住了老接口）。
/// </summary>
public class ReceptEnterByPhoneTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static QrCodeController Qr(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private const string Cell = "13800001003";   // 学员 A（会员 3）
    private const string OtherCell = "13800001004";   // 学员 B（会员 4）
    private const string Clerk = "session-7";     // 普通店员（员工 10），职级 100
    private const string Manager = "session-1";   // 店长以上（员工 9），职级 300

    private static async Task<dynamic> Enter(CoachFixture f, string cell, string session)
    {
        var res = await Qr(f).EnterReceptByPhone(cell, session);
        return ((OkObjectResult)res.Result!).Value!;
    }

    private static Task<List<ScanQrCode>> Rows(CoachFixture f) => f.NewContext().scanQrCode.AsNoTracking().OrderBy(s => s.id).ToListAsync();

    [Fact]
    public async Task Clerk_creates_the_record_with_the_phone_and_waits_for_the_manager()
    {
        await using var f = await CoachFixture.Create();

        var result = await Enter(f, Cell, Clerk);

        Assert.Equal(0, (int)result.code);
        Assert.Equal("need_auth", (string)result.data.stage);
        Assert.Equal(CoachFixture.StudentA, (int)result.data.memberId);
        var rows = await Rows(f);
        var row = Assert.Single(rows);
        Assert.Equal(row.id, (int)result.data.id);
        Assert.Equal(10, row.staff_id);
        Assert.Equal("wechat_mini", row.platform);
        Assert.Equal("店铺接待", row.scene);
        Assert.Equal("顾客扫码进小程序", row.purpose);
        Assert.Equal(Cell, row.cell);
        Assert.Equal(0, row.scaned);
        Assert.Equal(0, row.authed);
        Assert.Equal(CoachFixture.StudentA, row.scaner_member_id);
        Assert.True(ReceptCheckinRules.IsCheckinCode(row.code));
    }

    [Fact]
    public async Task Manager_is_released_at_once()
    {
        await using var f = await CoachFixture.Create();

        var result = await Enter(f, Cell, Manager);

        Assert.Equal("phone_authed", (string)result.data.stage);
        Assert.Equal(1, Assert.Single(await Rows(f)).authed);
    }

    [Fact]
    public async Task Clerk_is_released_by_an_authorization_given_earlier_today_for_the_same_phone()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(QrCodeQueryCellTests.Record(99, 9, Cell, authed: 1)); await f.Db.SaveChangesAsync();

        var result = await Enter(f, Cell, Clerk);

        Assert.Equal("phone_authed", (string)result.data.stage);
        Assert.Equal(1, (await Rows(f)).Single(r => r.id != 99).authed);
    }

    [Fact]
    public async Task An_authorization_from_yesterday_or_for_another_phone_does_not_count()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(QrCodeQueryCellTests.Record(98, 9, Cell, authed: 1, created: DateTime.Now.Date.AddMinutes(-30)));
        f.Db.scanQrCode.Add(QrCodeQueryCellTests.Record(99, 9, OtherCell, authed: 1)); await f.Db.SaveChangesAsync();

        var result = await Enter(f, Cell, Clerk);

        Assert.Equal("need_auth", (string)result.data.stage);
    }

    [Fact]
    public async Task A_phone_that_is_not_a_member_is_refused_without_creating_a_record()
    {
        await using var f = await CoachFixture.Create();

        var result = await Enter(f, "19900000000", Clerk);

        Assert.Equal(1, (int)result.code);
        Assert.Equal("用户不存在", (string)result.message);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task A_session_that_is_not_staff_is_refused_without_creating_a_record()
    {
        await using var f = await CoachFixture.Create();

        var result = await Enter(f, Cell, "session-" + CoachFixture.StudentA);

        Assert.Equal(1, (int)result.code);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task Spaces_around_the_phone_are_ignored()
    {
        await using var f = await CoachFixture.Create();

        await Enter(f, " " + Cell + " ", Clerk);

        Assert.Equal(Cell, Assert.Single(await Rows(f)).cell);
    }

    [Fact]
    public async Task The_record_works_with_the_existing_polling_and_manager_authorization()
    {
        await using var f = await CoachFixture.Create();
        var entered = await Enter(f, Cell, Clerk);
        int id = entered.data.id;

        // 店员页照旧用 GetReceptCheckinByStaff 轮询，店长在授权列表里看到并授权
        var poll1 = ((OkObjectResult)(await Qr(f).GetReceptCheckinByStaff(id, Clerk)).Result!).Value!;
        Assert.Equal("need_auth", (string)((dynamic)poll1).data.stage);

        var list = ((OkObjectResult)(await Qr(f).GetTodayAuthCellList(Manager)).Result!).Value!;
        var waiting = (List<ScanQrCode.AuthCell>)((dynamic)list).data;
        Assert.Contains(waiting, c => c.id == id && c.cell == Cell && !c.authed);

        await Qr(f).GiveAuth(id, Manager);

        var poll2 = ((OkObjectResult)(await Qr(f).GetReceptCheckinByStaff(id, Clerk)).Result!).Value!;
        Assert.Equal("phone_authed", (string)((dynamic)poll2).data.stage);
        Assert.Equal(CoachFixture.StudentA, (int)((dynamic)poll2).data.memberId);
    }
}

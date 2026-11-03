using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 老接口 QrCode/QueryCell（线上 398 版小程序还在用）的授权规则：店长及以上直接放行，普通店员要等店长授权，
/// 当天同一手机号已经授权过就直接放行。新接口 EnterReceptByPhone 复用同一套规则，这里先把它钉住。
/// </summary>
public class QrCodeQueryCellTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static QrCodeController Qr(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private const string Cell = "13800001003";   // 学员 A（会员 3）
    private const string Clerk = "session-7";     // 普通店员，职级 100
    private const string Manager = "session-1";   // 店长以上，职级 300

    internal static ScanQrCode Record(int id, int staffId, string? cell = null, int authed = 0, DateTime? created = null)
    {
        return new ScanQrCode
        {
            id = id, code = ReceptCheckinRules.BuildCode("abcdefghij", id), platform = "wechat_mini", staff_id = staffId,
            scene = "店铺接待", purpose = "顾客扫码进小程序", cell = cell, authed = authed,
            expire_time = DateTime.Now.AddMinutes(2), create_date = created ?? DateTime.Now,
        };
    }

    private static async Task<dynamic> Query(CoachFixture f, int id, string cell, string session)
    {
        var res = await Qr(f).QueryCell(id, cell, session);
        return ((OkObjectResult)res.Result!).Value!;
    }

    [Fact]
    public async Task Clerk_without_an_earlier_authorization_waits_for_the_manager()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(Record(100, 10)); await f.Db.SaveChangesAsync();

        var result = await Query(f, 100, Cell, Clerk);

        Assert.Equal(0, (int)result.code);
        var row = await f.NewContext().scanQrCode.AsNoTracking().SingleAsync(s => s.id == 100);
        Assert.Equal(Cell, row.cell);
        Assert.Equal(0, row.authed);
        Assert.Equal(CoachFixture.StudentA, row.scaner_member_id);
    }

    [Fact]
    public async Task Clerk_is_released_by_an_authorization_given_earlier_today()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(Record(99, 9, Cell, authed: 1));
        f.Db.scanQrCode.Add(Record(100, 10)); await f.Db.SaveChangesAsync();

        await Query(f, 100, Cell, Clerk);

        Assert.Equal(1, (await f.NewContext().scanQrCode.AsNoTracking().SingleAsync(s => s.id == 100)).authed);
    }

    [Fact]
    public async Task An_authorization_from_yesterday_does_not_count()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(Record(99, 9, Cell, authed: 1, created: DateTime.Now.Date.AddMinutes(-30)));
        f.Db.scanQrCode.Add(Record(100, 10)); await f.Db.SaveChangesAsync();

        await Query(f, 100, Cell, Clerk);

        Assert.Equal(0, (await f.NewContext().scanQrCode.AsNoTracking().SingleAsync(s => s.id == 100)).authed);
    }

    [Fact]
    public async Task Manager_is_released_at_once()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(Record(100, 9)); await f.Db.SaveChangesAsync();

        await Query(f, 100, Cell, Manager);

        Assert.Equal(1, (await f.NewContext().scanQrCode.AsNoTracking().SingleAsync(s => s.id == 100)).authed);
    }

    [Fact]
    public async Task A_phone_that_is_not_a_member_is_refused()
    {
        await using var f = await CoachFixture.Create();
        f.Db.scanQrCode.Add(Record(100, 10)); await f.Db.SaveChangesAsync();

        var result = await Query(f, 100, "19900000000", Clerk);

        Assert.Equal(1, (int)result.code);
        Assert.Equal("用户不存在", (string)result.message);
        Assert.Null((await f.NewContext().scanQrCode.AsNoTracking().SingleAsync(s => s.id == 100)).cell);
    }
}

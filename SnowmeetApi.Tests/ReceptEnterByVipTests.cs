using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// QrCode/EnterReceptByVip（2026-11-09 用户要求）：接待页「VIP开单」→ VIP 列表 → 点一位 VIP 直接开单，
/// 不用顾客扫码、不用店长授权，且视同身份已经验证（订单 wechat_unverified 开单时就是 1），其余开单流程不变。
/// 做法：建一条已授权的「手机号进入」接待记录（scan_qrcode，purpose = VIP开单），店员页照旧拿 phone_authed 去顾客信息页；
/// 开单时后端 ResolveReceptOrderVerified 本来就认「当天已授权的手机号接待记录」，所以订单自动算已核验。
/// </summary>
public class ReceptEnterByVipTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static QrCodeController Qr(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private const string Cell = "13800001003";   // 学员 A（会员 3）
    private const string Clerk = "session-7";     // 普通店员（员工 10），职级 100
    private const string Manager = "session-1";   // 店长以上（员工 9），职级 300

    private static async Task SetVip(CoachFixture f, int memberId, bool vip = true)
    {
        var m = await f.Db.member.AsTracking().FirstAsync(x => x.id == memberId);   // 测试库默认 NoTracking
        m.is_vip = vip;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<dynamic> Enter(CoachFixture f, int memberId, string session)
    {
        var res = await Qr(f).EnterReceptByVip(memberId, session);
        return ((OkObjectResult)res.Result!).Value!;
    }

    private static Task<List<ScanQrCode>> Rows(CoachFixture f) => f.NewContext().scanQrCode.AsNoTracking().OrderBy(s => s.id).ToListAsync();

    [Fact]
    public async Task A_clerk_enters_a_vip_at_once_without_any_authorization()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);

        var result = await Enter(f, CoachFixture.StudentA, Clerk);

        Assert.Equal(0, (int)result.code);
        Assert.Equal("phone_authed", (string)result.data.stage);   // 店员页据此直接进顾客信息页
        Assert.Equal(CoachFixture.StudentA, (int)result.data.memberId);
        Assert.Equal("学员A", (string)result.data.name);
        var row = Assert.Single(await Rows(f));
        Assert.Equal(row.id, (int)result.data.id);
        Assert.Equal(10, row.staff_id);
        Assert.Equal("wechat_mini", row.platform);
        Assert.Equal("店铺接待", row.scene);
        Assert.Equal("VIP开单", row.purpose);
        Assert.Equal(Cell, row.cell);
        Assert.Equal(0, row.scaned);
        Assert.Equal(1, row.authed);
        Assert.Equal(0, row.stoped);
        Assert.Equal(CoachFixture.StudentA, row.scaner_member_id);
        Assert.True(ReceptCheckinRules.IsCheckinCode(row.code));
    }

    [Fact]
    public async Task A_manager_enters_a_vip_the_same_way()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);

        var result = await Enter(f, CoachFixture.StudentA, Manager);

        Assert.Equal("phone_authed", (string)result.data.stage);
        Assert.Equal(9, Assert.Single(await Rows(f)).staff_id);
    }

    [Fact]
    public async Task An_order_opened_for_the_vip_is_treated_as_identity_verified()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        var result = await Enter(f, CoachFixture.StudentA, Clerk);

        var order = new SnowmeetApi.Models.Order { id = 0, member_id = CoachFixture.StudentA };
        var (prev, next, checkinId) = await Qr(f).ResolveReceptOrderVerified(order, 10);

        Assert.False(prev);
        Assert.True(next);
        Assert.Equal((int)result.data.id, checkinId);
    }

    [Fact]
    public async Task The_verification_only_covers_that_vip_and_the_staff_who_opened_the_entry()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await Enter(f, CoachFixture.StudentA, Clerk);

        var otherMember = await Qr(f).ResolveReceptOrderVerified(new SnowmeetApi.Models.Order { id = 0, member_id = CoachFixture.StudentB }, 10);
        var otherStaff = await Qr(f).ResolveReceptOrderVerified(new SnowmeetApi.Models.Order { id = 0, member_id = CoachFixture.StudentA }, 9);

        Assert.False(otherMember.next);
        Assert.False(otherStaff.next);
    }

    [Fact]
    public async Task A_member_who_is_not_vip_is_refused_without_creating_a_record()
    {
        await using var f = await CoachFixture.Create();

        var result = await Enter(f, CoachFixture.StudentA, Clerk);

        Assert.Equal(1, (int)result.code);
        Assert.Equal("该会员不是VIP", (string)result.message);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task A_vip_flag_removed_after_the_list_was_loaded_is_refused()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await SetVip(f, CoachFixture.StudentA, false);

        var result = await Enter(f, CoachFixture.StudentA, Clerk);

        Assert.Equal(1, (int)result.code);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task Missing_invalid_or_merged_members_are_refused()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await SetVip(f, CoachFixture.StudentB);
        await f.Db.member.Where(m => m.id == CoachFixture.StudentA).ExecuteUpdateAsync(s => s.SetProperty(m => m.valid, 0));
        await f.Db.member.Where(m => m.id == CoachFixture.StudentB).ExecuteUpdateAsync(s => s.SetProperty(m => m.merge_id, CoachFixture.StudentC).SetProperty(m => m.is_merge, 1));

        Assert.Equal(1, (int)(await Enter(f, 9999, Clerk)).code);
        Assert.Equal(1, (int)(await Enter(f, CoachFixture.StudentA, Clerk)).code);
        Assert.Equal(1, (int)(await Enter(f, CoachFixture.StudentB, Clerk)).code);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task A_vip_without_a_registered_cell_is_refused_with_a_clear_reason()
    {
        await using var f = await CoachFixture.Create();
        f.Db.member.Add(new Member { id = 20, real_name = "无手机号VIP", is_vip = true, following_wechat = 0 });
        await f.Db.SaveChangesAsync();

        var result = await Enter(f, 20, Clerk);

        Assert.Equal(1, (int)result.code);
        Assert.Contains("手机号", (string)result.message);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task A_session_that_is_not_staff_is_refused_without_creating_a_record()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);

        var result = await Enter(f, CoachFixture.StudentA, "session-" + CoachFixture.StudentA);

        Assert.Equal(1, (int)result.code);
        Assert.Empty(await Rows(f));
    }

    [Fact]
    public async Task The_vip_shortcut_does_not_authorize_a_later_phone_entry_by_another_clerk()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await Enter(f, CoachFixture.StudentA, Manager);   // 记录里 authed = 1，但用途是「VIP开单」

        var res = await Qr(f).EnterReceptByPhone(Cell, Clerk);
        var result = ((OkObjectResult)res.Result!).Value!;

        Assert.Equal("need_auth", (string)((dynamic)result).data.stage);   // 普通手机号进入仍要店长授权
    }
}

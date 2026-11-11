using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 会员 VIP（2026-11-09 用户要求）：member 表上的 is_vip 字段（带索引），不是标签——列表按 VIP 筛选一定要快，
/// 走 is_vip 索引，不再 EXISTS 关联 member_tag。
/// 会员详情页「设为VIP / 取消VIP」按钮 → SetMemberVip；会员列表「VIP」快捷按钮 → SearchMembersByStaff(vip: true)。
/// </summary>
public class MemberVipTests
{
    private static MemberAdminController Controller(CoachFixture f) => new(f.NewContext(), f.Config, new HttpContextAccessor());

    private static ApiResult<object> Env(ActionResult<ApiResult<object>> a) =>
        Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(a.Result).Value);

    private static JsonElement Data(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    private static async Task SetVip(CoachFixture f, int memberId, bool vip = true)
    {
        var m = await f.Db.member.AsTracking().FirstAsync(x => x.id == memberId);   // 测试库默认 NoTracking，要改必须显式 AsTracking
        m.is_vip = vip;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task 设为VIP_写入会员并留审计日志_记录是哪个店员操作的()
    {
        await using var f = await CoachFixture.Create();
        var r = Env(await Controller(f).SetMemberVip(CoachFixture.StudentA, true, "session-7"));   // 职级 100 的店员（staff 10）
        Assert.Equal(0, r.code);
        Assert.True(Data(r).GetProperty("isVip").GetBoolean());
        Assert.True((await f.Db.member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentA)).is_vip);

        var log = await f.Db.coreDataModLog.AsNoTracking().SingleAsync(l => l.table_name == "member" && l.field_name == "is_vip");
        Assert.Equal(CoachFixture.StudentA, log.key_value);
        Assert.Equal("0", log.prev_value);
        Assert.Equal("1", log.current_value);
        Assert.Equal(10, log.staff_id);
    }

    [Fact]
    public async Task 取消VIP_改回并留日志()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        var r = Env(await Controller(f).SetMemberVip(CoachFixture.StudentA, false, "session-7"));
        Assert.Equal(0, r.code);
        Assert.False(Data(r).GetProperty("isVip").GetBoolean());
        Assert.False((await f.Db.member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentA)).is_vip);
        var log = await f.Db.coreDataModLog.AsNoTracking().SingleAsync(l => l.field_name == "is_vip");
        Assert.Equal("1", log.prev_value);
        Assert.Equal("0", log.current_value);
    }

    [Fact]
    public async Task 重复设置同一状态_不改数据也不重复记日志()
    {
        await using var f = await CoachFixture.Create();
        await Controller(f).SetMemberVip(CoachFixture.StudentA, true, "session-7");
        var again = Env(await Controller(f).SetMemberVip(CoachFixture.StudentA, true, "session-7"));
        Assert.Equal(0, again.code);
        Assert.Equal(1, await f.Db.coreDataModLog.CountAsync(l => l.field_name == "is_vip"));
        // 本来就不是 VIP 时再取消：同样不记日志
        var none = Env(await Controller(f).SetMemberVip(CoachFixture.StudentB, false, "session-7"));
        Assert.Equal(0, none.code);
        Assert.Equal(1, await f.Db.coreDataModLog.CountAsync(l => l.field_name == "is_vip"));
    }

    [Fact]
    public async Task 会员不存在或已被合并_不能设置()
    {
        await using var f = await CoachFixture.Create();
        Assert.Equal("会员不存在", Env(await Controller(f).SetMemberVip(9999, true, "session-7")).message);
        await f.Db.member.Where(m => m.id == CoachFixture.StudentC).ExecuteUpdateAsync(s => s.SetProperty(m => m.merge_id, CoachFixture.StudentA).SetProperty(m => m.is_merge, 1));
        var merged = Env(await Controller(f).SetMemberVip(CoachFixture.StudentC, true, "session-7"));
        Assert.Equal(1, merged.code);
        Assert.False((await f.Db.member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentC)).is_vip);
    }

    [Fact]
    public async Task 列表按VIP筛选_只返回VIP会员_并带出isVip()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await SetVip(f, CoachFixture.StudentC);

        var all = Data(Env(await Controller(f).SearchMembersByStaff("session-7")));
        Assert.Equal(7, all.GetProperty("total").GetInt32());

        var vip = Data(Env(await Controller(f).SearchMembersByStaff("session-7", vip: true)));
        Assert.Equal(2, vip.GetProperty("total").GetInt32());
        var items = vip.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { CoachFixture.StudentC, CoachFixture.StudentA }, items.Select(i => i.GetProperty("id").GetInt32()).ToArray());   // 仍按 id 倒序
        Assert.All(items, i => Assert.True(i.GetProperty("isVip").GetBoolean()));

        // vip=false / 不传：不筛，普通会员的 isVip 是 false
        var notFiltered = Data(Env(await Controller(f).SearchMembersByStaff("session-7", vip: false)));
        Assert.Equal(7, notFiltered.GetProperty("total").GetInt32());
        Assert.Contains(notFiltered.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetInt32() == CoachFixture.StudentB && !i.GetProperty("isVip").GetBoolean());
    }

    [Fact]
    public async Task 按VIP筛选和其它条件是且的关系()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await SetVip(f, CoachFixture.StudentB);
        var r = Data(Env(await Controller(f).SearchMembersByStaff("session-7", name: "学员A", vip: true)));
        Assert.Equal(1, r.GetProperty("total").GetInt32());
        Assert.Equal(CoachFixture.StudentA, r.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task 会员详情带出isVip()
    {
        await using var f = await CoachFixture.Create();
        Assert.False(Data(Env(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7"))).GetProperty("isVip").GetBoolean());
        await SetVip(f, CoachFixture.StudentA);
        Assert.True(Data(Env(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7"))).GetProperty("isVip").GetBoolean());
    }

    [Fact]
    public async Task 合并会员时VIP跟着走_源会员是VIP则目标会员成为VIP并留日志()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentA);
        await new MemberController(f.NewContext(), f.Config).MergeMember(CoachFixture.StudentA, CoachFixture.StudentB);
        Assert.True((await f.Db.member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentB)).is_vip);
        Assert.Contains(await f.Db.coreDataModLog.AsNoTracking().ToListAsync(),
            l => l.table_name == "member" && l.field_name == "is_vip" && l.key_value == CoachFixture.StudentB && l.current_value == "1");
    }

    [Fact]
    public async Task 合并会员时_目标本来是VIP而源不是_保持VIP不变()
    {
        await using var f = await CoachFixture.Create();
        await SetVip(f, CoachFixture.StudentB);
        await new MemberController(f.NewContext(), f.Config).MergeMember(CoachFixture.StudentA, CoachFixture.StudentB);
        Assert.True((await f.Db.member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentB)).is_vip);
        Assert.DoesNotContain(await f.Db.coreDataModLog.AsNoTracking().ToListAsync(), l => l.field_name == "is_vip");
    }

    [Fact]
    public async Task is_vip带索引_VIP筛选走索引而不是关联标签表()
    {
        await using var f = await CoachFixture.Create();
        var index = f.Db.Model.FindEntityType(typeof(Member))!.GetIndexes()
            .SingleOrDefault(i => i.GetDatabaseName() == "ix_member_is_vip");
        Assert.NotNull(index);
        Assert.Equal(new[] { "is_vip", "id" }, index!.Properties.Select(p => p.Name).ToArray());
    }
}

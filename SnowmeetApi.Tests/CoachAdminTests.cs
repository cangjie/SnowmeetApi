using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

public class CoachAdminTests
{
    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    [Fact]
    public async Task Controller_OnlySystemAdminsAndValidSessions()
    {
        await using var f = await CoachFixture.Create();
        var ctl = () => new CoachAdminController(f.NewContext(), f.Config);
        Assert.Equal(2, (await ctl().ListCoaches("bad-session")).code);
        Assert.Equal(3, (await ctl().ListCoaches("session-3")).code);   // 学员
        Assert.Equal(3, (await ctl().ListCoaches("session-7")).code);   // 店员职级 100，不够 300
        var ok = await ctl().ListCoaches("session-1");
        Assert.Equal(0, ok.code);
        Assert.Equal(2, D(ok).GetProperty("coaches").GetArrayLength());
        Assert.Equal(2, D(ok).GetProperty("stats").GetProperty("coaches").GetInt32());
        Assert.Equal(3, (await ctl().GrantCoupons("session-7", new(new() { f.CoachId }, 12, 1, "r1"))).code);
    }

    [Fact]
    public async Task AddCoach_ByNewPhoneCreatesMember_ExistingMemberAndDuplicateRules()
    {
        await using var f = await CoachFixture.Create();
        Staff op = (await f.Db.staff.AsNoTracking().FirstAsync(s => s.id == 9));
        // 手机号还没有会员 → 同时创建会员
        var created = await f.Admin_().AddCoachAsync(op, null, "13911112222", "王教练");
        Assert.Equal("王教练", created.display_name);
        var member = await f.Db.member.AsNoTracking().FirstAsync(m => m.id == created.member_id);
        Assert.Equal("后台添加教练", member.source);
        Assert.True(await f.Db.memberSocialAccount.AnyAsync(a => a.member_id == member.id && a.type == "cell" && a.num == "13911112222"));
        // 已有会员：按手机号找
        var byCell = await f.Admin_().AddCoachAsync(op, null, "13800001003", null);   // 学员A（member 3）
        Assert.Equal(CoachFixture.StudentA, byCell.member_id);
        Assert.Equal("学员A", byCell.display_name);
        // 重复添加
        var dup = await Assert.ThrowsAsync<CoachException>(() => f.Admin_().AddCoachAsync(op, CoachFixture.StudentA, null, null));
        Assert.Contains("已经是教练", dup.Message);
        // 找不到会员
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().AddCoachAsync(op, 9999, null, null));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().AddCoachAsync(op, null, "123", null));
        // 停用后再添加 = 恢复原记录，价格保留
        await f.Admin_().UpdateCoachAsync(f.CoachId, null, false);
        var restored = await f.Admin_().AddCoachAsync(op, CoachFixture.CoachMember, null, "张教练（新）");
        Assert.Equal(f.CoachId, restored.id);
        Assert.Equal(1, restored.valid);
        Assert.Equal(4, (await f.Admin_().GetCarePricesAsync(f.CoachId)).Count);
    }

    [Fact]
    public async Task LookupMember_ReportsExistingAndCoachFlag()
    {
        await using var f = await CoachFixture.Create();
        var hit = JsonSerializer.SerializeToElement(await f.Admin_().LookupMemberAsync("13800001002"));
        Assert.True(hit.GetProperty("exists").GetBoolean());
        Assert.True(hit.GetProperty("isCoach").GetBoolean());
        Assert.Equal("1002", hit.GetProperty("phoneTail").GetString());
        var miss = JsonSerializer.SerializeToElement(await f.Admin_().LookupMemberAsync("13999990000"));
        Assert.False(miss.GetProperty("exists").GetBoolean());
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().LookupMemberAsync("12"));
    }

    [Fact]
    public async Task SetPricing_ReplacesAllFourCellsAndValidates()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await f.Admin_().SetPricingAsync(f.CoachId, CoachFixture.Standard());
        var rows = await f.Admin_().GetCarePricesAsync(f.CoachId);
        Assert.Equal(4, rows.Count);
        var coach = await f.Db.coach.AsNoTracking().FirstAsync(c => c.id == f.CoachId);
        Assert.Equal(0.9, coach.rental_rate);
        // 整体替换：没传的格子记为「无」，租赁 null = 不优惠
        await f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("崇礼旗舰店", "单项", "fixed", 50) }, null));
        rows = await f.Admin_().GetCarePricesAsync(f.CoachId);
        Assert.Equal(4, rows.Count);
        Assert.Equal("fixed", rows.Single(r => r.shop == "崇礼旗舰店" && r.tier == "单项").mode);
        Assert.Equal("none", rows.Single(r => r.shop == "万龙服务中心" && r.tier == "双项").mode);
        Assert.Null((await f.Db.coach.AsNoTracking().FirstAsync(c => c.id == f.CoachId)).rental_rate);
        // 校验
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("南山", "单项", "rate", 0.8) }, null)));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("万龙服务中心", "三项", "rate", 0.8) }, null)));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("万龙服务中心", "单项", "rate", 1.5) }, null)));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("万龙服务中心", "单项", "fixed", -1) }, null)));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(null, 2.0)));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().SetPricingAsync(9999, CoachFixture.Standard()));
    }

    [Fact]
    public async Task GrantCoupons_IssuesToCoachMember_IdempotentOnRetry()
    {
        await using var f = await CoachFixture.Create();
        Staff op = await f.Db.staff.AsNoTracking().FirstAsync(s => s.id == 9);
        var first = await f.Admin_().GrantCouponsAsync(op, new() { f.CoachId, f.Coach2Id }, 16, 3, "req-1");
        Assert.Equal(6, first.granted);
        Assert.Equal(0, first.skipped);
        var tickets = await f.Db.ticket.AsNoTracking().Where(t => t.channel == CoachAdminService.CouponChannel).ToListAsync();
        Assert.Equal(6, tickets.Count);
        Assert.All(tickets, t => { Assert.Equal(16, t.template_id); Assert.Equal(9, t.staff_id); Assert.Equal(1, t.valid); Assert.Equal(0, t.used); });
        Assert.Equal(3, tickets.Count(t => t.member_id == CoachFixture.CoachMember && t.open_id == "mini-2"));
        Assert.Equal(6, await f.Db.card.AsNoTracking().CountAsync(c => c.is_ticket == 1));
        Assert.Equal(6, await f.Db.coachCouponGrant.AsNoTracking().CountAsync());
        // 同一个 requestId 重试：不重复发
        var retry = await f.Admin_().GrantCouponsAsync(op, new() { f.CoachId, f.Coach2Id }, 16, 3, "req-1");
        Assert.Equal(0, retry.granted);
        Assert.Equal(6, retry.skipped);
        Assert.Equal(6, await f.Db.ticket.AsNoTracking().CountAsync(t => t.channel == CoachAdminService.CouponChannel));
        // 部分失败后重试：只补发缺的（把一个教练的 3 张删到 1 张模拟）
        var keys = await f.Db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == f.Coach2Id).OrderBy(g => g.id).Select(g => g.grant_key).ToListAsync();
        await f.Db.coachCouponGrant.Where(g => keys.Skip(1).Contains(g.grant_key)).ExecuteDeleteAsync();
        var fill = await f.Admin_().GrantCouponsAsync(op, new() { f.CoachId, f.Coach2Id }, 16, 3, "req-1");
        Assert.Equal(2, fill.granted);
        // 新的 requestId = 新一批
        Assert.Equal(2, (await f.Admin_().GrantCouponsAsync(op, new() { f.CoachId }, 12, 2, "req-2")).granted);
    }

    [Fact]
    public async Task GrantCoupons_Validation()
    {
        await using var f = await CoachFixture.Create();
        Staff op = await f.Db.staff.AsNoTracking().FirstAsync(s => s.id == 9);
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new(), 16, 1, "r"));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, null, 16, 1, "r"));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new() { f.CoachId }, 16, 0, "r"));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new() { f.CoachId }, 16, 51, "r"));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new() { f.CoachId }, 16, 1, ""));
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new() { f.CoachId }, 777, 1, "r"));       // 模板不存在
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new() { 9999 }, 16, 1, "r"));             // 教练不存在
        await f.Admin_().UpdateCoachAsync(f.Coach2Id, null, false);
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, new() { f.CoachId, f.Coach2Id }, 16, 1, "r")); // 含已停用教练
        // 总数上限
        var many = Enumerable.Range(1, 41).ToList();
        await Assert.ThrowsAsync<CoachException>(() => f.Admin_().GrantCouponsAsync(op, many, 16, 50, "r"));
        Assert.Empty(await f.Db.ticket.AsNoTracking().ToListAsync());
    }
}

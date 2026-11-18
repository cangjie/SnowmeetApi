using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Tests;

// 分销商「制作优惠券」改成批次分享（用户 2026-11-18）：每做一次 = 一个批次（规定张数、一个二维码 / 小程序卡片），
// 创建批次时不生成券，顾客领取时才生成；领满不能再领；每个 openid 每天只能领一次、同一批次也只能领一次；没人领取过的批次可删除。
public class CoachCouponBatchTests
{
    private const string Wl = "万龙服务中心";

    // 教练 2（会员 6）= 机构，万龙单项优惠券底价：一口价 ≥ 60 / 折扣 ≥ 7 折 / 减免 ≤ 30；教练 1（会员 2）= 教练，万龙单项 / 双项只开放减免 ≤ 50
    // （SQLite 测试库里同一个商品只能挂一个券模板的价格规则，所以「另一个分销商」的批次用双项，和机构的单项不撞）
    private static async Task<CoachFixture> NewFixture()
    {
        var f = await CoachFixture.Create(withPricing: false);
        foreach (var (member, coach, type) in new[] { (CoachFixture.CoachMember, f.CoachId, "教练"), (CoachFixture.CoachMember2, f.Coach2Id, "机构") })
        {
            var relation = new OrderShareRelation { name = type + member, type = "个人", valid = true };
            f.Db.orderShareRelation.Add(relation);
            await f.Db.SaveChangesAsync();
            f.Db.introducer.Add(new Introducer { member_id = member, coach_id = coach, type = type, name = member == CoachFixture.CoachMember ? "某教练" : "某机构", relation_id = relation.id, valid = 1 });
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
        var admin = new CoachAdminController(f.NewContext(), f.Config);
        Assert.Equal(0, (await admin.SetCouponFloors("session-1", new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "单项", 60, 0.7, 30), new(Wl, "双项", null, 0.7, null) }))).code);
        Assert.Equal(0, (await admin.SetCouponFloors("session-1", new CoachAdminController.SetCouponFloorsInput(f.CoachId, new() { new(Wl, "单项", null, null, 50), new(Wl, "双项", null, null, 50) }))).code);
        return f;
    }

    private static JsonElement J(object? data) => JsonSerializer.SerializeToElement(data);
    private static CoachCouponBatchService Svc(CoachFixture f) => new(f.NewContext(), f.Config);
    private static DistributorController Dist(CoachFixture f) => new(f.NewContext(), f.Config);
    private static CouponBatchController Claimer(CoachFixture f) => new(f.NewContext(), f.Config);

    private static async Task<(Coach coach, Introducer intro)> Own(CoachFixture f, int coachId)
        => (await f.NewContext().coach.AsNoTracking().SingleAsync(c => c.id == coachId), await f.NewContext().introducer.AsNoTracking().SingleAsync(i => i.coach_id == coachId));

    private static async Task<CoachCouponBatch> Batch(CoachFixture f, int count = 3, string req = "r1", string mode = "fixed", double value = 68, int? coachId = null)
    {
        var (coach, intro) = await Own(f, coachId ?? f.Coach2Id);
        return (await Svc(f).CreateAsync(coach, intro, new CoachCouponBatchService.CreateInput(Wl, "单项", mode, value, count, req), DateTime.Now)).batch;
    }

    private static async Task<CouponShareView> Claim(CoachFixture f, CoachCouponBatch batch, int memberId, DateTime? now = null, string? openId = null)
    {
        var member = await f.NewContext().member.AsNoTracking().SingleAsync(m => m.id == memberId);
        return await Svc(f).ClaimAsync(batch.token, member, openId ?? "mini-" + memberId, now ?? DateTime.Now);
    }

    private static Task<int> TicketCount(CoachFixture f) => f.NewContext().ticket.AsNoTracking().CountAsync(t => t.channel == CoachCouponMakerService.Channel);

    // ---------- 创建批次：不生成券 ----------

    [Fact]
    public async Task Creating_a_batch_records_the_size_and_share_token_but_makes_no_tickets()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, count: 10);
        Assert.Equal((10, 0, 1), (batch.total_count, batch.claimed_count, batch.valid));
        Assert.Matches("^[0-9a-f]{24}$", batch.token);
        Assert.Equal(0, await TicketCount(f));                                                           // 创建时不生成券
        Assert.Equal(0, await f.NewContext().coachCouponGrant.AsNoTracking().CountAsync());
        var tpl = await f.NewContext().ticketTemplate.AsNoTracking().SingleAsync(t => t.id == batch.template_id);
        Assert.Equal((1, 0, "养护券"), (tpl.hide, tpl.sharable, tpl.type));                                // 隐藏的养护券模板
        Assert.Contains("68元一口价", tpl.name);
        Assert.Equal(1, await f.NewContext().productTicketTemplate.AsNoTracking().CountAsync(r => r.ticket_template_id == tpl.id && r.fixed_price == 68));
        Assert.Equal(1, await f.NewContext().coreDataModLog.AsNoTracking().CountAsync(l => l.table_name == "coach_coupon_batch" && l.key_value == batch.id));
    }

    [Fact]
    public async Task Batch_prices_are_held_to_the_admins_coupon_floors_and_inputs_are_checked()
    {
        await using var f = await NewFixture();
        var (coach, intro) = await Own(f, f.Coach2Id);
        async Task<string> Bad(string? shop, string? tier, string? mode, double value, int count, string? req = "x")
            => (await Assert.ThrowsAsync<CoachException>(() => Svc(f).CreateAsync(coach, intro, new(shop, tier, mode, value, count, req), DateTime.Now))).Message;
        Assert.Contains("不能低于", await Bad(Wl, "单项", "fixed", 59, 5));
        Assert.Contains("不能低于", await Bad(Wl, "单项", "rate", 0.6, 5));
        Assert.Contains("不能超过", await Bad(Wl, "单项", "off", 31, 5));
        Assert.Contains("没有开放", await Bad("崇礼旗舰店", "单项", "fixed", 100, 5));                         // 管理员没给这一格设底价
        Assert.Contains("只能用于", await Bad("南山", "单项", "fixed", 100, 5));
        Assert.Contains("1～", await Bad(Wl, "单项", "fixed", 68, 0));
        Assert.Contains("1～", await Bad(Wl, "单项", "fixed", 68, CoachCouponBatchService.MaxBatchSize + 1));
        Assert.Contains("请求编号", await Bad(Wl, "单项", "fixed", 68, 5, " "));
        Assert.Equal(0, await f.NewContext().coachCouponBatch.AsNoTracking().CountAsync());
        // 教练只开放了减免：一口价不行
        var (c1, i1) = await Own(f, f.CoachId);
        Assert.Contains("没有开放一口价", (await Assert.ThrowsAsync<CoachException>(() => Svc(f).CreateAsync(c1, i1, new(Wl, "单项", "fixed", 100, 5, "x"), DateTime.Now))).Message);
        Assert.Equal(0, (await Svc(f).CreateAsync(c1, i1, new(Wl, "单项", "off", 50, 5, "x"), DateTime.Now)).batch.claimed_count);
    }

    [Fact]
    public async Task Resubmitting_the_same_request_id_returns_the_same_batch()
    {
        await using var f = await NewFixture();
        var (coach, intro) = await Own(f, f.Coach2Id);
        var first = await Svc(f).CreateAsync(coach, intro, new(Wl, "单项", "fixed", 68, 5, "same"), DateTime.Now);
        var again = await Svc(f).CreateAsync(coach, intro, new(Wl, "单项", "fixed", 68, 5, "same"), DateTime.Now);
        Assert.False(first.repeated); Assert.True(again.repeated);
        Assert.Equal(first.batch.id, again.batch.id);
        Assert.Equal(1, await f.NewContext().coachCouponBatch.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Batches_of_the_same_price_share_one_template_and_the_remaining_capacity_is_capped()
    {
        await using var f = await NewFixture();
        var a = await Batch(f, 100, "a"); var b = await Batch(f, 100, "b");
        Assert.Equal(a.template_id, b.template_id);
        Assert.Equal(1, await f.NewContext().ticketTemplate.AsNoTracking().CountAsync(t => t.id == a.template_id));
        var (coach, intro) = await Own(f, f.Coach2Id);
        await Batch(f, 500, "c"); await Batch(f, 300, "d");                                              // 剩余合计 1000
        var full = await Assert.ThrowsAsync<CoachException>(() => Svc(f).CreateAsync(coach, intro, new(Wl, "单项", "fixed", 68, 1, "e"), DateTime.Now));
        Assert.Contains("合计上限", full.Message);
        // 领走或删掉一些就腾出额度
        Assert.Equal(CouponBatchState.ClaimedByMe, (await Claim(f, a, CoachFixture.StudentA)).state);
        await Svc(f).DeleteAsync(coach, intro, b.id, DateTime.Now);
        Assert.Equal(0, (await Svc(f).CreateAsync(coach, intro, new(Wl, "单项", "fixed", 68, 101, "e"), DateTime.Now)).batch.claimed_count);
    }

    // ---------- 领取：这时才生成券 ----------

    [Fact]
    public async Task A_ticket_is_generated_only_when_someone_claims_and_it_belongs_to_the_claimer()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        Assert.Equal(0, await TicketCount(f));
        var view = await Claim(f, batch, CoachFixture.StudentA);
        Assert.Equal(CouponBatchState.ClaimedByMe, view.state);
        Assert.Matches("^[0-9A-Za-z]{9}$", view.code!);
        Assert.Equal(1, await TicketCount(f));

        var ticket = await f.NewContext().ticket.AsNoTracking().SingleAsync(t => t.code == view.code);
        Assert.Equal((CoachFixture.StudentA, "mini-3", batch.template_id, 0, 1), (ticket.member_id, ticket.open_id, ticket.template_id, ticket.used, ticket.valid));
        Assert.Equal(CoachCouponMakerService.Channel, ticket.channel);
        Assert.Equal(1, ticket.is_active);
        Assert.NotNull(ticket.expire_date);
        // 发放记录、领取记录、批次进度
        var grant = await f.NewContext().coachCouponGrant.AsNoTracking().SingleAsync(g => g.ticket_code == view.code);
        Assert.Equal((f.Coach2Id, batch.template_id), (grant.coach_id, grant.template_id));
        var claim = await f.NewContext().coachCouponClaim.AsNoTracking().SingleAsync(c => c.ticket_code == view.code);
        Assert.Equal((batch.id, CoachFixture.StudentA, "mini-3"), (claim.batch_id, claim.member_id, claim.open_id));
        Assert.Equal(DateTime.Now.Date, claim.claim_date);
        Assert.Equal(1, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).claimed_count);
        Assert.Equal(2, view.remaining);
        // 顾客归属：先到先得，领券的顾客成为这位分销商的顾客
        var intro = await f.NewContext().introducer.AsNoTracking().SingleAsync(i => i.coach_id == f.Coach2Id);
        Assert.Equal(intro.id, (await f.NewContext().distributorCustomer.AsNoTracking().SingleAsync(c => c.member_id == CoachFixture.StudentA && c.valid == 1)).introducer_id);
    }

    [Fact]
    public async Task The_claimed_ticket_is_priced_by_the_existing_rules_and_shows_in_the_distributors_coupon_list_as_accepted()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        var code = (await Claim(f, batch, CoachFixture.StudentA)).code!;
        // 开单计价：券的价格规则挂在模板上，一口价 68（单项）
        var db = f.NewContext();
        var ticket = await db.ticket.Include(t => t.template).ThenInclude(t => t.productTicketTemplates).AsNoTracking().SingleAsync(t => t.code == code);
        var (charge, _) = await new SnowmeetApi.Controllers.CareController(db, f.Config, ShareFixture.Http).CalcCharge(Wl, new Care { need_edge = 1, need_wax = 0, urgent = 0, valid = 1 }, ticket);
        Assert.Equal(68, charge);
        // 分销商的「我的券」：这张券已被顾客领取，带领取人
        var coupons = J((await new CoachController(f.NewContext(), f.Config).GetMyCoupons("session-6")).data).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("accepted", coupons.GetProperty("state").GetString());
        Assert.Equal("学员A", coupons.GetProperty("acceptedName").GetString());
    }

    [Fact]
    public async Task The_same_member_can_claim_a_batch_only_once_and_gets_the_same_ticket_back()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 5);
        var first = await Claim(f, batch, CoachFixture.StudentA);
        // 同一天再点、第二天再点：都是「已领取」并带同一张券，不再生成、不占名额
        foreach (var day in new[] { DateTime.Now, DateTime.Now.AddDays(1) })
        {
            var again = await Claim(f, batch, CoachFixture.StudentA, day);
            Assert.Equal(CouponBatchState.ClaimedByMe, again.state);
            Assert.Equal(first.code, again.code);
        }
        Assert.Equal(1, await TicketCount(f));
        Assert.Equal(1, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).claimed_count);
    }

    [Fact]
    public async Task One_claim_per_openid_per_day_across_all_batches_but_the_next_day_is_fine()
    {
        await using var f = await NewFixture();
        var a = await Batch(f, 5, "a");
        var b = await Batch(f, 5, "b");                                                                  // 同价格的另一个批次
        var (c1, i1) = await Own(f, f.CoachId);
        var other = (await Svc(f).CreateAsync(c1, i1, new(Wl, "双项", "off", 40, 5, "o"), DateTime.Now)).batch;   // 另一个分销商的批次

        Assert.Equal(CouponBatchState.ClaimedByMe, (await Claim(f, a, CoachFixture.StudentA)).state);
        foreach (var blocked in new[] { b, other })
        {
            var view = await Claim(f, blocked, CoachFixture.StudentA);
            Assert.Equal(CouponBatchState.DailyLimit, view.state);
            Assert.Contains("今天", view.message);
            Assert.Null(view.code);
        }
        Assert.Equal(1, await TicketCount(f));
        Assert.Equal(0, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(x => x.id == b.id)).claimed_count);   // 被拦下的没占名额
        // 打开领取页时就能看到「今天已领过」，不用点领取
        Assert.Equal(CouponBatchState.DailyLimit, (await Svc(f).GetShareAsync(b.token, CoachFixture.StudentA, "mini-3", DateTime.Now)).state);
        // 第二天可以领别的批次
        Assert.Equal(CouponBatchState.ClaimedByMe, (await Claim(f, b, CoachFixture.StudentA, DateTime.Now.AddDays(1))).state);
        Assert.Equal(2, await TicketCount(f));
    }

    [Fact]
    public async Task The_limits_follow_the_openid_not_just_the_member_id()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 5);
        Assert.Equal(CouponBatchState.ClaimedByMe, (await Claim(f, batch, CoachFixture.StudentA)).state);
        // 另一个会员记录但是同一个 openid：同一批次不能再领一张
        var second = await Claim(f, batch, CoachFixture.StudentB, openId: "mini-3");
        Assert.NotEqual(CouponBatchState.Claimable, second.state);
        Assert.Equal(1, await TicketCount(f));
        Assert.Equal(1, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).claimed_count);
    }

    // ---------- 领满不能再领 ----------

    [Fact]
    public async Task When_the_claimed_count_reaches_the_batch_size_nobody_else_can_claim()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 2);
        Assert.Equal(CouponBatchState.ClaimedByMe, (await Claim(f, batch, CoachFixture.StudentA)).state);
        Assert.Equal(CouponBatchState.ClaimedByMe, (await Claim(f, batch, CoachFixture.StudentB)).state);
        var late = await Claim(f, batch, CoachFixture.StudentC);
        Assert.Equal(CouponBatchState.SoldOut, late.state);
        Assert.Equal(2, await TicketCount(f));
        Assert.Equal(2, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).claimed_count);
        // 领完后，领过的人再打开仍是「已领取」（带券码），没领过的是「已领完」
        Assert.Equal(CouponBatchState.ClaimedByMe, (await Svc(f).GetShareAsync(batch.token, CoachFixture.StudentA, "mini-3", DateTime.Now)).state);
        Assert.Equal(CouponBatchState.SoldOut, (await Svc(f).GetShareAsync(batch.token, CoachFixture.StudentC, "mini-5", DateTime.Now)).state);
    }

    [Fact]
    public async Task Concurrent_claims_of_the_last_slot_give_exactly_one_ticket()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 1);
        var results = await Task.WhenAll(new[] { CoachFixture.StudentA, CoachFixture.StudentB, CoachFixture.StudentC }.Select(m => Claim(f, batch, m)));
        Assert.Equal(1, results.Count(r => r.state == CouponBatchState.ClaimedByMe));
        Assert.Equal(2, results.Count(r => r.state == CouponBatchState.SoldOut));
        Assert.Equal(1, await TicketCount(f));
        Assert.Equal(1, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).claimed_count);
        Assert.Equal(1, await f.NewContext().coachCouponClaim.AsNoTracking().CountAsync());
    }

    // ---------- 其它状态 ----------

    [Fact]
    public async Task The_distributor_cannot_claim_their_own_batch_and_bad_batches_are_invalid()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        var self = await Claim(f, batch, CoachFixture.CoachMember2);
        Assert.Equal(CouponBatchState.Self, self.state);
        Assert.Equal(0, await TicketCount(f));
        Assert.Equal(0, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).claimed_count);
        foreach (var bad in new[] { "", "short", "has space in token 1234567", "zzzzzzzzzzzzzzzzzzzzzzzz" })
            Assert.Equal(CouponBatchState.Invalid, (await Svc(f).GetShareAsync(bad, CoachFixture.StudentA, "mini-3", DateTime.Now)).state);
    }

    [Fact]
    public async Task A_stopped_distributor_or_a_disabled_template_or_an_expired_season_cannot_be_claimed()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        // 过了有效期
        var expired = await Svc(f).GetShareAsync(batch.token, CoachFixture.StudentA, "mini-3", DateTime.Now.AddYears(2));
        Assert.Equal(CouponBatchState.Expired, expired.state);
        Assert.Equal(CouponBatchState.Expired, (await Claim(f, batch, CoachFixture.StudentA, DateTime.Now.AddYears(2))).state);
        // 券模板被停用
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE ticket_template SET valid = 0 WHERE id = " + batch.template_id);
        Assert.Equal(CouponBatchState.Invalid, (await Claim(f, batch, CoachFixture.StudentA)).state);
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE ticket_template SET valid = 1 WHERE id = " + batch.template_id);
        Assert.Equal(CouponBatchState.Claimable, (await Svc(f).GetShareAsync(batch.token, CoachFixture.StudentA, "mini-3", DateTime.Now)).state);
        // 分销商被停用
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE coach SET valid = 0 WHERE id = " + f.Coach2Id);
        Assert.Equal(CouponBatchState.Invalid, (await Claim(f, batch, CoachFixture.StudentA)).state);
        Assert.Equal(0, await TicketCount(f));
    }

    // ---------- 删除批次 ----------

    [Fact]
    public async Task A_batch_nobody_claimed_can_be_deleted_and_then_its_link_stops_working()
    {
        await using var f = await NewFixture();
        var (coach, intro) = await Own(f, f.Coach2Id);
        var batch = await Batch(f, 3);
        var res = await Dist(f).DeleteMyCouponBatch("session-6", new DistributorController.DeleteCouponBatchInput(batch.id));
        Assert.Equal(0, res.code);
        var row = await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id);
        Assert.Equal(0, row.valid); Assert.NotNull(row.deleted_at);                                       // 软删除：行还在
        Assert.Equal(CouponBatchState.Invalid, (await Claim(f, batch, CoachFixture.StudentA)).state);
        Assert.Empty(await Svc(f).ListAsync(coach));
        Assert.Equal(1, await f.NewContext().coreDataModLog.AsNoTracking().CountAsync(l => l.table_name == "coach_coupon_batch" && l.key_value == batch.id && l.scene == "机构删除优惠券批次"));
        Assert.NotEqual(0, (await Dist(f).DeleteMyCouponBatch("session-6", new DistributorController.DeleteCouponBatchInput(batch.id))).code);   // 再删：已经没有
    }

    [Fact]
    public async Task A_batch_somebody_claimed_cannot_be_deleted_and_only_the_owner_can_delete()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        await Claim(f, batch, CoachFixture.StudentA);
        var refused = await Dist(f).DeleteMyCouponBatch("session-6", new DistributorController.DeleteCouponBatchInput(batch.id));
        Assert.Equal(1, refused.code); Assert.Contains("领取过", refused.message);
        Assert.Equal(1, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == batch.id)).valid);
        var other = await Batch(f, 3, "r2", coachId: null);
        // 别的分销商、普通会员、没登录都删不了
        foreach (var session in new[] { "session-2", "session-3", "nope" })
            Assert.NotEqual(0, (await Dist(f).DeleteMyCouponBatch(session, new DistributorController.DeleteCouponBatchInput(other.id))).code);
        Assert.Equal(1, (await f.NewContext().coachCouponBatch.AsNoTracking().SingleAsync(b => b.id == other.id)).valid);
    }

    // ---------- 分销商看批次 ----------

    [Fact]
    public async Task The_maker_view_lists_batches_with_progress_claimers_and_share_paths()
    {
        await using var f = await NewFixture();
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE member SET nick_name = '雪友小A', avatar = '/upload/20261118/avatar_a.jpg' WHERE id = " + CoachFixture.StudentA);
        var older = await Batch(f, 5, "a");
        var newer = await Batch(f, 2, "b");
        var code = (await Claim(f, older, CoachFixture.StudentA)).code!;
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE ticket SET used = 1 WHERE code = '" + code + "'");
        await Claim(f, newer, CoachFixture.StudentB, DateTime.Now.AddDays(1));

        var view = J((await Dist(f).GetMyCouponMaker("session-6")).data);
        var batches = view.GetProperty("batches").EnumerateArray().ToList();
        Assert.Equal(new[] { newer.id, older.id }.OrderByDescending(x => x), batches.Select(b => b.GetProperty("id").GetInt32()));   // 新的在前
        var a = batches.Single(b => b.GetProperty("id").GetInt32() == older.id);
        Assert.Equal((5, 1, 4, false, false), (a.GetProperty("total").GetInt32(), a.GetProperty("claimed").GetInt32(), a.GetProperty("remaining").GetInt32(), a.GetProperty("soldOut").GetBoolean(), a.GetProperty("canDelete").GetBoolean()));
        Assert.Equal("/pages/coach/coupon_claim/coupon_claim?c=" + older.token, a.GetProperty("sharePath").GetString());
        var claimer = a.GetProperty("claimers").EnumerateArray().Single();
        Assert.Equal(("雪友小A", "https://img.snowmeet.top/upload/20261118/avatar_a.jpg", code, true), (claimer.GetProperty("nickName").GetString(), claimer.GetProperty("avatarUrl").GetString(), claimer.GetProperty("ticketCode").GetString(), claimer.GetProperty("used").GetBoolean()));
        var fresh = await Batch(f, 4, "c");
        var all = J((await Dist(f).GetMyCouponMaker("session-6")).data).GetProperty("batches").EnumerateArray().Single(b => b.GetProperty("id").GetInt32() == fresh.id);
        Assert.True(all.GetProperty("canDelete").GetBoolean());                                           // 没人领取过的可删
        Assert.Equal(CoachCouponBatchService.MaxBatchSize, view.GetProperty("maxBatchSize").GetInt32());
    }

    [Fact]
    public async Task Batch_tickets_do_not_count_as_the_distributors_own_outstanding_stock_and_the_old_generate_endpoint_still_works()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 5);
        await Claim(f, batch, CoachFixture.StudentA);
        var view = J((await Dist(f).GetMyCouponMaker("session-6")).data);
        Assert.Equal(0, view.GetProperty("outstanding").GetInt32());                                      // 领走的券已经是顾客的
        var made = await Dist(f).MakeMyCoupons("session-6", new DistributorController.MakeCouponsInput(Wl, "单项", "fixed", 68, 3, "old1"));   // 旧版小程序的一次性生成
        Assert.Equal(0, made.code);
        Assert.Equal(3, J(J(made.data).GetProperty("maker")).GetProperty("outstanding").GetInt32());
    }

    // ---------- 领取页接口 ----------

    [Fact]
    public async Task The_claim_endpoints_need_a_login_and_a_member_and_return_the_view()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        Assert.Equal(2, (await Claimer(f).GetShare(batch.token, "nope")).code);
        Assert.Equal(2, (await Claimer(f).Claim(batch.token, "nope")).code);
        // 还没授权手机号（没有会员）：能看，不能领
        var anon = J((await Claimer(f).GetShare(batch.token, "session-new")).data);
        Assert.True(anon.GetProperty("needMember").GetBoolean());
        Assert.Equal("claimable", anon.GetProperty("view").GetProperty("state").GetString());
        Assert.Equal(5, (await Claimer(f).Claim(batch.token, "session-new")).code);
        Assert.Equal(0, await TicketCount(f));
        // 会员：看到券名、说明、剩余；领取后是 claimed_by_me + 券码
        var share = J((await Claimer(f).GetShare(batch.token, "session-3")).data).GetProperty("view");
        Assert.Equal((3, 3, "某机构"), (share.GetProperty("total").GetInt32(), share.GetProperty("remaining").GetInt32(), share.GetProperty("distributorName").GetString()));
        Assert.Contains("68元一口价", share.GetProperty("name").GetString());
        var claimed = J((await Claimer(f).Claim(batch.token, "session-3")).data);
        Assert.False(claimed.GetProperty("needMember").GetBoolean());
        Assert.Equal("claimed_by_me", claimed.GetProperty("view").GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(claimed.GetProperty("view").GetProperty("code").GetString()));
    }

    // ---------- 分享二维码 ----------

    private static MiniStaffBookingQrService Qr(MiniTicketQrTests.Handler handler) => new(new MiniTicketQrTests.Tokens(), new HttpClient(handler));

    [Fact]
    public async Task The_batch_qr_encodes_only_the_batch_token_and_lands_on_the_coupon_claim_page()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        var handler = new MiniTicketQrTests.Handler();
        var response = Assert.IsType<FileContentResult>(await Dist(f).GetCouponBatchQr("session-6", batch.id, Qr(handler)));
        Assert.Equal("image/png", response.ContentType);
        var body = JsonDocument.Parse(handler.Requests.Single()).RootElement;
        Assert.Equal("pages/coach/coupon_claim/coupon_claim", body.GetProperty("page").GetString());
        Assert.Equal("c=" + batch.token, body.GetProperty("scene").GetString());
        Assert.True(body.GetProperty("scene").GetString()!.Length <= 32);
    }

    [Fact]
    public async Task The_batch_qr_is_only_for_the_owner_and_not_for_deleted_batches()
    {
        await using var f = await NewFixture();
        var batch = await Batch(f, 3);
        var handler = new MiniTicketQrTests.Handler(); var qr = Qr(handler);
        Assert.IsType<BadRequestObjectResult>(await Dist(f).GetCouponBatchQr("nope", batch.id, qr));
        Assert.IsType<BadRequestObjectResult>(await Dist(f).GetCouponBatchQr("session-3", batch.id, qr));    // 不是分销商
        Assert.IsType<BadRequestObjectResult>(await Dist(f).GetCouponBatchQr("session-2", batch.id, qr));    // 别的分销商
        Assert.IsType<BadRequestObjectResult>(await Dist(f).GetCouponBatchQr("session-6", batch.id + 999, qr));
        Assert.Equal(0, handler.Calls);
        await Dist(f).DeleteMyCouponBatch("session-6", new DistributorController.DeleteCouponBatchInput(batch.id));
        Assert.IsType<BadRequestObjectResult>(await Dist(f).GetCouponBatchQr("session-6", batch.id, qr));
        Assert.Equal(0, handler.Calls);
        foreach (var bad in new[] { "", "short", "has space in token 1234567", new string('a', 31) })
            await Assert.ThrowsAsync<ArgumentException>(() => qr.GetForCouponBatchAsync(bad));
    }
}

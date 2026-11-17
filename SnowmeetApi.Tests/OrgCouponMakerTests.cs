using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

// 分销商（教练 / KOL / 机构）自己生成单张优惠券：按管理员设的「优惠券底价」（原来的价格设置）生成一批券放进自己的券包，再转发给顾客。
// 优惠券底价按一口价（不能低于）、折扣（不能低于）、减免（不能超过）三种方式各自设置，没设的方式没有开放。
// 一批券 = 一个隐藏的养护券模板 + 模板上针对单项 / 双项养护商品的价格规则（复用现有开单计价）+ N 张券 + 分销商发券登记。
// （文件名沿用早期只给机构的版本；2026-11-17 起教练和 KOL 也能发券、底价换成优惠券底价。）
public class OrgCouponMakerTests
{
    private const string Wl = "万龙服务中心", Cl = "崇礼旗舰店";

    // 教练 1（会员 2）默认是「教练」，教练 2（会员 6）是「机构」。
    // 机构的优惠券底价：万龙单项 一口价 ≥ 60 / 折扣 ≥ 7 折 / 减免 ≤ 30；万龙双项 只开放折扣 ≥ 7 折；崇礼单项 只开放一口价 ≥ 100；崇礼双项 没开放。
    // 教练 1 的优惠券底价：万龙单项 只开放减免 ≤ 50；崇礼单项 一口价 ≥ 80 / 折扣 ≥ 8 折。
    private static async Task<CoachFixture> NewFixture(bool withFloors = true, string firstType = "教练")
    {
        var f = await CoachFixture.Create(withPricing: false);
        foreach (var (member, coach, type) in new[] { (CoachFixture.CoachMember, f.CoachId, firstType), (CoachFixture.CoachMember2, f.Coach2Id, "机构") })
        {
            var relation = new OrderShareRelation { name = type + member, type = "个人", valid = true };
            f.Db.orderShareRelation.Add(relation);
            await f.Db.SaveChangesAsync();
            f.Db.introducer.Add(new Introducer { member_id = member, coach_id = coach, type = type, name = member == CoachFixture.CoachMember ? "某教练" : "某机构", relation_id = relation.id, valid = 1 });
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
        if (withFloors)
        {
            await SetCouponFloors(f, f.Coach2Id, new() { new(Wl, "单项", 60, 0.7, 30), new(Wl, "双项", null, 0.7, null), new(Cl, "单项", 100, null, null) });
            await SetCouponFloors(f, f.CoachId, new() { new(Wl, "单项", null, null, 50), new(Cl, "单项", 80, 0.8, null) });
        }
        return f;
    }

    private static DistributorController Dist(CoachFixture f) => new(f.NewContext(), f.Config);
    private static CoachAdminController Admin(CoachFixture f) => new(f.NewContext(), f.Config);
    private static JsonElement J(object? data) => JsonSerializer.SerializeToElement(data);
    private static CoachCouponMakerService Maker(CoachFixture f) => new(f.NewContext(), f.Config);

    private static async Task SetCouponFloors(CoachFixture f, int coachId, List<CouponFloorInput> care)
    {
        var res = await Admin(f).SetCouponFloors("session-1", new CoachAdminController.SetCouponFloorsInput(coachId, care));
        Assert.Equal(0, res.code);
    }

    private static async Task<CoachCouponMakerService.MakeResult> Make(CoachFixture f, string shop, string tier, string mode, double value, int count = 3, string req = "r1", int? coachId = null)
    {
        int id = coachId ?? f.Coach2Id;
        var coach = await f.NewContext().coach.AsNoTracking().SingleAsync(c => c.id == id);
        var intro = await f.NewContext().introducer.AsNoTracking().SingleAsync(i => i.coach_id == id);
        return await Maker(f).MakeAsync(coach, intro, new MakeCouponInput(shop, tier, mode, value, count, req), DateTime.Now);
    }

    private static async Task<CoachException> Rejected(Task<CoachCouponMakerService.MakeResult> t) => await Assert.ThrowsAsync<CoachException>(() => t);

    // 开单计价：用现有 CareController.CalcCharge 按券算服务费（券的价格规则就挂在模板上）
    private static async Task<(double charge, double discount)> Price(CoachFixture f, string code, string shop, bool urgent, bool two)
    {
        var db = f.NewContext();
        Ticket ticket = await db.ticket.Include(t => t.template).ThenInclude(t => t.productTicketTemplates).AsNoTracking().SingleAsync(t => t.code == code);
        var care = new Care { need_edge = 1, need_wax = two ? 1 : 0, urgent = urgent ? 1 : 0, valid = 1 };
        return await new CareController(db, f.Config, ShareFixture.Http).CalcCharge(shop, care, ticket);
    }

    private static async Task<string> FirstCode(CoachFixture f, int templateId) =>
        (await f.NewContext().ticket.AsNoTracking().OrderBy(t => t.code).FirstAsync(t => t.template_id == templateId)).code;

    // ---------- 优惠券底价的纯规则 ----------

    [Theory]
    [InlineData("fixed", 60, null)]
    [InlineData("fixed", 59.99, "一口价不能低于 ¥60")]
    [InlineData("rate", 0.7, null)]
    [InlineData("rate", 0.69, "折扣不能低于 7 折")]
    [InlineData("off", 30, null)]
    [InlineData("off", 30.01, "减免不能超过 ¥30")]
    [InlineData("off", 1, null)]
    public void Coupon_values_are_checked_against_the_three_floors(string mode, double value, string? expected)
    {
        var floor = new CoachCouponFloor { min_fixed = 60, min_rate = 0.7, max_off = 30 };
        string? msg = SnowmeetApi.Helpers.CoachFloorRules.CheckCoupon(floor, mode, value, "万龙 单项");
        if (expected == null) Assert.Null(msg); else Assert.Contains(expected, msg);
    }

    [Fact]
    public void A_mode_the_admin_did_not_set_is_not_open_and_a_cell_with_no_mode_is_closed()
    {
        var rules = typeof(SnowmeetApi.Helpers.CoachFloorRules);
        var onlyOff = new CoachCouponFloor { max_off = 50 };
        Assert.Equal(new[] { "off" }, SnowmeetApi.Helpers.CoachFloorRules.CouponModes(onlyOff));
        Assert.Equal(new[] { "fixed", "rate", "off" }, SnowmeetApi.Helpers.CoachFloorRules.CouponModes(new CoachCouponFloor { min_fixed = 0, min_rate = 0.5, max_off = 1 }));
        Assert.Empty(SnowmeetApi.Helpers.CoachFloorRules.CouponModes(null));
        Assert.Empty(SnowmeetApi.Helpers.CoachFloorRules.CouponModes(new CoachCouponFloor()));
        Assert.Contains("没有开放折扣", SnowmeetApi.Helpers.CoachFloorRules.CheckCoupon(onlyOff, "rate", 0.9, "x"));
        Assert.Contains("没有开放一口价", SnowmeetApi.Helpers.CoachFloorRules.CheckCoupon(onlyOff, "fixed", 100, "x"));
        Assert.Contains("还没有开放", SnowmeetApi.Helpers.CoachFloorRules.CheckCoupon(null, "fixed", 100, "x"));
        Assert.Contains("请选择", SnowmeetApi.Helpers.CoachFloorRules.CheckCoupon(onlyOff, "none", 0, "x"));
        Assert.NotNull(rules);
        // 0 元一口价是有效的底价（原来「一口价 0」的老设置迁过来就是这样）
        Assert.Null(SnowmeetApi.Helpers.CoachFloorRules.CheckCoupon(new CoachCouponFloor { min_fixed = 0 }, "fixed", 0, "x"));
    }

    // ---------- 管理员设优惠券底价 ----------

    [Fact]
    public async Task Only_admins_set_coupon_floors_bad_values_are_rejected_and_a_second_save_updates_in_place()
    {
        await using var f = await NewFixture(withFloors: false);
        var ok = new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "单项", 60, 0.7, 30) });
        Assert.Equal(3, (await Admin(f).SetCouponFloors("session-7", ok)).code);          // 店员
        Assert.Equal(3, (await Admin(f).GetCouponFloors("session-7", f.Coach2Id)).code);
        Assert.Equal(2, (await Admin(f).GetCouponFloors("nope", f.Coach2Id)).code);
        foreach (var bad in new[]
        {
            new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "单项", null, 0, null) }),
            new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "单项", null, 1.5, null) }),
            new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "单项", -1, null, null) }),
            new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "单项", null, null, 0) }),
            new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new("南山", "单项", 60, null, null) }),
            new CoachAdminController.SetCouponFloorsInput(f.Coach2Id, new() { new(Wl, "三项", 60, null, null) }),
            new CoachAdminController.SetCouponFloorsInput(9999, new()),
        })
            Assert.Equal(1, (await Admin(f).SetCouponFloors("session-1", bad)).code);
        Assert.Empty(await f.NewContext().coachCouponFloor.ToListAsync());

        Assert.Equal(0, (await Admin(f).SetCouponFloors("session-1", ok)).code);
        var view = J((await Admin(f).GetCouponFloors("session-1", f.Coach2Id)).data);
        Assert.Equal(4, view.GetProperty("care").GetArrayLength());
        var wl1 = view.GetProperty("care").EnumerateArray().Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.Equal((60.0, 0.7, 30.0), (wl1.GetProperty("minFixed").GetDouble(), wl1.GetProperty("minRate").GetDouble(), wl1.GetProperty("maxOff").GetDouble()));
        var cl2 = view.GetProperty("care").EnumerateArray().Single(c => c.GetProperty("shop").GetString() == Cl && c.GetProperty("tier").GetString() == "双项");
        Assert.Equal(JsonValueKind.Null, cl2.GetProperty("minFixed").ValueKind);
        // 再保存是更新不是新增；去掉的方式回到「没开放」
        Assert.Equal(0, (await Admin(f).SetCouponFloors("session-1", new(f.Coach2Id, new() { new(Wl, "单项", null, null, 20) }))).code);
        Assert.Equal(4, await f.NewContext().coachCouponFloor.CountAsync());
        var again = await f.NewContext().coachCouponFloor.AsNoTracking().SingleAsync(x => x.shop == Wl && x.tier == "单项");
        Assert.Equal((null, null, (double?)20), (again.min_fixed, again.min_rate, again.max_off));
        var logs = await f.NewContext().coreDataModLog.Where(l => l.table_name == "coach_coupon_floor").ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => { Assert.Equal("管理员设置优惠券底价", l.scene); Assert.Equal(9, l.staff_id); });
    }

    // ---------- 生成优惠券 ----------

    [Fact]
    public async Task A_fixed_price_batch_creates_a_hidden_template_rules_tickets_and_grants_and_prices_the_order()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Wl, "单项", "fixed", 68, 3);
        Assert.False(made.repeated);
        Assert.Equal(3, made.created);

        var tpl = await f.NewContext().ticketTemplate.AsNoTracking().SingleAsync(t => t.id == made.templateId);
        Assert.Equal(("养护券", "养护", 1, 0, 1), (tpl.type, tpl.biz_type, tpl.hide, tpl.sharable, tpl.valid));
        Assert.Contains("某机构", tpl.name);
        Assert.Contains("68元一口价", tpl.name);
        Assert.DoesNotContain("¥", tpl.name + tpl.memo);          // GBK 库里没有这个符号
        Assert.True(CoachCouponMakerService.ByteLen(tpl.name) <= 50);
        Assert.NotNull(tpl.expire_date);
        // 单项 120 / 单项加急 180：基础商品一口价 68；加急商品 = 68 + 60 加急加价
        var rules = await f.NewContext().productTicketTemplate.AsNoTracking().Where(r => r.ticket_template_id == tpl.id).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Equal(new double?[] { 68, 128 }, rules.Select(r => r.fixed_price).OrderBy(x => x).ToArray());
        Assert.All(rules, r => { Assert.Null(r.discount_rate); Assert.Null(r.discount_amount); Assert.True(r.valid); });

        var tickets = await f.NewContext().ticket.AsNoTracking().Where(t => t.template_id == tpl.id).ToListAsync();
        Assert.Equal(3, tickets.Count);
        Assert.All(tickets, t => { Assert.Equal(CoachFixture.CoachMember2, t.member_id); Assert.Equal("机构自制券", t.channel); Assert.Equal(0, t.used); Assert.Equal(1, t.valid); Assert.Equal(tpl.expire_date, t.expire_date); });
        Assert.Equal(3, tickets.Select(t => t.code).Distinct().Count());
        var grants = await f.NewContext().coachCouponGrant.AsNoTracking().Where(g => g.template_id == tpl.id).ToListAsync();
        Assert.Equal(3, grants.Count);
        Assert.All(grants, g => Assert.Equal(f.Coach2Id, g.coach_id));

        Assert.Equal((68.0, 0.0), await Price(f, tickets[0].code, Wl, urgent: false, two: false));
        Assert.Equal((128.0, 0.0), await Price(f, tickets[0].code, Wl, urgent: true, two: false));
        Assert.Equal(1, await f.NewContext().coreDataModLog.CountAsync(l => l.scene == "机构自制优惠券" && l.key_value == tpl.id && l.member_id == CoachFixture.CoachMember2));
    }

    [Fact]
    public async Task A_discount_rate_batch_discounts_the_base_and_urgent_prices_by_the_same_rate()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Wl, "双项", "rate", 0.8, 2);
        string code = await FirstCode(f, made.templateId);
        Assert.Equal((168.0, 33.6), await Price(f, code, Wl, urgent: false, two: true));    // 双项 168 → 8 折
        Assert.Equal((228.0, 45.6), await Price(f, code, Wl, urgent: true, two: true));     // 双项加急 228 → 8 折
        Assert.Contains("8折", made.name);
    }

    [Fact]
    public async Task A_reduction_batch_takes_the_same_amount_off_base_and_urgent_prices()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Wl, "单项", "off", 30, 2);
        Assert.Contains("减30元", made.name);
        Assert.Contains("减30元", made.memo);
        Assert.DoesNotContain("¥", made.name + made.memo);
        var rules = await f.NewContext().productTicketTemplate.AsNoTracking().Where(r => r.ticket_template_id == made.templateId).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.All(rules, r => { Assert.Equal(30, r.discount_amount); Assert.Null(r.fixed_price); Assert.Null(r.discount_rate); });
        string code = await FirstCode(f, made.templateId);
        Assert.Equal((120.0, 30.0), await Price(f, code, Wl, urgent: false, two: false));   // 单项 120 立减 30
        Assert.Equal((180.0, 30.0), await Price(f, code, Wl, urgent: true, two: false));    // 单项加急 180 立减 30，加急加价照收
    }

    [Fact]
    public async Task A_shop_without_urgent_products_falls_back_to_the_base_rule_for_urgent_orders()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Cl, "单项", "fixed", 100, 1);      // 崇礼没有加急商品
        string code = await FirstCode(f, made.templateId);
        Assert.Equal(1, await f.NewContext().productTicketTemplate.CountAsync(r => r.ticket_template_id == made.templateId));
        Assert.Equal((100.0, 0.0), await Price(f, code, Cl, urgent: false, two: false));
        Assert.Equal((100.0, 0.0), await Price(f, code, Cl, urgent: true, two: false));
    }

    [Fact]
    public async Task Values_beyond_the_floor_modes_the_admin_did_not_open_and_bad_input_are_refused_and_nothing_is_created()
    {
        await using var f = await NewFixture();
        Assert.Contains("一口价不能低于 ¥60", (await Rejected(Make(f, Wl, "单项", "fixed", 59))).Message);
        Assert.Contains("减免不能超过 ¥30", (await Rejected(Make(f, Wl, "单项", "off", 31))).Message);
        Assert.Contains("折扣不能低于 7 折", (await Rejected(Make(f, Wl, "双项", "rate", 0.6))).Message);
        Assert.Contains("没有开放一口价", (await Rejected(Make(f, Wl, "双项", "fixed", 100))).Message);      // 万龙双项只开放折扣
        Assert.Contains("没有开放减免", (await Rejected(Make(f, Wl, "双项", "off", 10))).Message);
        Assert.Contains("没有开放折扣", (await Rejected(Make(f, Cl, "单项", "rate", 0.9))).Message);       // 崇礼单项只开放一口价
        Assert.Contains("还没有开放", (await Rejected(Make(f, Cl, "双项", "fixed", 200))).Message);          // 崇礼双项整格没开放
        Assert.Contains("请选择一口价、折扣或减免", (await Rejected(Make(f, Wl, "单项", "none", 0))).Message);
        Assert.Contains("减免金额必须大于 0", (await Rejected(Make(f, Wl, "单项", "off", 0))).Message);
        Assert.Contains("只能用于", (await Rejected(Make(f, "南山", "单项", "fixed", 100))).Message);
        Assert.Contains("单项或双项", (await Rejected(Make(f, Wl, "三项", "fixed", 100))).Message);
        Assert.Contains("1～50", (await Rejected(Make(f, Wl, "单项", "fixed", 68, 0))).Message);
        Assert.Contains("1～50", (await Rejected(Make(f, Wl, "单项", "fixed", 68, 51))).Message);
        Assert.Contains("请求编号", (await Rejected(Make(f, Wl, "单项", "fixed", 68, 1, " "))).Message);
        Assert.Empty(await f.NewContext().ticket.Where(t => t.channel == "机构自制券").ToListAsync());
        Assert.Empty(await f.NewContext().ticketTemplate.Where(t => t.hide == 1 && t.type == "养护券" && t.name.Contains("某机构")).ToListAsync());
        // 刚好等于底价可以
        Assert.Equal(1, (await Make(f, Wl, "单项", "fixed", 60, 1, "ok1")).created);
        Assert.Equal(1, (await Make(f, Wl, "双项", "rate", 0.7, 1, "ok2")).created);
        // （测试库里同一个商品只能挂一个模板的规则，生产没有这个限制，所以三种方式的边界各用不同的商品）
        Assert.Equal(1, (await Make(f, Cl, "单项", "fixed", 100, 1, "ok3")).created);
    }

    [Fact]
    public async Task Without_any_coupon_floor_nothing_can_be_made_and_the_card_floor_does_not_count()
    {
        await using var f = await NewFixture(withFloors: false);
        Assert.Contains("还没有开放", (await Rejected(Make(f, Wl, "单项", "fixed", 68))).Message);
        // 会籍卡底价是另一份（只管机构自己的会籍卡），不能拿来发优惠券
        Assert.Equal(0, (await Admin(f).SetCoachFloors("session-1", new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { new(Wl, "单项", 0.7, 60) }, 0.8))).code);
        Assert.Contains("还没有开放", (await Rejected(Make(f, Wl, "单项", "fixed", 68))).Message);
    }

    [Fact]
    public async Task A_cell_without_a_catalog_product_is_refused()
    {
        await using var g = await NewFixture();
        await g.NewContext().product.Where(p => p.shop_id == 2).ExecuteDeleteAsync();
        Assert.Contains("还没有配置养护价目", (await Rejected(Make(g, Cl, "单项", "fixed", 100))).Message);
    }

    [Fact]
    public async Task A_repeated_request_id_does_not_create_a_second_batch()
    {
        await using var f = await NewFixture();
        var first = await Make(f, Wl, "单项", "fixed", 68, 3, "same");
        var again = await Make(f, Wl, "单项", "fixed", 68, 3, "same");
        Assert.True(again.repeated);
        Assert.Equal((first.templateId, 0), (again.templateId, again.created));
        Assert.Equal(3, await f.NewContext().ticket.CountAsync(t => t.channel == "机构自制券"));
        Assert.Equal(1, await f.NewContext().ticketTemplate.CountAsync(t => t.name.Contains("某机构")));
        // 不同请求编号、同价格：新生成 2 张，但共用同一个模板，价格规则不再增加
        var other = await Make(f, Wl, "单项", "fixed", 68, 2, "different");
        Assert.Equal((first.templateId, 2, false), (other.templateId, other.created, other.repeated));
        Assert.Equal(5, await f.NewContext().ticket.CountAsync(t => t.channel == "机构自制券"));
        Assert.Equal(1, await f.NewContext().ticketTemplate.CountAsync(t => t.name.Contains("某机构")));
        Assert.Equal(2, await f.NewContext().productTicketTemplate.CountAsync(r => r.ticket_template_id == first.templateId));
        // 价格不同才是新模板
        var cheaper = await Make(f, Wl, "双项", "rate", 0.8, 1, "other-product");
        Assert.NotEqual(first.templateId, cheaper.templateId);
        Assert.Equal(2, await f.NewContext().ticketTemplate.CountAsync(t => t.name.Contains("某机构")));
    }

    [Fact]
    public async Task The_outstanding_cap_counts_only_unused_unexpired_self_made_coupons()
    {
        await using var f = await NewFixture();
        var db = f.NewContext();
        var now = DateTime.Now;
        for (int i = 0; i < 495; i++)
        {
            string code = "CAP" + i.ToString("D6");
            db.ticket.Add(new Ticket { code = code, template_id = 1, name = "x", memo = "", member_id = CoachFixture.CoachMember2, open_id = "", valid = 1, used = i < 3 ? 1 : 0, is_active = 1,
                channel = "机构自制券", start_date = now, expire_date = now.AddDays(30), create_date = now });
            db.coachCouponGrant.Add(new CoachCouponGrant { coach_id = f.Coach2Id, ticket_code = code, template_id = 1, grant_key = "seed-" + f.Coach2Id + "-" + i, create_date = now });
        }
        await db.SaveChangesAsync();
        // 495 张里 3 张已用：还剩 492 张未用，再生成 9 张就到 501
        Assert.Contains("上限 500", (await Rejected(Make(f, Wl, "单项", "fixed", 68, 9, "big"))).Message);
        Assert.Equal(8, (await Make(f, Wl, "单项", "fixed", 68, 8, "fits")).created);
    }

    [Fact]
    public async Task Made_coupons_can_be_forwarded_and_accepting_one_gives_the_customer_to_the_distributor()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Wl, "单项", "off", 30, 2);
        string code = await FirstCode(f, made.templateId);
        var tickets = new TicketMigrationTests.TestTicketController(f.NewContext(), f.Config);
        Assert.Equal(0, TicketMigrationTests.Result(await tickets.SetTicketToMiniShare(code, "session-6")).code);       // 模板不可转赠，但分销商发出的券可以
        Assert.Equal(0, TicketMigrationTests.Result(await new TicketMigrationTests.TestTicketController(f.NewContext(), f.Config).AcceptMiniTicket(code, "", "session-4")).code);
        var rel = await f.NewContext().distributorCustomer.AsNoTracking().SingleAsync(c => c.member_id == CoachFixture.StudentB);
        Assert.Equal(("接券", code), (rel.source, rel.source_ref));
        var intro = await f.NewContext().introducer.AsNoTracking().SingleAsync(i => i.coach_id == f.Coach2Id);
        Assert.Equal(intro.id, rel.introducer_id);
        var coupons = J((await new CoachController(f.NewContext(), f.Config).GetMyCoupons("session-6")).data);
        Assert.Equal(2, coupons.GetProperty("items").GetArrayLength());
        Assert.Equal(1, coupons.GetProperty("summary").GetProperty("accepted").GetInt32());
    }

    // ---------- 教练 / KOL 也能发（按各自的优惠券底价）----------

    [Theory]
    [InlineData("教练")]
    [InlineData("KOL")]
    public async Task Coaches_and_kols_make_coupons_within_their_own_floors(string type)
    {
        await using var f = await NewFixture(firstType: type);
        // 教练 1 只开放了万龙单项的减免（≤ 50）和崇礼单项的一口价（≥ 80）/ 折扣（≥ 8 折）
        Assert.Contains("减免不能超过 ¥50", (await Rejected(Make(f, Wl, "单项", "off", 51, coachId: f.CoachId))).Message);
        Assert.Contains("没有开放一口价", (await Rejected(Make(f, Wl, "单项", "fixed", 100, coachId: f.CoachId))).Message);
        Assert.Contains("还没有开放", (await Rejected(Make(f, Wl, "双项", "off", 10, coachId: f.CoachId))).Message);
        Assert.Contains("一口价不能低于 ¥80", (await Rejected(Make(f, Cl, "单项", "fixed", 70, coachId: f.CoachId))).Message);
        var made = await Make(f, Wl, "单项", "off", 50, 4, "coach-1", f.CoachId);
        Assert.Equal(4, made.created);
        Assert.Contains("某教练", made.name);
        string code = await FirstCode(f, made.templateId);
        Assert.Equal(CoachFixture.CoachMember, (await f.NewContext().ticket.AsNoTracking().SingleAsync(t => t.code == code)).member_id);   // 放进自己的券包
        Assert.Equal((120.0, 50.0), await Price(f, code, Wl, urgent: false, two: false));
        // 各发各的底价：机构的减免上限 30 不影响教练的 50
        Assert.Equal(f.CoachId, (await f.NewContext().coachCouponGrant.AsNoTracking().FirstAsync(g => g.template_id == made.templateId)).coach_id);
    }

    [Fact]
    public async Task The_maker_page_lists_only_what_the_admin_opened_and_every_distributor_type_can_open_it()
    {
        await using var f = await NewFixture();
        var view = J((await Dist(f).GetMyCouponMaker("session-6")).data);
        Assert.True(view.GetProperty("canMake").GetBoolean());
        Assert.Equal(50, view.GetProperty("maxPerRequest").GetInt32());
        var cells = view.GetProperty("cells").EnumerateArray().ToList();
        Assert.Equal(4, cells.Count);
        string[] ModesOf(string shop, string tier) => cells.Single(c => c.GetProperty("shop").GetString() == shop && c.GetProperty("tier").GetString() == tier)
            .GetProperty("modes").EnumerateArray().Select(m => m.GetString()!).ToArray();
        Assert.Equal(new[] { "fixed", "rate", "off" }, ModesOf(Wl, "单项"));
        Assert.Equal(new[] { "rate" }, ModesOf(Wl, "双项"));
        Assert.Equal(new[] { "fixed" }, ModesOf(Cl, "单项"));
        Assert.Empty(ModesOf(Cl, "双项"));
        var wl1 = cells.Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.True(wl1.GetProperty("productReady").GetBoolean());
        Assert.Equal((60.0, 0.7, 30.0), (wl1.GetProperty("minFixed").GetDouble(), wl1.GetProperty("minRate").GetDouble(), wl1.GetProperty("maxOff").GetDouble()));
        Assert.False(cells.Single(c => c.GetProperty("shop").GetString() == Cl && c.GetProperty("tier").GetString() == "双项").GetProperty("open").GetBoolean());

        var res = await Dist(f).MakeMyCoupons("session-6", new("万龙服务中心", "单项", "off", 20, 2, "page-1"));
        Assert.Equal(0, res.code);
        var body = J(res.data);
        Assert.Equal(2, body.GetProperty("made").GetProperty("created").GetInt32());
        Assert.Equal(2, body.GetProperty("maker").GetProperty("outstanding").GetInt32());
        var recent = body.GetProperty("maker").GetProperty("recent").EnumerateArray().ToList();
        Assert.Single(recent);
        Assert.Equal(1, (await Dist(f).MakeMyCoupons("session-6", new(Wl, "单项", "fixed", 10, 1, "page-2"))).code);      // 低于底价

        // 教练也有这个页面，只看到自己被开放的
        var coach = J((await Dist(f).GetMyCouponMaker("session-2")).data);
        Assert.True(coach.GetProperty("canMake").GetBoolean());
        var coachWl1 = coach.GetProperty("cells").EnumerateArray().Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.Equal(new[] { "off" }, coachWl1.GetProperty("modes").EnumerateArray().Select(m => m.GetString()).ToArray());
        Assert.Equal(1, (await Dist(f).MakeMyCoupons("session-2", new(Wl, "单项", "off", 80, 1, "coach-over"))).code);      // 教练的减免上限是 50
        Assert.Equal(0, (await Dist(f).MakeMyCoupons("session-2", new(Cl, "单项", "fixed", 80, 1, "coach-page"))).code);

        Assert.Equal(3, (await Dist(f).GetMyCouponMaker("session-3")).code);         // 学员不是分销商
        Assert.Equal(2, (await Dist(f).GetMyCouponMaker("nope")).code);
    }

    [Fact]
    public async Task Only_organizations_can_issue_season_long_cards_coaches_and_kols_cannot()
    {
        await using var f = await NewFixture();
        var coachMade = await new CoachController(f.NewContext(), f.Config).CreateCard("session-2");       // 会员 2 = 教练
        Assert.Equal(1, coachMade.code);
        Assert.Contains("只有机构", coachMade.message);
        await f.NewContext().introducer.Where(i => i.coach_id == f.CoachId).ExecuteUpdateAsync(x => x.SetProperty(i => i.type, "KOL"));
        var kolMade = await new CoachController(f.NewContext(), f.Config).CreateCard("session-2");
        Assert.Equal(1, kolMade.code);
        Assert.Contains("只有机构", kolMade.message);
        Assert.Empty(await f.NewContext().coachCard.Where(c => c.coach_id == f.CoachId).ToListAsync());
        // 机构仍然按卡种发卡（没选卡种提示选卡种，不是被这条规则拦下）
        var org = await new CoachController(f.NewContext(), f.Config).CreateCard("session-6");
        Assert.Equal(1, org.code);
        Assert.Contains("卡种", org.message);
    }

    [Fact]
    public async Task A_long_distributor_name_is_cut_so_the_template_name_fits_the_fifty_byte_column()
    {
        await using var f = await NewFixture();
        await f.NewContext().introducer.Where(i => i.coach_id == f.Coach2Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.name, "北京某某体育文化发展有限公司崇礼滑雪学校分校"));
        var made = await Make(f, Wl, "单项", "fixed", 1234.5, 1);
        Assert.True(CoachCouponMakerService.ByteLen(made.name) <= 48, made.name);
        Assert.EndsWith(" 万龙单项1234.5元一口价券", made.name);
        Assert.StartsWith("北京某某", made.name);
        Assert.Equal(made.name, (await f.NewContext().ticket.AsNoTracking().FirstAsync(t => t.template_id == made.templateId)).name);
        Assert.Equal("abc", CoachCouponMakerService.FitBytes("abcdef", 3));
        Assert.Equal(5, CoachCouponMakerService.ByteLen("汉字a"));
        Assert.Equal("汉字", CoachCouponMakerService.FitBytes("汉字很长", 5));
    }
}

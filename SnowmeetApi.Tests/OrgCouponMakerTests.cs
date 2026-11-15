using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

// 机构自己生成优惠券：按管理员设的底价（和会员卡共用同一份底价）生成一批券放进自己的券包，再转发给顾客。
// 一批券 = 一个隐藏的养护券模板 + 模板上针对单项 / 双项养护商品的价格规则（复用现有开单计价）+ N 张券 + 分销商发券登记。
public class OrgCouponMakerTests
{
    private const string Wl = "万龙服务中心", Cl = "崇礼旗舰店";

    // 教练 1（会员 2）是「教练」，教练 2（会员 6）是「机构」；机构的底价：万龙单项 7 折 / ¥60，万龙双项 7 折，崇礼单项只开放一口价 ¥100
    private static async Task<CoachFixture> NewFixture(bool withFloors = true)
    {
        var f = await CoachFixture.Create(withPricing: false);
        foreach (var (member, coach, type) in new[] { (CoachFixture.CoachMember, f.CoachId, "教练"), (CoachFixture.CoachMember2, f.Coach2Id, "机构") })
        {
            var relation = new OrderShareRelation { name = type + member, type = "个人", valid = true };
            f.Db.orderShareRelation.Add(relation);
            await f.Db.SaveChangesAsync();
            f.Db.introducer.Add(new Introducer { member_id = member, coach_id = coach, type = type, name = "某机构", relation_id = relation.id, valid = 1 });
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
        if (withFloors)
        {
            var res = await new CoachAdminController(f.NewContext(), f.Config).SetCoachFloors("session-1", new(f.Coach2Id, new()
            {
                new(Wl, "单项", 0.7, 60), new(Wl, "双项", 0.7, null), new(Cl, "单项", null, 100),
            }, 0.8));
            Assert.Equal(0, res.code);
        }
        return f;
    }

    private static DistributorController Org(CoachFixture f) => new(f.NewContext(), f.Config);
    private static JsonElement J(object? data) => JsonSerializer.SerializeToElement(data);
    private static CoachCouponMakerService Maker(CoachFixture f) => new(f.NewContext(), f.Config);
    private static async Task<(Coach, Introducer)> OrgOf(CoachFixture f) =>
        (await f.NewContext().coach.AsNoTracking().SingleAsync(c => c.id == f.Coach2Id), await f.NewContext().introducer.AsNoTracking().SingleAsync(i => i.coach_id == f.Coach2Id));
    private static Task<CoachCouponMakerService.MakeResult> Make(CoachFixture f, string shop, string tier, string mode, double value, int count = 3, string req = "r1")
    {
        var (coach, intro) = OrgOf(f).Result;
        return Maker(f).MakeAsync(coach, intro, new MakeCouponInput(shop, tier, mode, value, count, req), DateTime.Now);
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
        Assert.Contains("万龙", tpl.name);
        Assert.NotNull(tpl.expire_date);
        // 单项 120 / 单项加急 180：基础商品一口价 68；加急商品 = 68 + 60 加急加价
        var rules = await f.NewContext().productTicketTemplate.AsNoTracking().Where(r => r.ticket_template_id == tpl.id).ToListAsync();
        Assert.Equal(2, rules.Count);
        Assert.Equal(new double?[] { 68, 128 }, rules.Select(r => r.fixed_price).OrderBy(x => x).ToArray());
        Assert.All(rules, r => { Assert.Null(r.discount_rate); Assert.True(r.valid); });

        // 券在机构自己的券包里，登记为分销商发出的券
        var tickets = await f.NewContext().ticket.AsNoTracking().Where(t => t.template_id == tpl.id).ToListAsync();
        Assert.Equal(3, tickets.Count);
        Assert.All(tickets, t => { Assert.Equal(CoachFixture.CoachMember2, t.member_id); Assert.Equal("机构自制券", t.channel); Assert.Equal(0, t.used); Assert.Equal(1, t.valid); Assert.Equal(tpl.expire_date, t.expire_date); });
        Assert.Equal(3, tickets.Select(t => t.code).Distinct().Count());
        var grants = await f.NewContext().coachCouponGrant.AsNoTracking().Where(g => g.template_id == tpl.id).ToListAsync();
        Assert.Equal(3, grants.Count);
        Assert.All(grants, g => Assert.Equal(f.Coach2Id, g.coach_id));
        Assert.Equal(3, await f.NewContext().card.CountAsync(c => tickets.Select(t => t.code).Contains(c.card_no)));

        // 开单计价：单项按 68；加急单项按 68 + 60；券带来的不是「减免」而是定价
        Assert.Equal((68.0, 0.0), await Price(f, tickets[0].code, Wl, urgent: false, two: false));
        Assert.Equal((128.0, 0.0), await Price(f, tickets[0].code, Wl, urgent: true, two: false));
        // 审计
        Assert.Equal(1, await f.NewContext().coreDataModLog.CountAsync(l => l.scene == "机构自制优惠券" && l.key_value == tpl.id && l.member_id == CoachFixture.CoachMember2));
    }

    [Fact]
    public async Task A_discount_rate_batch_discounts_the_base_and_urgent_prices_by_the_same_rate()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Wl, "双项", "rate", 0.8, 2);
        string code = (await f.NewContext().ticket.AsNoTracking().FirstAsync(t => t.template_id == made.templateId)).code;
        Assert.Equal((168.0, 33.6), await Price(f, code, Wl, urgent: false, two: true));    // 双项 168 → 8 折
        Assert.Equal((228.0, 45.6), await Price(f, code, Wl, urgent: true, two: true));     // 双项加急 228 → 8 折
        Assert.Contains("8折", made.name);
    }

    [Fact]
    public async Task A_shop_without_urgent_products_falls_back_to_the_base_rule_for_urgent_orders()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Cl, "单项", "fixed", 100, 1);      // 崇礼没有加急商品
        string code = (await f.NewContext().ticket.AsNoTracking().FirstAsync(t => t.template_id == made.templateId)).code;
        Assert.Equal(1, await f.NewContext().productTicketTemplate.CountAsync(r => r.ticket_template_id == made.templateId));
        Assert.Equal((100.0, 0.0), await Price(f, code, Cl, urgent: false, two: false));
        Assert.Equal((100.0, 0.0), await Price(f, code, Cl, urgent: true, two: false));
    }

    [Fact]
    public async Task Prices_below_the_floor_or_on_unopened_cells_or_the_wrong_mode_are_refused_and_nothing_is_created()
    {
        await using var f = await NewFixture();
        Assert.Contains("一口价不能低于 ¥60", (await Rejected(Make(f, Wl, "单项", "fixed", 59))).Message);
        Assert.Contains("折扣不能低于 7 折", (await Rejected(Make(f, Wl, "双项", "rate", 0.6))).Message);
        Assert.Contains("只开放了折扣", (await Rejected(Make(f, Wl, "双项", "fixed", 100))).Message);
        Assert.Contains("只开放了一口价", (await Rejected(Make(f, Cl, "单项", "rate", 0.9))).Message);
        Assert.Contains("还没有开放", (await Rejected(Make(f, Cl, "双项", "fixed", 200))).Message);
        Assert.Contains("请选择折扣或一口价", (await Rejected(Make(f, Wl, "单项", "none", 0))).Message);
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
    }

    [Fact]
    public async Task Without_any_floor_an_organization_cannot_make_coupons_and_a_cell_without_a_catalog_product_is_refused()
    {
        await using var f = await NewFixture(withFloors: false);
        Assert.Contains("还没有开放", (await Rejected(Make(f, Wl, "单项", "fixed", 68))).Message);
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
    public async Task Made_coupons_can_be_forwarded_and_accepting_one_gives_the_customer_to_the_organization()
    {
        await using var f = await NewFixture();
        var made = await Make(f, Wl, "单项", "fixed", 68, 2);
        string code = (await f.NewContext().ticket.AsNoTracking().OrderBy(t => t.code).FirstAsync(t => t.template_id == made.templateId)).code;
        var tickets = new TicketMigrationTests.TestTicketController(f.NewContext(), f.Config);
        Assert.Equal(0, TicketMigrationTests.Result(await tickets.SetTicketToMiniShare(code, "session-6")).code);       // 模板不可转赠，但分销商发出的券可以
        Assert.Equal(0, TicketMigrationTests.Result(await new TicketMigrationTests.TestTicketController(f.NewContext(), f.Config).AcceptMiniTicket(code, "", "session-4")).code);
        var rel = await f.NewContext().distributorCustomer.AsNoTracking().SingleAsync(c => c.member_id == CoachFixture.StudentB);
        Assert.Equal(("接券", code), (rel.source, rel.source_ref));
        var intro = await f.NewContext().introducer.AsNoTracking().SingleAsync(i => i.coach_id == f.Coach2Id);
        Assert.Equal(intro.id, rel.introducer_id);
        // 机构在「我的券」里能看到这批券，其中一张已被接受
        var coupons = J((await new CoachController(f.NewContext(), f.Config).GetMyCoupons("session-6")).data);
        Assert.Equal(2, coupons.GetProperty("items").GetArrayLength());
        Assert.Equal(1, coupons.GetProperty("summary").GetProperty("accepted").GetInt32());
    }

    [Fact]
    public async Task The_maker_page_data_comes_from_the_floors_and_lists_recent_batches_and_a_coach_cannot_make_coupons()
    {
        await using var f = await NewFixture();
        var view = J((await Org(f).GetMyCouponMaker("session-6")).data);
        Assert.True(view.GetProperty("canMake").GetBoolean());
        Assert.Equal(50, view.GetProperty("maxPerRequest").GetInt32());
        var cells = view.GetProperty("cells").EnumerateArray().ToList();
        Assert.Equal(4, cells.Count);
        var wl1 = cells.Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.True(wl1.GetProperty("open").GetBoolean());
        Assert.True(wl1.GetProperty("productReady").GetBoolean());
        Assert.Equal(60, wl1.GetProperty("minFixed").GetDouble());
        Assert.False(cells.Single(c => c.GetProperty("shop").GetString() == Cl && c.GetProperty("tier").GetString() == "双项").GetProperty("open").GetBoolean());

        var res = await Org(f).MakeMyCoupons("session-6", new(Wl, "单项", "fixed", 68, 2, "page-1"));
        Assert.Equal(0, res.code);
        var body = J(res.data);
        Assert.Equal(2, body.GetProperty("made").GetProperty("created").GetInt32());
        Assert.Equal(2, body.GetProperty("maker").GetProperty("outstanding").GetInt32());
        var recent = body.GetProperty("maker").GetProperty("recent").EnumerateArray().ToList();
        Assert.Single(recent);
        Assert.Equal(2, recent[0].GetProperty("count").GetInt32());
        Assert.Equal(1, (await Org(f).MakeMyCoupons("session-6", new(Wl, "单项", "fixed", 10, 1, "page-2"))).code);      // 低于底价

        var coachView = J((await Org(f).GetMyCouponMaker("session-2")).data);
        Assert.False(coachView.GetProperty("canMake").GetBoolean());
        Assert.Equal(3, (await Org(f).MakeMyCoupons("session-2", new(Wl, "单项", "fixed", 68, 1, "x"))).code);
        Assert.Equal(3, (await Org(f).GetMyCouponMaker("session-3")).code);
        Assert.Equal(2, (await Org(f).GetMyCouponMaker("nope")).code);
    }

    [Fact]
    public async Task A_long_institution_name_is_cut_so_the_template_name_fits_the_fifty_byte_column()
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

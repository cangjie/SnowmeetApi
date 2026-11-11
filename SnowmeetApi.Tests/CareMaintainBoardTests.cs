using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护「养板」（用户 2026-11-10 提出）：
/// 和热蜡同一类业务、互斥、可互相替换。步骤 养板打蜡1 → 养板刮蜡1 → 养板打蜡2 → 养板刮蜡2 → 养板打蜡3。
/// 价格（万龙服务中心）：单选养板 220、凭老顾客券（模板 16）200；修刃+养板 280、凭券 250。
/// 和热蜡一样：选了养板就默认选中刮蜡，且刮蜡和封板必选其一。
///
/// 夹具里万龙服务中心的单项价 120 / 双项 168，和 220 / 280 对不上，一眼能看出走的是哪条规则。
/// </summary>
public class CareMaintainBoardTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private const string Wanlong = "万龙服务中心";
    private static CareController Ctl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    // 联动规则是纯函数，不碰数据库
    private static CareController Pure() => new(null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new Http());

    private static Care M(int maint = 1, int wax = 0, int unwax = 1, int seal = 0, int edge = 0, int freeWax = 0, string? summer = null, int urgent = 0) =>
        new() { equipment = "双板", need_edge = edge, need_wax = wax, need_unwax = unwax, seal_board = seal, free_wax = freeWax,
            maintain_board = maint, summer = summer, urgent = urgent, biz_type = summer == null ? "雪季养护" : "非雪季养护" };

    private static (int wax, int unwax, int seal, int maint) S(Care c) => (c.need_wax, c.need_unwax, c.seal_board, c.maintain_board);

    // ================= 服务项联动（ApplyServiceLinkage） =================

    [Fact]
    public void TapMaintain_DefaultsToScrape()
    {
        var c = M(maint: 1, unwax: 0);
        Pure().ApplyServiceLinkage(c, "maintain_board");
        Assert.Equal((0, 1, 0, 1), S(c));
    }

    [Fact]
    public void TapMaintain_WhileWaxIsOn_ReplacesWax()
    {
        // 养板与热蜡互斥：界面先把 maintain_board 置 1，再发联动请求，热蜡被换掉
        var c = M(maint: 1, wax: 1, unwax: 1);
        Pure().ApplyServiceLinkage(c, "maintain_board");
        Assert.Equal((0, 1, 0, 1), S(c));
    }

    [Fact]
    public void TapMaintain_WhileWaxWasSealed_GoesBackToScrapeDefault()
    {
        var c = M(maint: 1, wax: 1, unwax: 0, seal: 1);
        Pure().ApplyServiceLinkage(c, "maintain_board");
        Assert.Equal((0, 1, 0, 1), S(c));
    }

    [Fact]
    public void TapMaintain_WhileMachineWaxIsOn_ClearsMachineWax()
    {
        var c = M(maint: 1, freeWax: 1, unwax: 0);
        Pure().ApplyServiceLinkage(c, "maintain_board");
        Assert.Equal((0, 1, 0, 1), S(c));
        Assert.Equal(0, c.free_wax);
    }

    [Fact]
    public void TapWax_WhileMaintainIsOn_ReplacesMaintain()
    {
        var c = M(maint: 1, wax: 1, unwax: 1);
        Pure().ApplyServiceLinkage(c, "need_wax");
        Assert.Equal((1, 1, 0, 0), S(c));
    }

    [Fact]
    public void TapMachineWax_ClearsMaintain()
    {
        var c = M(maint: 1, unwax: 1, freeWax: 1);
        Pure().ApplyServiceLinkage(c, "free_wax");
        Assert.Equal((0, 0, 0, 0), S(c));
        Assert.Equal(1, c.free_wax);
    }

    [Fact]
    public void TurnOffMaintain_ClearsScrapeAndSeal()
    {
        var scraped = M(maint: 0, unwax: 1);
        Pure().ApplyServiceLinkage(scraped, "maintain_board");
        Assert.Equal((0, 0, 0, 0), S(scraped));
        var sealed_ = M(maint: 0, unwax: 0, seal: 1);
        Pure().ApplyServiceLinkage(sealed_, "maintain_board");
        Assert.Equal((0, 0, 0, 0), S(sealed_));
    }

    [Fact]
    public void WaxEventWithoutWax_DoesNotWipeTheMaintainScrape()
    {
        // 养板开着时收到 need_wax=0 的联动（热蜡本来就是关的）：不能把养板的刮蜡带掉
        var c = M(maint: 1, wax: 0, unwax: 1);
        Pure().ApplyServiceLinkage(c, "need_wax");
        Assert.Equal((0, 1, 0, 1), S(c));
    }

    [Fact]
    public void TapSeal_WithMaintain_SwitchesScrapeToSeal()
    {
        var c = M(maint: 1, unwax: 1, seal: 1);
        Pure().ApplyServiceLinkage(c, "seal_board");
        Assert.Equal((0, 0, 1, 1), S(c));
    }

    [Fact]
    public void TapScrape_WhileMaintainSealed_SwitchesBackToScrape()
    {
        var c = M(maint: 1, unwax: 1, seal: 1);
        Pure().ApplyServiceLinkage(c, "need_unwax");
        Assert.Equal((0, 1, 0, 1), S(c));
    }

    [Fact]
    public void ScrapeTurnedOffWithMaintain_BecomesSeal_SoOneOfTheTwoIsAlwaysChosen()
    {
        var c = M(maint: 1, unwax: 0, seal: 0);
        Pure().ApplyServiceLinkage(c, "need_unwax");
        Assert.Equal((0, 0, 1, 1), S(c));
    }

    [Theory]
    [InlineData("now")]
    [InlineData("later")]
    public void Maintain_InSummerCare_IsRejected(string mode)
    {
        var c = M(maint: 1, unwax: 0, summer: mode);
        Pure().ApplyServiceLinkage(c, "maintain_board");
        Assert.Equal(0, c.maintain_board);
        Assert.Equal(0, c.seal_board);
    }

    [Theory]
    [InlineData("now")]
    [InlineData("later")]
    public void SwitchingToSummerCare_ClearsMaintain(string mode)
    {
        var c = M(maint: 1, unwax: 1, summer: mode);
        Pure().ApplyServiceLinkage(c, "summer");
        Assert.Equal(0, c.maintain_board);
        if (mode == "later") Assert.Equal((1, 1, 0, 0), S(c));      // 先双项：修刃+热蜡+刮蜡
        else Assert.Equal((0, 0, 0, 0), S(c));
    }

    [Fact]
    public void DeriveDefaultServices_ClearsMaintain()
    {
        var c = M(maint: 1, unwax: 1);
        Pure().ApplyDefaultServices(c, null!, null!);
        Assert.Equal((0, 0, 0, 0), S(c));
    }

    // ================= 收敛到合法组合（Normalize）与封板资格 =================

    [Theory]
    //   输入 wax, unwax, seal, maint, freeWax, summer → 期望 wax, unwax, seal, maint
    [InlineData(0, 1, 0, 1, 0, null, 0, 1, 0, 1)]      // 养板+刮蜡：原样
    [InlineData(0, 0, 1, 1, 0, null, 0, 0, 1, 1)]      // 养板+封板：原样
    [InlineData(0, 1, 1, 1, 0, null, 0, 0, 1, 1)]      // 两个都勾：以封板为准
    [InlineData(0, 0, 0, 1, 0, null, 0, 1, 0, 1)]      // 养板但都没选：默认刮蜡
    [InlineData(1, 1, 0, 1, 0, null, 0, 1, 0, 1)]      // 热蜡和养板都勾（界面不该发生）：以养板为准，热蜡摘掉
    [InlineData(0, 0, 1, 0, 0, null, 0, 0, 0, 0)]      // 没热蜡也没养板：不能封板
    [InlineData(0, 0, 0, 1, 1, null, 0, 0, 0, 0)]      // 机打蜡与养板互斥：养板摘掉，机打蜡留着
    [InlineData(1, 1, 0, 1, 0, "later", 1, 1, 0, 0)]   // 非雪季没有养板：养板摘掉，其他项不动
    [InlineData(0, 0, 1, 1, 0, "now", 0, 0, 0, 0)]
    public void Normalize_ReducesToValidCombination(int wax, int unwax, int seal, int maint, int freeWax, string? summer,
        int eWax, int eUnwax, int eSeal, int eMaint)
    {
        var c = M(maint, wax, unwax, seal, freeWax: freeWax, summer: summer);
        CareSealRules.Normalize(c);
        Assert.Equal((eWax, eUnwax, eSeal, eMaint), S(c));
    }

    [Fact]
    public void CanSeal_IsTrueForMaintain_ButNotInSummerOrWithMachineWax()
    {
        Assert.True(CareSealRules.CanSeal(M(maint: 1)));
        Assert.False(CareSealRules.CanSeal(M(maint: 1, summer: "later")));
        Assert.False(CareSealRules.CanSeal(M(maint: 1, freeWax: 1)));
        Assert.False(CareSealRules.CanSeal(M(maint: 0, wax: 0)));
    }

    // ================= 计费口径（纯规则） =================

    [Theory]
    [InlineData(1, 0, 1)]     // 只养板：一项
    [InlineData(1, 1, 2)]     // 修刃+养板：两项
    [InlineData(0, 1, 1)]     // 只修刃：一项（没有养板，与以前一致）
    [InlineData(0, 0, 0)]
    public void CountChargeableItems_MaintainCountsAsTheWaxItem(int maint, int edge, int expected)
    {
        Assert.Equal(expected, CarePricingRules.CountChargeableItems(M(maint: maint, edge: edge)));
    }

    [Theory]
    [InlineData(0, 0, "养板")]
    [InlineData(1, 0, "修刃养板")]
    [InlineData(0, 1, "养板加急")]
    [InlineData(1, 1, "修刃养板加急")]
    public void ResolveProductName_Maintain(int edge, int urgent, string expected)
    {
        Assert.Equal(expected, CarePricingRules.ResolveProductName(M(maint: 1, edge: edge, urgent: urgent)));
    }

    [Fact]
    public void ResolveProductName_NoMaintain_StaysTheOldSingleDouble()
    {
        Assert.Equal("双项", CarePricingRules.ResolveProductName(M(maint: 0, wax: 1, edge: 1)));
        Assert.Equal("单项", CarePricingRules.ResolveProductName(M(maint: 0, wax: 1)));
        Assert.Equal("单项", CarePricingRules.ResolveProductName(M(maint: 0, wax: 0, edge: 1)));
    }

    [Fact]
    public void MatchProduct_UrgentMaintain_FallsBackToTheBasePrice()
    {
        var products = new List<Product> { new() { name = "养板", sale_price = 220 }, new() { name = "修刃养板", sale_price = 280 } };
        Assert.Equal(220, CarePricingRules.MatchProduct(products, M(maint: 1, urgent: 1))!.sale_price);
        Assert.Equal(280, CarePricingRules.MatchProduct(products, M(maint: 1, edge: 1, urgent: 1))!.sale_price);
    }

    [Fact]
    public void IsScrapeOnly_MaintainPlusScrape_IsNotTheScrapeOnlyDeal()
    {
        // 养板自带刮蜡：不能被当成「单刮蜡一口价」(80/100)
        Assert.False(CarePricingRules.IsScrapeOnly(M(maint: 1, unwax: 1)));
        Assert.True(CarePricingRules.IsScrapeOnly(M(maint: 0, unwax: 1)));
    }

    [Theory]
    [InlineData(0, 20)]       // 单选养板：老顾客券减 20 → 200
    [InlineData(1, 30)]       // 修刃+养板：老顾客券减 30 → 250
    public void LegacyTicketDiscount_MatchesTheStatedPrices(int edge, double discount)
    {
        Assert.Equal(discount, CarePricingRules.LegacyTicketDiscount(16, M(maint: 1, edge: edge), 999));
    }

    // ================= 券卡取舍（纯规则）：养板只认老顾客券 =================

    private static Ticket T(int template, string code = "TK") => new() { code = code, template_id = template, valid = 1, is_active = 1 };

    [Fact]
    public void ClearBenefits_KeepsTheOldCustomerTicket_DropsEverythingElse()
    {
        var c = M(); c.ticket_code = "TK"; c.discount = 20;
        Assert.False(CarePricingRules.ClearBenefitsForMaintainBoard(c, T(16)));
        Assert.Equal("TK", c.ticket_code);
        Assert.Equal(20, c.discount);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(17)]
    [InlineData(99)]
    public void ClearBenefits_DropsOtherTickets_AndTheirDiscount(int template)
    {
        var c = M(); c.ticket_code = "TK"; c.discount = 50; c.ticket_discount = 5;
        Assert.True(CarePricingRules.ClearBenefitsForMaintainBoard(c, T(template)));
        Assert.Null(c.ticket_code);
        Assert.Equal((0d, 0d), (c.discount, c.ticket_discount));
    }

    [Fact]
    public void ClearBenefits_DropsTicketThatCannotBeFound()
    {
        var c = M(); c.ticket_code = "GHOST";
        Assert.True(CarePricingRules.ClearBenefitsForMaintainBoard(c, null));
        Assert.Null(c.ticket_code);
    }

    [Fact]
    public void ClearBenefits_DropsMemberCardAndCoachCard()
    {
        var c = M(); c.use_card = true; c.card_id = 5; c.card_name = "次卡"; c.coach_card_id = 7; c.discount = 30;
        Assert.True(CarePricingRules.ClearBenefitsForMaintainBoard(c, null));
        Assert.False(c.use_card); Assert.Null(c.card_id); Assert.Null(c.card_name); Assert.Null(c.coach_card_id);
        Assert.Equal(0, c.discount);
    }

    [Fact]
    public void ClearBenefits_NothingSelected_LeavesTheHandTypedDiscountAlone()
    {
        var c = M(); c.discount = 15;
        Assert.False(CarePricingRules.ClearBenefitsForMaintainBoard(c, null));
        Assert.Equal(15, c.discount);
    }

    [Fact]
    public void ClearBenefits_DoesNothingWhenMaintainIsNotSelected()
    {
        var c = M(maint: 0, wax: 1); c.ticket_code = "TK"; c.use_card = true; c.card_id = 5;
        Assert.False(CarePricingRules.ClearBenefitsForMaintainBoard(c, T(12)));
        Assert.Equal("TK", c.ticket_code);
        Assert.True(c.use_card);
    }

    // ================= 夹具：养板价目、券 =================

    private const int MaintainProduct = 300, EdgeMaintainProduct = 301;

    /// <summary>万龙服务中心加两个商品：养板 220、修刃养板 280；withRules 时再配上老顾客券（16）立减 20 / 30 的商品规则。</summary>
    private static async Task AddMaintainPrices(CoachFixture f, bool withRules = true, string shopOnly = Wanlong)
    {
        int shopId = shopOnly == Wanlong ? 1 : 2;
        f.Db.product.Add(new Product { id = MaintainProduct, shop_id = shopId, name = "养板", category_code = "0203", category_id = 20, sale_price = 220, valid = 1, on_shelves = 1 });
        f.Db.product.Add(new Product { id = EdgeMaintainProduct, shop_id = shopId, name = "修刃养板", category_code = "0203", category_id = 20, sale_price = 280, valid = 1, on_shelves = 1 });
        if (withRules)
        {
            f.Db.productTicketTemplate.Add(new ProductTicketTemplate { product_id = MaintainProduct, ticket_template_id = 16, discount_amount = 20, valid = true });
            f.Db.productTicketTemplate.Add(new ProductTicketTemplate { product_id = EdgeMaintainProduct, ticket_template_id = 16, discount_amount = 30, valid = true });
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<string> AddTicket(CoachFixture f, int template = 16, int memberId = CoachFixture.StudentA)
    {
        string code = "MB" + Guid.NewGuid().ToString("N")[..8];
        f.Db.ticket.Add(new Ticket { code = code, name = "测试券", open_id = "", member_id = memberId, template_id = template, valid = 1, is_active = 1, used = 0,
            channel = "", create_date = DateTime.Now, start_date = DateTime.Now.Date.AddDays(-1), expire_date = DateTime.Now.Date.AddDays(30) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return code;
    }

    private static async Task<ApiResult<object>> PreviewRaw(CoachFixture f, Care care, string? changed = null, string shop = Wanlong, int? memberId = CoachFixture.StudentA)
    {
        var res = await Ctl(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = shop, memberId = memberId, care = care, changedField = changed }, "session-7");
        return Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(res.Result).Value);
    }

    private static async Task<JsonElement> Preview(CoachFixture f, Care care, string? changed = null, string shop = Wanlong, int? memberId = CoachFixture.StudentA)
    {
        var env = await PreviewRaw(f, care, changed, shop, memberId);
        Assert.Equal(0, env.code);
        return JsonSerializer.SerializeToElement(env.data);
    }

    // ================= 开单页实时计价 =================

    [Theory]
    [InlineData(false)]      // 没配商品规则：回退到老写死的 20 / 30
    [InlineData(true)]       // 配了商品规则
    public async Task Preview_Prices_AreTheStatedOnes(bool withRules)
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f, withRules);
        // 正常价
        Assert.Equal(220, (await Preview(f, M())).GetProperty("commonCharge").GetDouble());
        Assert.Equal(280, (await Preview(f, M(edge: 1))).GetProperty("commonCharge").GetDouble());
        // 凭老顾客券：减 20 / 减 30 → 200 / 250
        string code = await AddTicket(f);
        var one = M(); one.ticket_code = code;
        var oneJson = await Preview(f, one);
        Assert.Equal(220, oneJson.GetProperty("commonCharge").GetDouble());
        Assert.Equal(20, oneJson.GetProperty("ticketDiscount").GetDouble());
        Assert.Equal(20, oneJson.GetProperty("care").GetProperty("discount").GetDouble());
        Assert.False(oneJson.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal(code, oneJson.GetProperty("care").GetProperty("ticket_code").GetString());
        var two = M(edge: 1); two.ticket_code = code;
        var twoJson = await Preview(f, two);
        Assert.Equal(280, twoJson.GetProperty("commonCharge").GetDouble());
        Assert.Equal(30, twoJson.GetProperty("ticketDiscount").GetDouble());
        Assert.Equal(30, twoJson.GetProperty("care").GetProperty("discount").GetDouble());
    }

    [Fact]
    public async Task Preview_Urgent_UsesTheBasePrice()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        Assert.Equal(220, (await Preview(f, M(urgent: 1))).GetProperty("commonCharge").GetDouble());
        Assert.Equal(280, (await Preview(f, M(edge: 1, urgent: 1))).GetProperty("commonCharge").GetDouble());
    }

    [Fact]
    public async Task Preview_WanlongExperienceCenter_UsesTheServiceCenterPrice()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        Assert.Equal(220, (await Preview(f, M(), shop: "万龙体验中心")).GetProperty("commonCharge").GetDouble());
    }

    [Fact]
    public async Task Preview_MaintainPlusScrape_IsNotPricedAsTheScrapeOnlyDeal()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        // 养板的刮蜡是默认带出来的，不是「只刮蜡」：不能落到 80 / 100 的一口价
        var json = await Preview(f, M(maint: 1, unwax: 1));
        Assert.Equal(220, json.GetProperty("commonCharge").GetDouble());
    }

    [Fact]
    public async Task Preview_TapMaintain_ReturnsServerNormalizedCare()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        // 界面：热蜡+刮蜡开着时点了养板
        var json = await Preview(f, M(maint: 1, wax: 1, unwax: 1), changed: "maintain_board");
        var care = json.GetProperty("care");
        Assert.Equal((0, 1, 0, 1), (care.GetProperty("need_wax").GetInt32(), care.GetProperty("need_unwax").GetInt32(),
            care.GetProperty("seal_board").GetInt32(), care.GetProperty("maintain_board").GetInt32()));
        Assert.Equal(220, json.GetProperty("commonCharge").GetDouble());
    }

    [Fact]
    public async Task Preview_SealingTheMaintain_DoesNotChangeThePrice()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var json = await Preview(f, M(maint: 1, unwax: 1, seal: 1), changed: "seal_board");
        var care = json.GetProperty("care");
        Assert.Equal((0, 1, 1), (care.GetProperty("need_unwax").GetInt32(), care.GetProperty("seal_board").GetInt32(), care.GetProperty("maintain_board").GetInt32()));
        Assert.Equal(220, json.GetProperty("commonCharge").GetDouble());
    }

    [Fact]
    public async Task Preview_OtherTicketsAndCoachCard_AreDropped_ButTheOldCustomerTicketIsKept()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        string freeWaxTicket = await AddTicket(f, template: 12);
        var withOther = M(); withOther.ticket_code = freeWaxTicket; withOther.discount = 40;
        var otherJson = await Preview(f, withOther);
        Assert.True(otherJson.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal(JsonValueKind.Null, otherJson.GetProperty("care").GetProperty("ticket_code").ValueKind);
        Assert.Equal(220, otherJson.GetProperty("commonCharge").GetDouble());
        Assert.Equal(0, otherJson.GetProperty("care").GetProperty("discount").GetDouble());

        CoachCard coachCard = await f.IssueAndClaim(CoachFixture.StudentA);
        var withCoach = M(); withCoach.coach_card_id = coachCard.id;
        var coachJson = await Preview(f, withCoach);
        Assert.True(coachJson.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal(JsonValueKind.Null, coachJson.GetProperty("care").GetProperty("coach_card_id").ValueKind);
        Assert.Equal(220, coachJson.GetProperty("commonCharge").GetDouble());
        Assert.Equal(JsonValueKind.Null, coachJson.GetProperty("coach").ValueKind);
    }

    [Fact]
    public async Task Preview_StoreWithoutMaintainPrice_IsAnErrorNotFree()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);       // 只有万龙服务中心配了养板价目
        foreach (string shop in new[] { "崇礼旗舰店", "南山" })
        {
            var env = await PreviewRaw(f, M(), shop: shop);
            Assert.Equal(1, env.code);
            Assert.Contains("养板", env.message);
        }
        // 修刃养板同理
        var edgeEnv = await PreviewRaw(f, M(edge: 1), shop: "崇礼旗舰店");
        Assert.Equal(1, edgeEnv.code);
        Assert.Contains("养板", edgeEnv.message);
    }

    [Fact]
    public async Task Preview_WithoutMaintain_IsUnchanged()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var dbl = await Preview(f, M(maint: 0, wax: 1, unwax: 1, edge: 1));
        Assert.Equal(168, dbl.GetProperty("commonCharge").GetDouble());
        var single = await Preview(f, M(maint: 0, wax: 1, unwax: 1));
        Assert.Equal(120, single.GetProperty("commonCharge").GetDouble());
    }

    // ================= 下单落库 =================

    private static async Task<(int orderId, int careId)> Draft(CoachFixture f, Care care, string shop = Wanlong, int? memberId = CoachFixture.StudentA)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = shop, member_id = memberId, valid = 0, recepting = 1, staff_id = 9,
            code = "YH_20261110_" + Guid.NewGuid().ToString("N")[..6] };
        db.order.Add(order); await db.SaveChangesAsync();
        care.order_id = order.id; care.brand = "品牌"; care.scale = "170"; care.valid = 0;
        db.care.Add(care); await db.SaveChangesAsync();
        return (order.id, care.id);
    }

    private static async Task<ApiResult<SnowmeetApi.Models.Order>> Place(CoachFixture f, int orderId)
    {
        var res = await new OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(orderId, "session-1");
        return Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(res.Result).Value);
    }

    private static Task<Care> Saved(CoachFixture f, int careId) => f.NewContext().care.AsNoTracking().SingleAsync(c => c.id == careId);
    private static Task<SnowmeetApi.Models.Order> SavedOrder(CoachFixture f, int orderId) => f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId);

    [Fact]
    public async Task PlaceOrder_PersistsMaintainAndTheStatedPrice()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (o1, c1) = await Draft(f, M());
        var (o2, c2) = await Draft(f, M(edge: 1));
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal(0, (await Place(f, o2)).code);
        var one = await Saved(f, c1);
        Assert.Equal((1, 1, 0, 0), (one.maintain_board, one.need_unwax, one.seal_board, one.need_wax));
        Assert.Equal(220, one.common_charge);
        Assert.Equal(220, (await SavedOrder(f, o1)).total_amount);
        var two = await Saved(f, c2);
        Assert.Equal(280, two.common_charge);
        Assert.Equal(280, (await SavedOrder(f, o2)).total_amount);
    }

    [Theory]
    [InlineData(false)]      // 没配商品规则：回退到老写死的 20 / 30
    [InlineData(true)]
    public async Task PlaceOrder_WithOldCustomerTicket_Pays200And250(bool withRules)
    {
        const double single = 200, withEdge = 250;
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f, withRules);
        string c16a = await AddTicket(f), c16b = await AddTicket(f);
        var a = M(); a.ticket_code = c16a;
        var b = M(edge: 1); b.ticket_code = c16b;
        var (o1, id1) = await Draft(f, a);
        var (o2, id2) = await Draft(f, b);
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal(0, (await Place(f, o2)).code);
        var first = await Saved(f, id1);
        Assert.Equal((220d, 20d, c16a), (first.common_charge, first.discount, first.ticket_code));
        Assert.Equal(single, (await SavedOrder(f, o1)).total_amount);
        var second = await Saved(f, id2);
        Assert.Equal((280d, 30d, c16b), (second.common_charge, second.discount, second.ticket_code));
        Assert.Equal(withEdge, (await SavedOrder(f, o2)).total_amount);
    }

    [Fact]
    public async Task PlaceOrder_SealedMaintain_PersistsTheSealAndNoScrape()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M(maint: 1, unwax: 0, seal: 1));
        Assert.Equal(0, (await Place(f, orderId)).code);
        var saved = await Saved(f, careId);
        Assert.Equal((1, 0, 1), (saved.maintain_board, saved.need_unwax, saved.seal_board));
    }

    [Fact]
    public async Task PlaceOrder_MaintainWithoutScrapeOrSeal_DefaultsToScrape()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M(maint: 1, unwax: 0, seal: 0));
        Assert.Equal(0, (await Place(f, orderId)).code);
        var saved = await Saved(f, careId);
        Assert.Equal((1, 1, 0), (saved.maintain_board, saved.need_unwax, saved.seal_board));
    }

    [Fact]
    public async Task PlaceOrder_MaintainAndWaxBothTicked_KeepsTheMaintainOnly()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M(maint: 1, wax: 1, unwax: 1));
        Assert.Equal(0, (await Place(f, orderId)).code);
        var saved = await Saved(f, careId);
        Assert.Equal((1, 0, 220d), (saved.maintain_board, saved.need_wax, saved.common_charge));
    }

    [Fact]
    public async Task PlaceOrder_MaintainDropsOtherTicketAndCoachCard_SoNothingIsConsumedForNoDiscount()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        string freeWaxTicket = await AddTicket(f, template: 12);
        CoachCard coachCard = await f.IssueAndClaim(CoachFixture.StudentA);
        var withTicket = M(); withTicket.ticket_code = freeWaxTicket; withTicket.discount = 40;
        var withCoach = M(); withCoach.coach_card_id = coachCard.id;
        var (o1, c1) = await Draft(f, withTicket);
        var (o2, c2) = await Draft(f, withCoach);
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal(0, (await Place(f, o2)).code);
        var first = await Saved(f, c1);
        Assert.Null(first.ticket_code); Assert.Equal(0, first.discount); Assert.Equal(220, first.common_charge);
        Assert.Equal(220, (await SavedOrder(f, o1)).total_amount);
        var second = await Saved(f, c2);
        Assert.Null(second.coach_card_id); Assert.Equal(0, second.discount);
        Assert.Equal(220, (await SavedOrder(f, o2)).total_amount);
    }

    [Fact]
    public async Task PlaceOrder_StoreWithoutMaintainPrice_IsRefused()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M(), shop: "崇礼旗舰店");
        var r = await Place(f, orderId);
        Assert.Equal(1, r.code);
        Assert.Contains("养板", r.message);
        Assert.Equal(0, (await Saved(f, careId)).valid);     // 没落成有效订单
    }

    // ================= 生效：任务链 =================

    private static async Task<List<string>> TaskNames(CoachFixture f, int careId) =>
        (await f.NewContext().careTask.AsNoTracking().Where(t => t.care_id == careId).ToListAsync())
            .OrderBy(t => t.sort).ThenBy(t => t.id).Select(t => t.task_name).ToList();

    private static readonly string[] MaintainSteps = { "养板打蜡1", "养板刮蜡1", "养板打蜡2", "养板刮蜡2", "养板打蜡3" };

    [Fact]
    public async Task Effect_MaintainWithScrape_CreatesTheFiveStepsThenScrapeThenFinish_AndNoHotWax()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M());
        Assert.Equal(0, (await Place(f, orderId)).code);
        await Ctl(f).EffectCareOrder(orderId);
        Assert.Equal(new[] { "安全检查" }.Concat(MaintainSteps).Concat(new[] { "刮蜡", "发板" }).ToList(), await TaskNames(f, careId));
    }

    [Fact]
    public async Task Effect_MaintainWithEdgeAndSeal_StartsWithTheEdge_AndHasNoScrapeStep()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M(edge: 1, unwax: 0, seal: 1));
        Assert.Equal(0, (await Place(f, orderId)).code);
        await Ctl(f).EffectCareOrder(orderId);
        Assert.Equal(new[] { "安全检查", "修刃" }.Concat(MaintainSteps).Concat(new[] { "发板" }).ToList(), await TaskNames(f, careId));
    }

    [Fact]
    public async Task Effect_HotWaxCare_StillGetsTheHotWaxTask_NotTheMaintainSteps()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        var (orderId, careId) = await Draft(f, M(maint: 0, wax: 1, unwax: 1));
        Assert.Equal(0, (await Place(f, orderId)).code);
        await Ctl(f).EffectCareOrder(orderId);
        Assert.Equal(new List<string> { "安全检查", "热蜡", "刮蜡", "发板" }, await TaskNames(f, careId));
    }

    [Fact]
    public async Task Effect_SealedMaintain_PickupStillIssuesTheOpenBoardTicket()
    {
        await using var f = await CoachFixture.Create();
        await AddMaintainPrices(f);
        f.Db.ticketTemplate.Add(new TicketTemplate { id = CareSealRules.SealTicketTemplateId, name = "开板刮蜡券", memo = "封板后，免费刮蜡", type = "养护券",
            biz_type = "养护", miniapp_recept_path = "", sharable = 0, valid = 1 });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var (orderId, careId) = await Draft(f, M(unwax: 0, seal: 1));
        Assert.Equal(0, (await Place(f, orderId)).code);
        await Ctl(f).EffectCareOrder(orderId);
        int finish = await f.NewContext().careTask.AsNoTracking().Where(t => t.care_id == careId && t.task_name == "发板").Select(t => t.id).SingleAsync();
        await Ctl(f).SetTaskStatus(finish, "已完成", "扫码取板", "session-7");
        var tickets = await f.NewContext().ticket.AsNoTracking().Where(t => t.template_id == CareSealRules.SealTicketTemplateId).ToListAsync();
        Assert.Equal(careId, Assert.Single(tickets).biz_id);
    }

    // ================= 订单服务项标签 =================

    [Fact]
    public void OrderServicesLabel_ShowsMaintainBoard()
    {
        var order = new SnowmeetApi.Models.Order
        {
            type = "养护", valid = 1, shop = Wanlong,
            cares = new List<Care> { new() { valid = 1, maintain_board = 1, need_unwax = 1, need_edge = 1, equipment = "双板" } }
        };
        string services = order.careProperties!.services;
        Assert.Contains("修刃", services);
        Assert.Contains("养板", services);
        Assert.Contains("刮蜡", services);
        Assert.DoesNotContain("打蜡", services);
    }
}

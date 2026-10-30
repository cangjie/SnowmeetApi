using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 万龙服务中心「只选了刮蜡」的养护单一口价（2026-10-30 用户提出）：
/// 会员之前有过消费记录 → 80 元，否则（新会员 / 没有会员）→ 100 元。
///
/// 「消费记录」口径（用户 2026-10-30 确认）：同一会员在本单之外，有任意业务类型的订单，
/// 订单有效（valid=1）、非测试（is_test=0），且至少一笔 valid=1、status=支付成功、is_debt=0、amount>0 的支付。
/// 招待 / 质保（没有真实支付）、未付款、已取消、测试单都不算。
///
/// 刮蜡与别的项目一起选、别的门店、质保招待、会员卡 / 非雪季：保持原有计费，不受影响。
/// 万龙服务中心的单项价在夹具里是 120、双项 168，和 80 / 100 都对不上，一眼能看出走的是哪条规则。
/// </summary>
public class CareScrapeOnlyPricingTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private const string Wanlong = "万龙服务中心";
    private static CareController Care(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private static Care C(int edge = 0, int wax = 0, int freeWax = 0, int unwax = 1) =>
        new() { equipment = "双板", need_edge = edge, need_wax = wax, free_wax = freeWax, need_unwax = unwax, biz_type = "雪季养护" };

    /// <summary>给会员造一张订单，默认就是一张「有效、非测试、已付款 100 元」的订单。</summary>
    private static async Task AddOrder(CoachFixture f, int memberId, string type = "零售", int valid = 1, int isTest = 0,
        string? payStatus = "支付成功", int isDebt = 0, double amount = 100, int payValid = 1, string shop = Wanlong)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = type, shop = shop, member_id = memberId, valid = valid, is_test = isTest,
            code = "T_" + Guid.NewGuid().ToString("N")[..10] };
        db.order.Add(order); await db.SaveChangesAsync();
        if (payStatus != null)
        {
            db.orderPayment.Add(new OrderPayment { order_id = order.id, amount = amount, status = payStatus, is_debt = isDebt, valid = payValid });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<double> Price(CoachFixture f, Care care, int? memberId, string shop = Wanlong)
    {
        var (commonCharge, ticketDiscount, _) = await Care(f).CalcChargeWithCoach(shop, care, null, null, memberId);
        Assert.Equal(0, ticketDiscount);
        return commonCharge;
    }

    // ---------- 老会员 80 / 其他 100 ----------

    [Theory]
    [InlineData("零售")]
    [InlineData("租赁")]
    [InlineData("养护")]
    [InlineData("雪票")]
    [InlineData("餐饮")]
    public async Task ScrapeOnly_MemberWithAnyPaidOrder_Pays80(string earlierOrderType)
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA, earlierOrderType);
        Assert.Equal(80, await Price(f, C(), CoachFixture.StudentA));
    }

    [Fact]
    public async Task ScrapeOnly_MemberWithoutAnyOrder_Pays100()
    {
        await using var f = await CoachFixture.Create();
        Assert.Equal(100, await Price(f, C(), CoachFixture.StudentA));
    }

    [Fact]
    public async Task ScrapeOnly_NoMember_Pays100()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);   // 别人有消费记录不能带到散客身上
        Assert.Equal(100, await Price(f, C(), null));
    }

    [Fact]
    public async Task ScrapeOnly_AnotherMembersOrderDoesNotMakeMeAnOldMember()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentB);
        Assert.Equal(100, await Price(f, C(), CoachFixture.StudentA));
        Assert.Equal(80, await Price(f, C(), CoachFixture.StudentB));
    }

    [Theory]
    [InlineData("order_not_valid")]
    [InlineData("order_is_test")]
    [InlineData("no_payment_at_all")]       // 招待 / 质保单没有真实支付
    [InlineData("payment_pending")]
    [InlineData("payment_cancelled")]
    [InlineData("payment_is_debt")]
    [InlineData("payment_zero_amount")]
    [InlineData("payment_not_valid")]
    public async Task ScrapeOnly_OrdersThatAreNotRealConsumption_DoNotCount(string scenario)
    {
        await using var f = await CoachFixture.Create();
        int m = CoachFixture.StudentA;
        switch (scenario)
        {
            case "order_not_valid": await AddOrder(f, m, valid: 0); break;
            case "order_is_test": await AddOrder(f, m, isTest: 1); break;
            case "no_payment_at_all": await AddOrder(f, m, type: "养护", payStatus: null); break;
            case "payment_pending": await AddOrder(f, m, payStatus: "待支付"); break;
            case "payment_cancelled": await AddOrder(f, m, payStatus: "取消"); break;
            case "payment_is_debt": await AddOrder(f, m, isDebt: 1); break;
            case "payment_zero_amount": await AddOrder(f, m, amount: 0); break;
            case "payment_not_valid": await AddOrder(f, m, payValid: 0); break;
        }
        Assert.Equal(100, await Price(f, C(), m));
    }

    [Fact]
    public async Task ScrapeOnly_OneQualifyingOrderAmongNonQualifyingOnes_StillCounts()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA, valid: 0);
        await AddOrder(f, CoachFixture.StudentA, payStatus: "待支付");
        await AddOrder(f, CoachFixture.StudentA, type: "租赁");
        Assert.Equal(80, await Price(f, C(), CoachFixture.StudentA));
    }

    [Theory]
    [InlineData("万龙体验中心")]   // 体验中心的养护归服务中心（ResolveCareShop），预览与下单必须同一结果
    [InlineData(" 万龙服务中心 ")]
    public async Task ScrapeOnly_ShopsThatResolveToWanlongService_UseTheFixedPrice(string shop)
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        Assert.Equal(80, await Price(f, C(), CoachFixture.StudentA, shop));
        Assert.Equal(100, await Price(f, C(), CoachFixture.StudentB, shop));
    }

    [Theory]
    [InlineData("崇礼旗舰店")]
    [InlineData("南山")]
    public async Task ScrapeOnly_OtherShops_AreUnchanged_StillFree(string shop)
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        Assert.Equal(0, await Price(f, C(), CoachFixture.StudentA, shop));
        Assert.Equal(0, await Price(f, C(), CoachFixture.StudentB, shop));
    }

    // ---------- 刮蜡与别的项目一起选：保持原有计费 ----------

    [Theory]
    [InlineData(1, 0, 0, 120)]   // 修刃 + 刮蜡 = 单项
    [InlineData(0, 1, 0, 120)]   // 热蜡 + 刮蜡 = 单项
    [InlineData(1, 1, 0, 168)]   // 修刃 + 热蜡 + 刮蜡 = 双项
    [InlineData(0, 0, 1, 0)]     // 机打蜡 + 刮蜡：机打蜡与刮蜡都不计费
    [InlineData(1, 0, 1, 120)]   // 修刃 + 机打蜡 + 刮蜡
    public async Task ScrapePlusOtherServices_KeepsTheOriginalPricing_ForOldAndNewMembers(int edge, int wax, int freeWax, double expected)
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        Assert.Equal(expected, await Price(f, C(edge, wax, freeWax), CoachFixture.StudentA));   // 老会员
        Assert.Equal(expected, await Price(f, C(edge, wax, freeWax), CoachFixture.StudentB));   // 新会员
        Assert.Equal(expected, await Price(f, C(edge, wax, freeWax), null));                    // 散客
    }

    [Fact]
    public async Task OtherServiceSetups_AreUnchanged()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        Assert.Equal(120, await Price(f, C(wax: 1, unwax: 0), CoachFixture.StudentA));      // 只热蜡
        Assert.Equal(0, await Price(f, C(freeWax: 1, unwax: 0), CoachFixture.StudentA));    // 只机打蜡
        Assert.Equal(0, await Price(f, C(unwax: 0), CoachFixture.StudentA));                // 什么服务都没选
    }

    [Fact]
    public async Task ScrapeOnly_WarrantyEntertainAndSummer_AreNotCharged()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        var warranty = C(); warranty.warranty = true;
        var entertain = C(); entertain.entertain = true;
        var summerNow = C(); summerNow.summer = "now"; summerNow.biz_type = "非雪季养护";
        Assert.Equal(0, await Price(f, warranty, CoachFixture.StudentA));
        Assert.Equal(0, await Price(f, entertain, CoachFixture.StudentA));
        Assert.Equal(330, await Price(f, summerNow, CoachFixture.StudentA));   // 非雪季走自己的价（夹具没配 715，落到 330 兜底），不是 80/100
    }

    // ---------- 开单页预览：服务端同一套规则 ----------

    private static async Task<(double commonCharge, double careCharge)> Preview(CoachFixture f, Care care, int? memberId, string shop = Wanlong)
    {
        var res = await Care(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = shop, memberId = memberId, care = care }, "session-7");
        var env = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal(0, env.code);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(env.data);
        return (json.GetProperty("commonCharge").GetDouble(), json.GetProperty("care").GetProperty("common_charge").GetDouble());
    }

    [Fact]
    public async Task Preview_ReturnsTheSamePriceAsThePricingHelper()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        Assert.Equal((80d, 80d), await Preview(f, C(), CoachFixture.StudentA));
        Assert.Equal((100d, 100d), await Preview(f, C(), CoachFixture.StudentB));
        Assert.Equal((100d, 100d), await Preview(f, C(), null));
        Assert.Equal((0d, 0d), await Preview(f, C(), CoachFixture.StudentA, "南山"));
        // 开单页点掉刮蜡再点上热蜡：不再是单刮蜡，回到原有计费
        Assert.Equal((120d, 120d), await Preview(f, C(wax: 1, unwax: 1), CoachFixture.StudentA));
    }

    // ---------- 只选刮蜡：不选任何券、卡、学员卡 ----------

    private static async Task<System.Text.Json.JsonElement> PreviewJson(CoachFixture f, Care care, int? memberId, string shop = Wanlong)
    {
        var res = await Care(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = shop, memberId = memberId, care = care }, "session-7");
        var env = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal(0, env.code);
        return System.Text.Json.JsonSerializer.SerializeToElement(env.data);
    }

    private static async Task<int> AddPunchCard(CoachFixture f, int memberId, int total = 10, int punches = 0)
    {
        var db = f.NewContext();
        var card = new PunchCard { biz_type = "养护", card_name = "测试次卡", member_id = memberId, total = total, punches = punches };
        db.punchCard.Add(card); await db.SaveChangesAsync();
        return card.id;
    }

    [Fact]
    public async Task Preview_ScrapeOnly_ClearsTicketCardAndDiscountAndReportsIt()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        int cardId = await AddPunchCard(f, CoachFixture.StudentA);
        var care = C(); care.ticket_code = "OLDCUST16"; care.use_card = true; care.card_id = cardId; care.card_name = "测试次卡"; care.discount = 20;
        var json = await PreviewJson(f, care, CoachFixture.StudentA);
        Assert.Equal(80, json.GetProperty("commonCharge").GetDouble());
        Assert.True(json.GetProperty("benefitsCleared").GetBoolean());
        var returned = json.GetProperty("care");
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("ticket_code").ValueKind);
        Assert.False(returned.GetProperty("use_card").GetBoolean());
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("card_id").ValueKind);
        Assert.Equal(0, returned.GetProperty("discount").GetDouble());
        Assert.Equal(80, returned.GetProperty("common_charge").GetDouble());
    }

    [Fact]
    public async Task Preview_ScrapeOnly_ClearsACoachCardToo_NoQuote()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var care = C(); care.coach_card_id = card.id;
        var json = await PreviewJson(f, care, CoachFixture.StudentA);
        Assert.Equal(100, json.GetProperty("commonCharge").GetDouble());
        Assert.True(json.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("care").GetProperty("coach_card_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("coach").ValueKind);
    }

    [Fact]
    public async Task Preview_ScrapeOnly_WithNothingSelected_ReportsNothingCleared_AndKeepsAManualDiscount()
    {
        await using var f = await CoachFixture.Create();
        var care = C(); care.discount = 10;
        var json = await PreviewJson(f, care, CoachFixture.StudentA);
        Assert.False(json.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal(10, json.GetProperty("care").GetProperty("discount").GetDouble());
    }

    [Fact]
    public async Task Preview_MixedServicesAndOtherShops_KeepTheirTicketsAndCards()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        int cardId = await AddPunchCard(f, CoachFixture.StudentA);
        var mixed = C(edge: 1, wax: 1); mixed.ticket_code = "OLDCUST16"; mixed.discount = 20;
        var json = await PreviewJson(f, mixed, CoachFixture.StudentA);
        Assert.False(json.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal("OLDCUST16", json.GetProperty("care").GetProperty("ticket_code").GetString());
        Assert.Equal(20, json.GetProperty("care").GetProperty("discount").GetDouble());
        var otherShop = C(); otherShop.use_card = true; otherShop.card_id = cardId; otherShop.card_name = "测试次卡";
        var other = await PreviewJson(f, otherShop, CoachFixture.StudentA, "南山");
        Assert.False(other.GetProperty("benefitsCleared").GetBoolean());
        Assert.True(other.GetProperty("care").GetProperty("use_card").GetBoolean());
    }

    // ---------- 下单：服务端落库金额与预览一致 ----------

    private static async Task<(int orderId, int careId)> Draft(CoachFixture f, int? memberId, Care care, string shop = Wanlong)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = shop, member_id = memberId, valid = 0, recepting = 1, staff_id = 9,
            code = "YH_20261030_" + Guid.NewGuid().ToString("N")[..6] };
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

    [Theory]
    [InlineData(true, 80)]
    [InlineData(false, 100)]
    public async Task PlaceOrder_ScrapeOnly_StoresTheFixedPriceAndTotals(bool oldMember, double expected)
    {
        await using var f = await CoachFixture.Create();
        if (oldMember) await AddOrder(f, CoachFixture.StudentA);
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, C());
        var r = await Place(f, orderId);
        Assert.Equal(0, r.code);
        Assert.Equal(expected, (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == careId)).common_charge);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(expected, order.total_amount);
        Assert.Equal(expected, order.paying_amount);
    }

    [Fact]
    public async Task PlaceOrder_ScrapeOnly_ThisOrderItselfNeverMakesTheMemberAnOldMember()
    {
        await using var f = await CoachFixture.Create();
        // 同一会员连开两张单：第一张下单（尚未付款）不算消费记录，第二张仍是 100
        var (o1, _) = await Draft(f, CoachFixture.StudentA, C());
        var (o2, c2) = await Draft(f, CoachFixture.StudentA, C());
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal(0, (await Place(f, o2)).code);
        Assert.Equal(100, (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == c2)).common_charge);
    }

    [Fact]
    public async Task PlaceOrder_ScrapeOnly_NoMemberPays100_AndOtherShopsAndMixedServicesAreUnchanged()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        var (noMember, noMemberCare) = await Draft(f, null, C());
        var (nanshan, nanshanCare) = await Draft(f, CoachFixture.StudentA, C(), "南山");
        var (mixed, mixedCare) = await Draft(f, CoachFixture.StudentA, C(edge: 1, wax: 1));
        foreach (int id in new[] { noMember, nanshan, mixed }) Assert.Equal(0, (await Place(f, id)).code);
        Assert.Equal(100, (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == noMemberCare)).common_charge);
        Assert.Equal(0, (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == nanshanCare)).common_charge);
        Assert.Equal(168, (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == mixedCare)).common_charge);
    }

    [Fact]
    public async Task PlaceOrder_ScrapeOnly_DropsTheTicketSoItIsNotConsumedForNoDiscount()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        // 开单页会给老顾客自动带上券 16；单刮蜡是一口价、券没有任何优惠，下单时不能带着券进生效环节被核销
        var scrape = C(); scrape.ticket_code = "OLDCUST16";
        var mixed = C(edge: 1, wax: 1); mixed.ticket_code = "OLDCUST17";
        var (o1, c1) = await Draft(f, CoachFixture.StudentA, scrape);
        var (o2, c2) = await Draft(f, CoachFixture.StudentA, mixed);
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal(0, (await Place(f, o2)).code);
        Assert.Null((await f.Db.care.AsNoTracking().SingleAsync(c => c.id == c1)).ticket_code);
        Assert.Equal("OLDCUST17", (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == c2)).ticket_code);   // 其它单照旧
    }

    [Fact]
    public async Task PlaceOrder_ScrapeOnly_DropsTheCardEvenIfItWouldFailTheBalanceCheck_AndTheDiscountThatCameWithTheTicket()
    {
        await using var f = await CoachFixture.Create();
        int usedUpCard = await AddPunchCard(f, CoachFixture.StudentA, total: 1, punches: 1);   // 剩余 0 次：带着卡本来过不了余额校验
        var care = C(); care.use_card = true; care.card_id = usedUpCard; care.card_name = "测试次卡";
        care.ticket_code = "OLDCUST16"; care.discount = 20;
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, care);
        var r = await Place(f, orderId);
        Assert.Equal(0, r.code);
        var saved = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == careId);
        Assert.False(saved.use_card); Assert.Null(saved.card_id); Assert.Null(saved.card_name); Assert.Null(saved.ticket_code);
        Assert.Equal(0, saved.discount);
        Assert.Equal(100, saved.common_charge);
        Assert.Equal(100, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).total_amount);   // 没有 20 的残留减免
    }

    [Fact]
    public async Task PlaceOrder_ScrapeOnly_DropsACoachCard()
    {
        await using var f = await CoachFixture.Create();
        await AddOrder(f, CoachFixture.StudentA);
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var care = C(); care.coach_card_id = card.id;
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, care);
        Assert.Equal(0, (await Place(f, orderId)).code);
        var saved = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == careId);
        Assert.Null(saved.coach_card_id);
        Assert.Equal(80, saved.common_charge);
        Assert.Equal(0, saved.discount);
    }

    [Fact]
    public async Task PlaceOrder_MixedServicesAtWanlong_KeepTheTicketAndDiscount()
    {
        await using var f = await CoachFixture.Create();
        var mixed = C(edge: 1, wax: 1); mixed.ticket_code = "OLDCUST16"; mixed.discount = 20;
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, mixed);
        Assert.Equal(0, (await Place(f, orderId)).code);
        var saved = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == careId);
        Assert.Equal("OLDCUST16", saved.ticket_code);
        Assert.Equal(20, saved.discount);
    }
}

/// <summary>「只选了刮蜡」一口价的纯规则：什么算单刮蜡、哪些店适用、两个价各多少。</summary>
public class CareScrapeOnlyRulesTests
{
    private static Care C(int edge = 0, int wax = 0, int freeWax = 0, int unwax = 1) =>
        new() { need_edge = edge, need_wax = wax, free_wax = freeWax, need_unwax = unwax };

    [Theory]
    [InlineData(0, 0, 0, 1, true)]    // 只刮蜡
    [InlineData(1, 0, 0, 1, false)]   // 修刃 + 刮蜡
    [InlineData(0, 1, 0, 1, false)]   // 热蜡 + 刮蜡
    [InlineData(0, 0, 1, 1, false)]   // 机打蜡 + 刮蜡
    [InlineData(1, 1, 0, 1, false)]   // 双项 + 刮蜡
    [InlineData(0, 0, 0, 0, false)]   // 什么都没选
    [InlineData(0, 1, 0, 0, false)]   // 只热蜡
    public void IsScrapeOnly_OnlyWhenScrapeIsTheOnlyService(int edge, int wax, int freeWax, int unwax, bool expected)
    {
        Assert.Equal(expected, CarePricingRules.IsScrapeOnly(C(edge, wax, freeWax, unwax)));
    }

    [Fact]
    public void IsScrapeOnly_FalseForNull_Summer_Card_Warranty_Entertain()
    {
        Assert.False(CarePricingRules.IsScrapeOnly(null!));
        var summerNow = C(); summerNow.summer = "now";
        var summerLater = C(); summerLater.summer = "later";
        var summerBiz = C(); summerBiz.biz_type = "非雪季养护";
        var warranty = C(); warranty.warranty = true;
        var entertain = C(); entertain.entertain = true;
        foreach (Care care in new[] { summerNow, summerLater, summerBiz, warranty, entertain })
            Assert.False(CarePricingRules.IsScrapeOnly(care));
        var urgent = C(); urgent.urgent = 1;       // 立等不改变「只刮蜡」
        Assert.True(CarePricingRules.IsScrapeOnly(urgent));
    }

    [Fact]
    public void IsScrapeOnly_IgnoresWhichTicketOrCardIsSelected_TheyGetCleared()
    {
        var withTicket = C(); withTicket.ticket_code = "T";
        var withCard = C(); withCard.use_card = true; withCard.card_id = 5;
        var withCoach = C(); withCoach.coach_card_id = 9;
        foreach (Care care in new[] { withTicket, withCard, withCoach })
            Assert.True(CarePricingRules.IsScrapeOnly(care));
    }

    private static Care Loaded(int edge = 0, int wax = 0, int freeWax = 0, int unwax = 1)
    {
        Care care = C(edge, wax, freeWax, unwax);
        care.ticket_code = "T16"; care.use_card = true; care.card_id = 5; care.card_name = "次卡";
        care.coach_card_id = 9; care.discount = 20; care.ticket_discount = 5;
        return care;
    }

    [Fact]
    public void ClearBenefitsForScrapeOnly_ClearsTicketCardCoachCardAndTheirDiscount()
    {
        Care care = Loaded();
        Assert.True(CarePricingRules.ClearBenefitsForScrapeOnly("万龙服务中心", care));
        Assert.Null(care.ticket_code);
        Assert.False(care.use_card); Assert.Null(care.card_id); Assert.Null(care.card_name);
        Assert.Null(care.coach_card_id);
        Assert.Equal(0, care.discount); Assert.Equal(0, care.ticket_discount);
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData("card")]
    [InlineData("coach")]
    public void ClearBenefitsForScrapeOnly_AnyOneOfThemIsEnough(string which)
    {
        Care care = C(); care.discount = 20;
        if (which == "ticket") care.ticket_code = "T";
        if (which == "card") { care.use_card = true; care.card_id = 5; }
        if (which == "coach") care.coach_card_id = 9;
        Assert.True(CarePricingRules.ClearBenefitsForScrapeOnly("万龙服务中心", care));
        Assert.Equal(0, care.discount);
    }

    [Fact]
    public void ClearBenefitsForScrapeOnly_NothingSelected_ReturnsFalseAndKeepsAManualDiscount()
    {
        Care care = C(); care.discount = 10;
        Assert.False(CarePricingRules.ClearBenefitsForScrapeOnly("万龙服务中心", care));
        Assert.Equal(10, care.discount);
    }

    [Theory]
    [InlineData("南山", 0, 0, 0, 1)]               // 别的店不动
    [InlineData("崇礼旗舰店", 0, 0, 0, 1)]
    [InlineData("万龙服务中心", 1, 0, 0, 1)]       // 刮蜡 + 修刃
    [InlineData("万龙服务中心", 0, 1, 0, 1)]       // 刮蜡 + 热蜡
    [InlineData("万龙服务中心", 0, 0, 1, 1)]       // 刮蜡 + 机打蜡
    [InlineData("万龙服务中心", 0, 0, 0, 0)]       // 没选刮蜡
    public void ClearBenefitsForScrapeOnly_LeavesEverythingElseAlone(string shop, int edge, int wax, int freeWax, int unwax)
    {
        Care care = Loaded(edge, wax, freeWax, unwax);
        Assert.False(CarePricingRules.ClearBenefitsForScrapeOnly(shop, care));
        Assert.Equal("T16", care.ticket_code); Assert.True(care.use_card); Assert.Equal(5, care.card_id);
        Assert.Equal(9, care.coach_card_id); Assert.Equal(20, care.discount);
    }

    [Fact]
    public void ClearBenefitsForScrapeOnly_WarrantyEntertainAndSummerAreNotTouched()
    {
        Care warranty = Loaded(); warranty.warranty = true;
        Care entertain = Loaded(); entertain.entertain = true;
        Care summer = Loaded(); summer.summer = "later";
        foreach (Care care in new[] { warranty, entertain, summer })
        {
            Assert.False(CarePricingRules.ClearBenefitsForScrapeOnly("万龙服务中心", care));
            Assert.Equal("T16", care.ticket_code);
        }
    }

    [Theory]
    [InlineData("万龙服务中心", true)]
    [InlineData(" 万龙服务中心 ", true)]
    [InlineData("万龙体验中心", true)]    // 先归并到服务中心（与 PlaceCareOrder 一致）
    [InlineData("南山", false)]
    [InlineData("崇礼旗舰店", false)]
    [InlineData("怀北", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AppliesScrapeOnlyPrice_OnlyAtWanlongService(string? shop, bool expected)
    {
        Assert.Equal(expected, CarePricingRules.AppliesScrapeOnlyPrice(shop, C()));
    }

    [Fact]
    public void AppliesScrapeOnlyPrice_NeedsScrapeAlone()
    {
        Assert.False(CarePricingRules.AppliesScrapeOnlyPrice("万龙服务中心", C(edge: 1)));
        Assert.False(CarePricingRules.AppliesScrapeOnlyPrice("万龙服务中心", C(wax: 1)));
    }

    [Fact]
    public void ScrapeOnlyPrice_OldMember80_Otherwise100()
    {
        Assert.Equal(80, CarePricingRules.ScrapeOnlyPrice(true));
        Assert.Equal(100, CarePricingRules.ScrapeOnlyPrice(false));
    }
}

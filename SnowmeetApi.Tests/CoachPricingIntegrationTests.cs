using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

public class CoachPricingIntegrationTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static CareController Care(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private static Care C(int edge, int wax, int urgent = 0, int? cardId = null) =>
        new() { equipment = "双板", need_edge = edge, need_wax = wax, urgent = urgent, coach_card_id = cardId, biz_type = "雪季养护" };

    // ---------- 养护：门店 × 单项/双项 ----------

    [Theory]
    [InlineData("万龙服务中心", 1, 1, 168, 33.6)]   // 双项 8 折
    [InlineData("万龙服务中心", 1, 0, 120, 24)]     // 单项 8 折
    [InlineData("万龙体验中心", 1, 1, 168, 33.6)]   // 体验中心的养护归服务中心
    [InlineData("崇礼旗舰店", 1, 1, 188, 60)]       // 双项一口价 128
    [InlineData("崇礼旗舰店", 0, 1, 138, 70)]       // 单项一口价 68
    public async Task Care_QuotePerShopAndTier(string shop, int edge, int wax, double common, double discount)
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var (commonCharge, ticketDiscount, coach) = await Care(f).CalcChargeWithCoach(shop, C(edge, wax, 0, card.id), null, null, CoachFixture.StudentA);
        Assert.Equal(common, commonCharge);
        Assert.Equal(0, ticketDiscount);
        Assert.NotNull(coach);
        Assert.Equal(discount, coach!.discount);
        Assert.Equal("张教练", coach.coachName);
    }

    [Fact]
    public async Task Care_UrgentSurchargeIsNotDiscounted_AndShopsWithoutUrgentFallBack()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        // 万龙双项加急 = 228，基础价 168 打 8 折，加急加价 60 照收
        var (urgentCharge, _, urgent) = await Care(f).CalcChargeWithCoach("万龙服务中心", C(1, 1, 1, card.id), null, null, CoachFixture.StudentA);
        Assert.Equal(228, urgentCharge);
        Assert.Equal(168, urgent!.baseAmount);
        Assert.Equal(33.6, urgent.discount);
        // 崇礼没有加急商品：按非加急价 188，一口价 128 → 优惠 60
        var (cl, _, clq) = await Care(f).CalcChargeWithCoach("崇礼旗舰店", C(1, 1, 1, card.id), null, null, CoachFixture.StudentA);
        Assert.Equal(188, cl);
        Assert.Equal(60, clq!.discount);
    }

    [Fact]
    public async Task Care_FixedPriceNeverRaisesAPrice_AndMissingCellMeansNoDiscount()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new()
        { new("万龙服务中心", "单项", "fixed", 500), new("崇礼旗舰店", "双项", "rate", 0.5) }, null));
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var (_, _, hi) = await Care(f).CalcChargeWithCoach("万龙服务中心", C(1, 0, 0, card.id), null, null, CoachFixture.StudentA);
        Assert.Equal(0, hi!.discount);          // 一口价 500 高于原价 120，不涨价
        var (_, _, none) = await Care(f).CalcChargeWithCoach("万龙服务中心", C(1, 1, 0, card.id), null, null, CoachFixture.StudentA);
        Assert.Equal(0, none!.discount);        // 万龙双项没设价格 = 无优惠
        var (_, _, ns) = await Care(f).CalcChargeWithCoach("南山", C(1, 1, 0, card.id), null, null, CoachFixture.StudentA);
        Assert.Equal(0, ns!.discount);          // 南山不在价格设置范围
    }

    [Fact]
    public async Task Care_ExcludedOrInvalid_Combinations()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        CareController c = Care(f);
        // 质保 / 招待 / 非雪季：不享受，也不报错
        var warranty = C(1, 1, 0, card.id); warranty.warranty = true;
        Assert.Null((await c.CalcChargeWithCoach("万龙服务中心", warranty, null, null, CoachFixture.StudentA)).coach);
        var entertain = C(1, 1, 0, card.id); entertain.entertain = true;
        Assert.Null((await c.CalcChargeWithCoach("万龙服务中心", entertain, null, null, CoachFixture.StudentA)).coach);
        var summer = C(0, 0, 0, card.id); summer.summer = "now";
        Assert.Null((await c.CalcChargeWithCoach("万龙服务中心", summer, null, null, CoachFixture.StudentA)).coach);
        // 没有卡 id：走原有计价
        var plain = await c.CalcChargeWithCoach("万龙服务中心", C(1, 1), null, null, CoachFixture.StudentA);
        Assert.Equal(168, plain.commonCharge);
        Assert.Null(plain.coach);
        // 与券 / 会员卡互斥
        var withTicketCode = C(1, 1, 0, card.id); withTicketCode.ticket_code = "123456789";
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CalcChargeWithCoach("万龙服务中心", withTicketCode, null, null, CoachFixture.StudentA));
        var withUseCard = C(1, 1, 0, card.id); withUseCard.use_card = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CalcChargeWithCoach("万龙服务中心", withUseCard, null, null, CoachFixture.StudentA));
        // 散客 / 别人的卡 / 已撤销 / 已过期
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CalcChargeWithCoach("万龙服务中心", C(1, 1, 0, card.id), null, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CalcChargeWithCoach("万龙服务中心", C(1, 1, 0, card.id), null, null, CoachFixture.StudentB));
        await f.Db.coachCard.Where(x => x.id == card.id).ExecuteUpdateAsync(s => s.SetProperty(x => x.end_date, (DateTime?)DateTime.Now.AddDays(-1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Care(f).CalcChargeWithCoach("万龙服务中心", C(1, 1, 0, card.id), null, null, CoachFixture.StudentA));
        await f.Db.coachCard.Where(x => x.id == card.id).ExecuteUpdateAsync(s => s.SetProperty(x => x.end_date, (DateTime?)DateTime.Now.AddDays(30)).SetProperty(x => x.status, CoachCard.StatusRevoked));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Care(f).CalcChargeWithCoach("万龙服务中心", C(1, 1, 0, card.id), null, null, CoachFixture.StudentA));
    }

    [Fact]
    public async Task Care_PreviewEndpointReturnsCoachAndFloorsTheDiscount()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var ok = await Care(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = "万龙服务中心", memberId = CoachFixture.StudentA, care = C(1, 1, 0, card.id) }, "session-7");
        var env = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(ok.Result).Value);
        Assert.Equal(0, env.code);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(env.data);
        Assert.Equal(168, json.GetProperty("commonCharge").GetDouble());
        Assert.Equal(33.6, json.GetProperty("care").GetProperty("discount").GetDouble());
        Assert.Equal(33.6, json.GetProperty("coach").GetProperty("discount").GetDouble());
        // 店员手动加大的减免保留（保底，不覆盖）
        var manual = C(1, 1, 0, card.id); manual.discount = 50;
        var kept = await Care(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = "万龙服务中心", memberId = CoachFixture.StudentA, care = manual }, "session-7");
        var keptJson = System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(kept.Result).Value).data);
        Assert.Equal(50, keptJson.GetProperty("care").GetProperty("discount").GetDouble());
        // 与券同时选 → 业务错误，不是 500
        var both = C(1, 1, 0, card.id); both.ticket_code = "123456789";
        var bad = await Care(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = "万龙服务中心", memberId = CoachFixture.StudentA, care = both }, "session-7");
        Assert.Equal(1, Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(bad.Result).Value).code);
    }

    [Fact]
    public async Task UsableCardsForTheSelectorShowEstimatedDiscountPerContext()
    {
        await using var f = await CoachFixture.Create();
        await f.IssueAndClaim(CoachFixture.StudentA);
        var care = await f.Pricing().ListUsableAsync(CoachFixture.StudentA, "养护", "崇礼旗舰店", new Care { equipment = "", need_edge = 1, need_wax = 1 }, 188, 188, 0, DateTime.Now);
        Assert.Single(care);
        Assert.True(care[0].applicable);
        Assert.Equal(60, care[0].estimatedDiscount);
        Assert.Contains("崇礼旗舰店", care[0].benefitText);
        var none = await f.Pricing().ListUsableAsync(CoachFixture.StudentA, "养护", "南山", new Care { equipment = "", need_edge = 1, need_wax = 1 }, 150, 150, 0, DateTime.Now);
        Assert.False(none[0].applicable);
        var rental = await f.Pricing().ListUsableAsync(CoachFixture.StudentA, "租赁", null, null, 0, 0, 540, DateTime.Now);
        Assert.Equal(54, rental[0].estimatedDiscount);
        Assert.Empty(await f.Pricing().ListUsableAsync(CoachFixture.StudentB, "养护", "万龙服务中心", null, 0, 0, 0, DateTime.Now));
        // 控制器：店员 ≥ 100 才能查
        var ctl = new SnowmeetApi.Controllers.CoachPlatform.CoachCardController(f.NewContext(), f.Config, new Http());
        Assert.Equal(3, (await ctl.ListUsableCards("session-3", CoachFixture.StudentA)).code);
        var ok = await ctl.ListUsableCards("session-7", CoachFixture.StudentA, "养护", "万龙服务中心", "双项", 168, 168);
        Assert.Equal(0, ok.code);
        Assert.Equal(33.6, System.Text.Json.JsonSerializer.SerializeToElement(ok.data).GetProperty("cards")[0].GetProperty("estimatedDiscount").GetDouble());
        // 客户端不传 basePrice：非加急取服务费本身，加急由服务端查非加急价（168），加急加价 60 不打折
        double Est(ApiResult<object> r) => System.Text.Json.JsonSerializer.SerializeToElement(r.data).GetProperty("cards")[0].GetProperty("estimatedDiscount").GetDouble();
        Assert.Equal(33.6, Est(await ctl.ListUsableCards("session-7", CoachFixture.StudentA, "养护", "万龙服务中心", "双项", 168, 0, 0, 0)));
        Assert.Equal(33.6, Est(await ctl.ListUsableCards("session-7", CoachFixture.StudentA, "养护", "万龙服务中心", "双项", 228, 0, 0, 1)));
        Assert.Equal(60, Est(await ctl.ListUsableCards("session-7", CoachFixture.StudentA, "养护", "崇礼旗舰店", "双项", 188, 0, 0, 1)));   // 崇礼没有加急商品
    }

    // ---------- 使用记录：每笔只记一次，取消后反向 ----------

    [Fact]
    public async Task Usage_RecordedOnceAndReversedOnCancel()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        Assert.True(await f.Pricing().RecordUseAsync(card, 100, "养护", 501, 0, "rate", 0.8, 168, 33.6, DateTime.Now));
        Assert.False(await f.Pricing().RecordUseAsync(card, 100, "养护", 501, 0, "rate", 0.8, 168, 33.6, DateTime.Now));   // 重复核销 / 支付回调重放
        Assert.Equal(1, await f.Db.coachCardUse.AsNoTracking().CountAsync());
        Assert.Equal(1, await f.Pricing().ReverseUseAsync("养护", 501, 0, "取消养护", DateTime.Now));
        Assert.Equal(0, await f.Pricing().ReverseUseAsync("养护", 501, 0, "取消养护", DateTime.Now));                         // 幂等
        var use = await f.Db.coachCardUse.AsNoTracking().SingleAsync();
        Assert.Equal(CoachCardUse.StatusReversed, use.status);
        Assert.Equal(33.6, use.discount_amount);
        // 卡本身不受影响，还能继续用
        Assert.True(CoachCardRules.IsEffective(await f.Db.coachCard.AsNoTracking().SingleAsync(c => c.id == card.id), CoachFixture.StudentA, DateTime.Now));
    }

    // ---------- 租赁：服务端强制折扣率 ----------

    private static async Task<int> NewRental(CoachFixture f, int memberId, int? cardId, bool manual = false, DateTime? date = null)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "租赁", shop = "万龙服务中心", member_id = memberId, valid = 1, staff_id = 9 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "雪板套餐", valid = 1, staff_id = 9, coach_card_id = cardId, start_date = date ?? DateTime.Now.Date };
        db.rental.Add(rental); await db.SaveChangesAsync();
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = (date ?? DateTime.Now).Date, rent_type = "日场", day_type = "平日", scene = "门市", price = 180, discount = 0, manual = manual });
        await db.SaveChangesAsync();
        return rental.id;
    }
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    [Fact]
    public async Task Rental_DayChargeGetsTheCardRateAndOneUseRecord()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        int rentalId = await NewRental(f, CoachFixture.StudentA, card.id);
        var detail = await Rent(f).SetRentalDetail(rentalId, DateTime.Now.Date, 9);
        Assert.NotNull(detail);
        Assert.Equal(180, detail!.amount);
        var discount = await f.Db.discount.AsNoTracking().SingleAsync(d => d.biz_id == rentalId);
        Assert.Equal(18, discount.amount);                // 180 × (1 − 0.9)
        Assert.Equal("租赁", discount.biz_type);
        Assert.Equal("日租金", discount.sub_biz_type);
        Assert.Equal(detail.id, discount.sub_biz_id);
        Assert.Null(discount.ticket_code);                // 不能带券码，否则会被当成券优惠、被改价逻辑误改
        var use = await f.Db.coachCardUse.AsNoTracking().SingleAsync();
        Assert.Equal((card.id, "租赁", rentalId, detail.id, 18d, 180d), (use.card_id, use.biz_type, use.biz_id, use.sub_biz_id, use.discount_amount, use.base_amount));
        // 同一天再生效：已开始计费，不重复优惠、不重复记录
        Assert.Null(await Rent(f).SetRentalDetail(rentalId, DateTime.Now.Date, 9));
        Assert.Equal(1, await f.Db.discount.AsNoTracking().CountAsync());
        Assert.Equal(1, await f.Db.coachCardUse.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Rental_NotAppliedWhenManualPriceOtherMemberExpiredOrNoCard()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        // 店员手改过价 → 以店员为准，不叠加
        int manual = await NewRental(f, CoachFixture.StudentA, card.id, manual: true);
        Assert.NotNull(await Rent(f).SetRentalDetail(manual, DateTime.Now.Date, 9));
        Assert.Empty(await f.Db.discount.AsNoTracking().Where(d => d.biz_id == manual).ToListAsync());
        // 卡不属于订单会员（客户端伪造 coach_card_id）
        int other = await NewRental(f, CoachFixture.StudentB, card.id);
        Assert.NotNull(await Rent(f).SetRentalDetail(other, DateTime.Now.Date, 9));
        Assert.Empty(await f.Db.discount.AsNoTracking().Where(d => d.biz_id == other).ToListAsync());
        // 没选卡
        int plain = await NewRental(f, CoachFixture.StudentA, null);
        Assert.Equal(180, (await Rent(f).SetRentalDetail(plain, DateTime.Now.Date, 9))!.amount);
        Assert.Empty(await f.Db.discount.AsNoTracking().Where(d => d.biz_id == plain).ToListAsync());
        // 卡已过期（租赁日晚于有效期）
        await f.Db.coachCard.Where(c => c.id == card.id).ExecuteUpdateAsync(s => s.SetProperty(c => c.end_date, (DateTime?)DateTime.Now.AddDays(-1)));
        int expired = await NewRental(f, CoachFixture.StudentA, card.id);
        Assert.NotNull(await Rent(f).SetRentalDetail(expired, DateTime.Now.Date, 9));
        Assert.Empty(await f.Db.discount.AsNoTracking().Where(d => d.biz_id == expired).ToListAsync());
        Assert.Empty(await f.Db.coachCardUse.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Rental_ExistingPresetDiscountWinsWhenLarger_NotStacked()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        int rentalId = await NewRental(f, CoachFixture.StudentA, card.id);
        await f.Db.rentalPricePreset.Where(p => p.rental_id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(p => p.discount, 40d));
        await Rent(f).SetRentalDetail(rentalId, DateTime.Now.Date, 9);
        Assert.Equal(40, (await f.Db.discount.AsNoTracking().SingleAsync(d => d.biz_id == rentalId)).amount);   // 40 > 18，取大，不叠加
        Assert.Empty(await f.Db.coachCardUse.AsNoTracking().ToListAsync());                                      // 这天没用到学员卡，不记归属
    }
}

public class CoachCareOrderFlowTests
{
    private sealed class Http : Microsoft.AspNetCore.Http.IHttpContextAccessor
    { public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; } = new Microsoft.AspNetCore.Http.DefaultHttpContext(); }

    // 草稿养护单（valid=0，开单页保存后的样子）：一件双板、双项
    private static async Task<(int orderId, int careId)> Draft(CoachFixture f, int memberId, int? cardId, string shop = "万龙服务中心",
        string? ticketCode = null, int urgent = 0, double repair = 0)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = shop, member_id = memberId, valid = 0, recepting = 1, staff_id = 9, code = "YH_20261030_001" };
        db.order.Add(order); await db.SaveChangesAsync();
        var care = new Care { order_id = order.id, equipment = "双板", brand = "品牌", scale = "170", need_edge = 1, need_wax = 1, urgent = urgent,
            biz_type = "雪季养护", valid = 0, coach_card_id = cardId, ticket_code = ticketCode, repair_charge = repair };
        db.care.Add(care); await db.SaveChangesAsync();
        return (order.id, care.id);
    }

    private static async Task<ApiResult<SnowmeetApi.Models.Order>> Place(CoachFixture f, int orderId)
    {
        var res = await new SnowmeetApi.Controllers.OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(orderId, "session-1");
        return Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res.Result).Value);
    }

    [Fact]
    public async Task PlaceOrder_PricesWithTheCard_StoresDiscountRow_AndTotalsCorrectly()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, card.id, repair: 30);
        var r = await Place(f, orderId);
        Assert.Equal(0, r.code);
        var care = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == careId);
        Assert.Equal(168, care.common_charge);
        Assert.Equal(33.6, care.discount);                 // 双项 8 折，只对服务费；维修费 30 不打折
        Assert.Equal(card.id, care.coach_card_id);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(CoachCardRules.Money(168 + 30 - 33.6), order.total_amount);
        Assert.Equal(33.6, (await f.Db.discount.AsNoTracking().SingleAsync(d => d.biz_id == careId && d.biz_type == "养护")).amount);
        // 下单不记使用：付款生效后才记
        Assert.Empty(await f.Db.coachCardUse.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PlaceOrder_UrgentAndChongliAndOtherShops()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var (o1, c1) = await Draft(f, CoachFixture.StudentA, card.id, urgent: 1);
        Assert.Equal(0, (await Place(f, o1)).code);
        var urgent = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == c1);
        Assert.Equal(228, urgent.common_charge);           // 双项加急 168 + 加急加价 60
        Assert.Equal(33.6, urgent.discount);               // 只对基础价 168 打 8 折，加急加价不打折
        var (o2, c2) = await Draft(f, CoachFixture.StudentA, card.id, shop: "崇礼旗舰店");
        Assert.Equal(0, (await Place(f, o2)).code);
        var cl = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == c2);
        Assert.Equal((188d, 60d), (cl.common_charge, cl.discount));   // 崇礼双项一口价 128
    }

    [Fact]
    public async Task PlaceOrder_RejectsBadCardsWithABusinessError()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        // 别人的卡
        var (o1, _) = await Draft(f, CoachFixture.StudentB, card.id);
        var r1 = await Place(f, o1);
        Assert.Equal(1, r1.code);
        Assert.Contains("学员卡", r1.message);
        // 同时选了券
        var (o2, _) = await Draft(f, CoachFixture.StudentA, card.id, ticketCode: "123456789");
        var r2 = await Place(f, o2);
        Assert.Equal(1, r2.code);
        Assert.Contains("不能与优惠券", r2.message);
        // 散客
        var (o3, _) = await Draft(f, 0, card.id);
        await f.Db.order.Where(o => o.id == o3).ExecuteUpdateAsync(s => s.SetProperty(o => o.member_id, (int?)null));
        Assert.Equal(1, (await Place(f, o3)).code);
        // 都没有生效成单
        Assert.Equal(0, await f.Db.order.AsNoTracking().CountAsync(o => o.valid == 1));
    }

    [Fact]
    public async Task Effect_RecordsExactlyOneUsePerCare_AndCancelReversesIt()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, card.id);
        Assert.Equal(0, (await Place(f, orderId)).code);
        var careCtl = () => new CareController(f.NewContext(), f.Config, new Http());
        await careCtl().EffectCareOrder(orderId);
        var use = await f.Db.coachCardUse.AsNoTracking().SingleAsync();
        Assert.Equal((card.id, f.CoachId, CoachFixture.StudentA, orderId, "养护", careId, 0), (use.card_id, use.coach_id, use.member_id, use.order_id, use.biz_type, use.biz_id, use.sub_biz_id));
        Assert.Equal((168d, 33.6, "rate", CoachCardUse.StatusEffective), (use.base_amount, use.discount_amount, use.mode, use.status));
        // 支付回调重放 / 重复生效：仍只有一条
        await careCtl().EffectCareOrder(orderId);
        Assert.Equal(1, await f.Db.coachCardUse.AsNoTracking().CountAsync());
        // 取消这一件：使用记录置为已反向，卡本身不动
        int finish = await f.Db.careTask.AsNoTracking().Where(t => t.care_id == careId && t.task_name == "发板").Select(t => t.id).SingleAsync();
        await careCtl().SetTaskStatus(finish, "已完成", "测试取消", "session-1", isCancel: true, cancelReason: "取消本件");
        var reversed = await f.Db.coachCardUse.AsNoTracking().SingleAsync();
        Assert.Equal(CoachCardUse.StatusReversed, reversed.status);
        Assert.Contains("取消养护", reversed.reverse_reason);
        Assert.Equal(CoachCard.StatusClaimed, (await f.Db.coachCard.AsNoTracking().SingleAsync(c => c.id == card.id)).status);
        // 重复取消不出错
        await careCtl().SetTaskStatus(finish, "已完成", "测试取消", "session-1", isCancel: true, cancelReason: "重试");
        Assert.Equal(CoachCardUse.StatusReversed, (await f.Db.coachCardUse.AsNoTracking().SingleAsync()).status);
    }

    [Fact]
    public async Task Effect_WithoutACardRecordsNothing_AndAZeroDiscountCellRecordsNothing()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("崇礼旗舰店", "单项", "rate", 0.8) }, 0.9));   // 万龙双项没设价格
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        var (o1, _) = await Draft(f, CoachFixture.StudentA, null);
        var (o2, c2) = await Draft(f, CoachFixture.StudentA, card.id);
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal(0, (await Place(f, o2)).code);
        Assert.Equal(0, (await f.Db.care.AsNoTracking().SingleAsync(c => c.id == c2)).discount);
        await new CareController(f.NewContext(), f.Config, new Http()).EffectCareOrder(o1);
        await new CareController(f.NewContext(), f.Config, new Http()).EffectCareOrder(o2);
        Assert.Empty(await f.Db.coachCardUse.AsNoTracking().ToListAsync());
    }
}

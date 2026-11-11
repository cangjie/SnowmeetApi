using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 开板刮蜡券（模板 20）怎么用：封板取板后发给顾客（见 CareSealBoardTests），下次来「开板」时抵掉
/// 万龙服务中心「只选刮蜡」的一口价（老会员 80 / 其他 100），抵完这件装备 0 元，券核销。
///
/// 只有这一个场合有东西可抵——别的门店刮蜡本来就不收费，刮蜡跟修刃 / 热蜡一起选时刮蜡也不单独计费——
/// 所以其余场合不允许用，券从装备上摘掉、不核销。其余券卡在单刮蜡一口价下照旧全部摘掉。
/// 万龙服务中心的单项价在夹具里是 120、双项 168，和 80 / 100 对不上，一眼能看出走的是哪条规则。
/// </summary>
public class CareBoardScrapeTicketTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private const string Wanlong = "万龙服务中心";
    private const int Tpl = CarePricingRules.BoardScrapeTicketTemplateId;
    private static CareController Ctl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private static Care C(int edge = 0, int wax = 0, int freeWax = 0, int unwax = 1, int seal = 0) =>
        new() { equipment = "双板", need_edge = edge, need_wax = wax, free_wax = freeWax, need_unwax = unwax, seal_board = seal, biz_type = "雪季养护" };

    // ---------- 夹具：模板、券、订单 ----------

    private static async Task AddTemplate(CoachFixture f)
    {
        f.Db.ticketTemplate.Add(new TicketTemplate { id = Tpl, name = "开板刮蜡券", memo = "封板后，免费刮蜡", type = "养护券", biz_type = "养护",
            miniapp_recept_path = "", sharable = 0, valid = 1 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    /// <summary>给会员发一张开板刮蜡券（走真实发券入口，跟封板取板发出来的一模一样）。</summary>
    private static async Task<Ticket> Issue(CoachFixture f, int memberId = CoachFixture.StudentA, string memo = "测试发券")
    {
        var ticket = await new TicketController(f.NewContext(), f.Config).CreateTicket(Tpl, memberId, 10, memo, "养护", 0, true, DateTime.Now.Date, null);
        Assert.NotNull(ticket);
        return ticket;
    }

    private static async Task Edit(CoachFixture f, string code, Action<Ticket> change)
    {
        var db = f.NewContext();
        var t = await db.ticket.AsTracking().SingleAsync(x => x.code == code);   // 这个上下文默认不跟踪实体
        change(t); await db.SaveChangesAsync();
    }

    private static Task<Ticket> Reload(CoachFixture f, string code) => f.NewContext().ticket.AsNoTracking().SingleAsync(x => x.code == code);

    /// <summary>给会员造一张已付款的订单 → 老会员（一口价 80）。</summary>
    private static async Task MakeOldMember(CoachFixture f, int memberId)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "零售", shop = Wanlong, member_id = memberId, valid = 1, is_test = 0, code = "T_" + Guid.NewGuid().ToString("N")[..10] };
        db.order.Add(order); await db.SaveChangesAsync();
        db.orderPayment.Add(new OrderPayment { order_id = order.id, amount = 100, status = "支付成功", is_debt = 0, valid = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Preview(CoachFixture f, Care care, int? memberId, string shop = Wanlong, bool derive = false)
    {
        var res = await Ctl(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = shop, memberId = memberId, care = care, deriveServices = derive }, "session-7");
        var env = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal(0, env.code);
        return JsonSerializer.SerializeToElement(env.data);
    }

    private static async Task<(int orderId, int careId)> Draft(CoachFixture f, int? memberId, Care care, string shop = Wanlong, int? orderId = null)
    {
        var db = f.NewContext();
        if (orderId == null)
        {
            var order = new SnowmeetApi.Models.Order { type = "养护", shop = shop, member_id = memberId, valid = 0, recepting = 1, staff_id = 9,
                code = "YH_20261109_" + Guid.NewGuid().ToString("N")[..6] };
            db.order.Add(order); await db.SaveChangesAsync(); orderId = order.id;
        }
        care.order_id = orderId; care.brand = "品牌"; care.scale = "170"; care.valid = 0;
        db.care.Add(care); await db.SaveChangesAsync();
        return (orderId.Value, care.id);
    }

    private static async Task<ApiResult<SnowmeetApi.Models.Order>> Place(CoachFixture f, int orderId)
    {
        var res = await new OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(orderId, "session-1");
        return Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(res.Result).Value);
    }

    private static Task<Care> SavedCare(CoachFixture f, int careId) => f.NewContext().care.AsNoTracking().SingleAsync(c => c.id == careId);

    // ================= 纯规则：这张券此刻能不能抵 =================

    private static Ticket T(int template = Tpl, int? member = 5, int valid = 1, int used = 0, int active = 1, DateTime? start = null, DateTime? expire = null) =>
        new() { code = "TK1", template_id = template, member_id = member, valid = valid, used = used, is_active = active, start_date = start, expire_date = expire };
    private static readonly DateTime Now = new(2026, 11, 9, 15, 0, 0);

    [Fact]
    public void CanRedeem_ValidTicketOfTheOwner_OnAWanlongScrapeOnlyCare()
    {
        Assert.True(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(), 5, Now));
        Assert.True(CarePricingRules.CanRedeemBoardTicket("万龙体验中心", C(), T(), 5, Now));      // 体验中心归并到服务中心
        Assert.True(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(expire: Now.Date), 5, Now));   // 到期当天还能用
        Assert.True(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(start: Now.Date), 5, Now));
    }

    [Theory]
    [InlineData("南山")]
    [InlineData("崇礼旗舰店")]
    [InlineData("")]
    public void CanRedeem_False_AtShopsWhereScrapeIsAlreadyFree(string shop)
    {
        Assert.False(CarePricingRules.CanRedeemBoardTicket(shop, C(), T(), 5, Now));
    }

    [Theory]
    [InlineData(1, 0, 0, 1)]   // 修刃 + 刮蜡
    [InlineData(0, 1, 0, 1)]   // 热蜡 + 刮蜡
    [InlineData(1, 1, 0, 1)]   // 双项
    [InlineData(0, 0, 1, 0)]   // 机打蜡
    [InlineData(0, 0, 0, 0)]   // 什么都没选
    public void CanRedeem_False_WhenTheCareIsNotScrapeOnly(int edge, int wax, int freeWax, int unwax)
    {
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(edge, wax, freeWax, unwax), T(), 5, Now));
    }

    [Fact]
    public void CanRedeem_False_ForWarrantyEntertainAndSummerCares()
    {
        foreach (var care in new[] { new Care { need_unwax = 1, warranty = true }, new Care { need_unwax = 1, entertain = true },
                     new Care { need_unwax = 1, summer = "now", biz_type = "非雪季养护" } })
            Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, care, T(), 5, Now));
    }

    [Fact]
    public void CanRedeem_False_ForTicketsThatAreNotUsable()
    {
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), null, 5, Now));
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(template: 16), 5, Now));          // 别的券
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(used: 1), 5, Now));               // 已用
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(valid: 0), 5, Now));              // 作废
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(active: 0), 5, Now));             // 未激活
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(expire: Now.Date.AddDays(-1)), 5, Now));   // 过期
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(start: Now.Date.AddDays(1)), 5, Now));     // 还没到生效日
    }

    [Fact]
    public void CanRedeem_False_WhenTheTicketIsNotTheOrderMembers()
    {
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(member: 5), 6, Now));   // 别人的券
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(member: 5), null, Now)); // 散客单
        Assert.False(CarePricingRules.CanRedeemBoardTicket(Wanlong, C(), T(member: null), 5, Now)); // 没有持有人
    }

    // ================= 纯规则：单刮蜡一口价下的券卡取舍 =================

    [Fact]
    public void Rules_RedeemableBoardTicket_IsKept_ButCardsCoachCardAndDiscountAreStillCleared()
    {
        var care = C(); care.ticket_code = "TK1"; care.use_card = true; care.card_id = 3; care.card_name = "次卡"; care.coach_card_id = 4; care.discount = 20;
        var r = CarePricingRules.ApplyScrapeOnlyBenefitRules(Wanlong, care, T(), 5, Now);
        Assert.True(r.BoardTicketKept); Assert.True(r.BenefitsCleared); Assert.False(r.BoardTicketRemoved);
        Assert.Equal("TK1", care.ticket_code);
        Assert.False(care.use_card); Assert.Null(care.card_id); Assert.Null(care.card_name); Assert.Null(care.coach_card_id);
        Assert.Equal(0, care.discount);
    }

    [Fact]
    public void Rules_RedeemableBoardTicketAlone_ReportsNothingCleared_AndKeepsAManualDiscount()
    {
        var care = C(); care.ticket_code = "TK1"; care.discount = 15;
        var r = CarePricingRules.ApplyScrapeOnlyBenefitRules(Wanlong, care, T(), 5, Now);
        Assert.True(r.BoardTicketKept); Assert.False(r.BenefitsCleared); Assert.False(r.BoardTicketRemoved);
        Assert.Equal("TK1", care.ticket_code); Assert.Equal(15, care.discount);
    }

    [Theory]
    [InlineData("南山", 0, 0)]
    [InlineData(Wanlong, 0, 1)]   // 热蜡 + 刮蜡
    [InlineData(Wanlong, 1, 1)]   // 双项
    public void Rules_BoardTicketThatCannotBeRedeemed_IsRemovedWithoutTouchingTheRest(string shop, int edge, int wax)
    {
        var care = C(edge, wax, unwax: 1); care.ticket_code = "TK1"; care.discount = 15;
        var r = CarePricingRules.ApplyScrapeOnlyBenefitRules(shop, care, T(), 5, Now);
        Assert.True(r.BoardTicketRemoved); Assert.False(r.BoardTicketKept); Assert.False(r.BenefitsCleared);
        Assert.Null(care.ticket_code);
        Assert.Equal(15, care.discount);   // 这张券本来也没带来减免，店员手填的减免不动
    }

    [Fact]
    public void Rules_ABoardTicketOfSomeoneElse_AtWanlongScrapeOnly_IsRemoved_AndNeverTreatedAsRedeemable()
    {
        var care = C(); care.ticket_code = "TK1";
        var r = CarePricingRules.ApplyScrapeOnlyBenefitRules(Wanlong, care, T(member: 5), 6, Now);
        Assert.True(r.BoardTicketRemoved); Assert.False(r.BoardTicketKept);
        Assert.Null(care.ticket_code);
    }

    [Fact]
    public void Rules_OtherTickets_BehaveExactlyAsBefore()
    {
        // 单刮蜡：别的券照旧全部摘掉
        var scrape = C(); scrape.ticket_code = "OLD16";
        var r1 = CarePricingRules.ApplyScrapeOnlyBenefitRules(Wanlong, scrape, T(template: 16), 5, Now);
        Assert.True(r1.BenefitsCleared); Assert.False(r1.BoardTicketRemoved); Assert.False(r1.BoardTicketKept);
        Assert.Null(scrape.ticket_code);
        // 不是单刮蜡：别的券照旧留着
        var mixed = C(wax: 1); mixed.ticket_code = "OLD16";
        var r2 = CarePricingRules.ApplyScrapeOnlyBenefitRules(Wanlong, mixed, T(template: 16), 5, Now);
        Assert.False(r2.BenefitsCleared); Assert.False(r2.BoardTicketRemoved);
        Assert.Equal("OLD16", mixed.ticket_code);
        // 券码在库里查不到（ticket = null）：按原口径摘掉
        var ghost = C(); ghost.ticket_code = "GHOST";
        Assert.True(CarePricingRules.ApplyScrapeOnlyBenefitRules(Wanlong, ghost, null, 5, Now).BenefitsCleared);
        Assert.Null(ghost.ticket_code);
    }

    // ================= 开单页预览 =================

    [Theory]
    [InlineData(true, 80)]
    [InlineData(false, 100)]
    public async Task Preview_ScrapeOnlyWithBoardTicket_ChargesTheFixedPriceAndTheTicketCoversAllOfIt(bool oldMember, double price)
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        if (oldMember) await MakeOldMember(f, CoachFixture.StudentA);
        var ticket = await Issue(f);
        var care = C(); care.ticket_code = ticket.code;
        var json = await Preview(f, care, CoachFixture.StudentA);
        Assert.Equal(price, json.GetProperty("commonCharge").GetDouble());
        Assert.Equal(price, json.GetProperty("ticketDiscount").GetDouble());
        Assert.False(json.GetProperty("benefitsCleared").GetBoolean());
        Assert.False(json.GetProperty("boardTicketRemoved").GetBoolean());
        var returned = json.GetProperty("care");
        Assert.Equal(ticket.code, returned.GetProperty("ticket_code").GetString());
        Assert.Equal(price, returned.GetProperty("common_charge").GetDouble());
        Assert.Equal(price, returned.GetProperty("discount").GetDouble());   // 服务费 − 减免 = 0
    }

    [Fact]
    public async Task Preview_PickingTheTicket_DerivesScrapeOnlyServices()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f);
        // 换券时（deriveServices）先清空服务项再按券推导：开板刮蜡券 = 只刮蜡
        var care = C(edge: 1, wax: 1, unwax: 1); care.ticket_code = ticket.code;
        var json = await Preview(f, care, CoachFixture.StudentA, derive: true);
        var returned = json.GetProperty("care");
        Assert.Equal((0, 0, 1, 0), (returned.GetProperty("need_edge").GetInt32(), returned.GetProperty("need_wax").GetInt32(),
            returned.GetProperty("need_unwax").GetInt32(), returned.GetProperty("seal_board").GetInt32()));
        Assert.Equal(100, json.GetProperty("commonCharge").GetDouble());
        Assert.Equal(100, json.GetProperty("ticketDiscount").GetDouble());
        Assert.Equal(ticket.code, returned.GetProperty("ticket_code").GetString());
    }

    [Fact]
    public async Task Preview_AddingWaxAfterPickingTheTicket_RemovesTheTicketAndReportsIt()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f);
        // 单刮蜡 + 券 → 店员又点上热蜡：不再是单刮蜡，券没东西可抵，摘掉；价格回到热蜡单项 120
        var care = C(wax: 1, unwax: 1); care.ticket_code = ticket.code;
        var json = await Preview(f, care, CoachFixture.StudentA);
        Assert.True(json.GetProperty("boardTicketRemoved").GetBoolean());
        Assert.False(json.GetProperty("benefitsCleared").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("care").GetProperty("ticket_code").ValueKind);
        Assert.Equal(120, json.GetProperty("commonCharge").GetDouble());
        Assert.Equal(0, json.GetProperty("ticketDiscount").GetDouble());
        Assert.Equal(0, (await Reload(f, ticket.code)).used);   // 没被核销
    }

    [Fact]
    public async Task Preview_AtAShopWhereScrapeIsFree_RemovesTheTicketToo()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f);
        var care = C(); care.ticket_code = ticket.code;
        var json = await Preview(f, care, CoachFixture.StudentA, "南山");
        Assert.True(json.GetProperty("boardTicketRemoved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("care").GetProperty("ticket_code").ValueKind);
        Assert.Equal(0, json.GetProperty("commonCharge").GetDouble());   // 这家店刮蜡本来就不收钱
    }

    [Fact]
    public async Task Preview_ATicketOfAnotherMember_IsRemoved_AndChargedAtTheFullFixedPrice()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f, CoachFixture.StudentB);
        var care = C(); care.ticket_code = ticket.code;
        var json = await Preview(f, care, CoachFixture.StudentA);
        Assert.True(json.GetProperty("boardTicketRemoved").GetBoolean());
        Assert.Equal(100, json.GetProperty("commonCharge").GetDouble());
        Assert.Equal(0, json.GetProperty("ticketDiscount").GetDouble());
    }

    [Fact]
    public async Task Preview_AUsedOrExpiredTicket_IsRemoved_NotAppliedAndNotReusedLater()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var used = await Issue(f, memo: "已用"); await Edit(f, used.code, t => t.used = 1);
        var expired = await Issue(f, memo: "过期"); await Edit(f, expired.code, t => t.expire_date = DateTime.Now.Date.AddDays(-1));
        Assert.Equal(1, (await Reload(f, used.code)).used);   // 夹具改的确实落库了，后面的断言才有意义
        foreach (var (label, code) in new[] { ("已用", used.code), ("过期", expired.code) })
        {
            var care = C(); care.ticket_code = code;
            var json = await Preview(f, care, CoachFixture.StudentA);
            Assert.True(json.GetProperty("boardTicketRemoved").GetBoolean(), label);
            Assert.Equal(100, json.GetProperty("commonCharge").GetDouble());
            Assert.Equal(0, json.GetProperty("ticketDiscount").GetDouble());
        }
    }

    [Fact]
    public async Task Preview_BoardTicketWithAPunchCard_KeepsTheTicketAndClearsTheCard()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f);
        var db = f.NewContext();
        var card = new PunchCard { biz_type = "养护", card_name = "测试次卡", member_id = CoachFixture.StudentA, total = 10, punches = 0 };
        db.punchCard.Add(card); await db.SaveChangesAsync();
        var care = C(); care.ticket_code = ticket.code; care.use_card = true; care.card_id = card.id; care.card_name = "测试次卡";
        var json = await Preview(f, care, CoachFixture.StudentA);
        Assert.True(json.GetProperty("benefitsCleared").GetBoolean());
        var returned = json.GetProperty("care");
        Assert.Equal(ticket.code, returned.GetProperty("ticket_code").GetString());
        Assert.False(returned.GetProperty("use_card").GetBoolean());
        Assert.Equal(JsonValueKind.Null, returned.GetProperty("card_id").ValueKind);
        Assert.Equal(100, json.GetProperty("ticketDiscount").GetDouble());
    }

    [Fact]
    public async Task Preview_OtherTicketsAndOrdinaryScrapeOnly_AreUnchanged()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        await MakeOldMember(f, CoachFixture.StudentA);
        var plain = await Preview(f, C(), CoachFixture.StudentA);
        Assert.Equal((80d, 0d, false, false), (plain.GetProperty("commonCharge").GetDouble(), plain.GetProperty("ticketDiscount").GetDouble(),
            plain.GetProperty("benefitsCleared").GetBoolean(), plain.GetProperty("boardTicketRemoved").GetBoolean()));
        var other = C(); other.ticket_code = "OLDCUST16";
        var cleared = await Preview(f, other, CoachFixture.StudentA);
        Assert.True(cleared.GetProperty("benefitsCleared").GetBoolean());
        Assert.False(cleared.GetProperty("boardTicketRemoved").GetBoolean());
        Assert.Equal(80, cleared.GetProperty("commonCharge").GetDouble());
        Assert.Equal(0, cleared.GetProperty("ticketDiscount").GetDouble());
    }

    // ================= 下单 + 生效 + 取消 =================

    [Theory]
    [InlineData(true, 80)]
    [InlineData(false, 100)]
    public async Task PlaceOrder_ScrapeOnlyWithBoardTicket_IsFreeAndTheTicketIsConsumedOnEffect(bool oldMember, double price)
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        if (oldMember) await MakeOldMember(f, CoachFixture.StudentA);
        var ticket = await Issue(f);
        var care = C(); care.ticket_code = ticket.code;
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, care);
        var r = await Place(f, orderId);
        Assert.Equal(0, r.code);
        var saved = await SavedCare(f, careId);
        Assert.Equal(price, saved.common_charge);
        Assert.Equal(price, saved.discount);
        Assert.Equal(ticket.code, saved.ticket_code);
        var order = await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(0, order.total_amount); Assert.Equal(0, order.paying_amount);
        // 用了券的 0 元单要去结算页核验会员本人后才生效：下单这一刻券还没核销
        Assert.Equal(0, (await Reload(f, ticket.code)).used);

        await Ctl(f).EffectCareOrder(orderId);
        var used = await Reload(f, ticket.code);
        Assert.Equal(1, used.used); Assert.NotNull(used.used_time);
        Assert.Equal(("养护", careId), (used.biz_type, used.biz_id));
        // 生效出来的工序就是刮蜡 + 发板，没有修刃、热蜡
        var tasks = await f.NewContext().careTask.AsNoTracking().Where(t => t.care_id == careId).Select(t => t.task_name).ToListAsync();
        Assert.Contains("刮蜡", tasks); Assert.Contains("发板", tasks);
        Assert.DoesNotContain("修刃", tasks); Assert.DoesNotContain("热蜡", tasks);
        // 重复生效（支付回调重放）：还是只核销这一张
        await Ctl(f).EffectCareOrder(orderId);
        Assert.Equal(1, (await Reload(f, ticket.code)).used);
    }

    [Fact]
    public async Task PlaceOrder_CancellingTheCare_GivesTheBoardTicketBack()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f);
        var care = C(); care.ticket_code = ticket.code;
        var (orderId, careId) = await Draft(f, CoachFixture.StudentA, care);
        Assert.Equal(0, (await Place(f, orderId)).code);
        await Ctl(f).EffectCareOrder(orderId);
        Assert.Equal(1, (await Reload(f, ticket.code)).used);
        int finish = await f.NewContext().careTask.AsNoTracking().Where(t => t.care_id == careId && t.task_name == "发板").Select(t => t.id).SingleAsync();
        await Ctl(f).SetTaskStatus(finish, "已完成", "测试取消", "session-1", isCancel: true, cancelReason: "取消本件");
        var back = await Reload(f, ticket.code);
        Assert.Equal(0, back.used); Assert.Null(back.used_time);   // 券退回，顾客还能再用
    }

    [Fact]
    public async Task PlaceOrder_ATicketThatCannotBeRedeemed_IsDropped_SoItIsNeverConsumed()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var mixedTicket = await Issue(f, memo: "热蜡单");
        var otherTicket = await Issue(f, CoachFixture.StudentB, memo: "别人的");
        var usedTicket = await Issue(f, memo: "已用"); await Edit(f, usedTicket.code, t => { t.used = 1; t.biz_type = "养护"; t.biz_id = 777; });
        var nanshanTicket = await Issue(f, memo: "南山");
        var mixed = C(wax: 1, unwax: 1); mixed.ticket_code = mixedTicket.code;
        var other = C(); other.ticket_code = otherTicket.code;
        var usedCare = C(); usedCare.ticket_code = usedTicket.code;
        var nanshan = C(); nanshan.ticket_code = nanshanTicket.code;
        var (o1, c1) = await Draft(f, CoachFixture.StudentA, mixed);
        var (o2, c2) = await Draft(f, CoachFixture.StudentA, other);
        var (o3, c3) = await Draft(f, CoachFixture.StudentA, usedCare);
        var (o4, c4) = await Draft(f, CoachFixture.StudentA, nanshan, "南山");
        foreach (int id in new[] { o1, o2, o3, o4 }) Assert.Equal(0, (await Place(f, id)).code);
        foreach (var (label, id) in new[] { ("热蜡单", c1), ("别人的", c2), ("已用", c3), ("南山", c4) })
            Assert.True((await SavedCare(f, id)).ticket_code == null, label);
        Assert.Equal(120, (await SavedCare(f, c1)).common_charge);   // 按热蜡单项正常收，没有券减免
        Assert.Equal(100, (await SavedCare(f, c2)).common_charge);
        Assert.Equal(100, (await SavedCare(f, c3)).common_charge);
        Assert.Equal(0, (await SavedCare(f, c4)).common_charge);
        // 生效后这些券都没有被动：没用的还是没用，别人的不动，已用的保持原来的核销记录
        foreach (int id in new[] { o1, o2, o3, o4 }) await Ctl(f).EffectCareOrder(id);
        Assert.Equal(0, (await Reload(f, mixedTicket.code)).used);
        Assert.Equal(0, (await Reload(f, otherTicket.code)).used);
        Assert.Equal(0, (await Reload(f, nanshanTicket.code)).used);
        var stillUsed = await Reload(f, usedTicket.code);
        Assert.Equal((1, 777), (stillUsed.used, stillUsed.biz_id));   // 原来那次核销记录没被覆盖
    }

    [Fact]
    public async Task PlaceOrder_OneBoardTicketCannotCoverTwoCaresInTheSameOrder()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var ticket = await Issue(f);
        var first = C(); first.ticket_code = ticket.code;
        var second = C(); second.ticket_code = ticket.code;
        var (orderId, c1) = await Draft(f, CoachFixture.StudentA, first);
        var (_, c2) = await Draft(f, CoachFixture.StudentA, second, orderId: orderId);
        Assert.Equal(0, (await Place(f, orderId)).code);
        Assert.Equal(ticket.code, (await SavedCare(f, c1)).ticket_code);
        Assert.Equal(100, (await SavedCare(f, c1)).discount);
        Assert.Null((await SavedCare(f, c2)).ticket_code);          // 后一件摘掉，按一口价收
        Assert.Equal(0, (await SavedCare(f, c2)).discount);
        Assert.Equal(100, (await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId)).total_amount);
    }

    [Fact]
    public async Task PlaceOrder_TwoDifferentBoardTickets_CoverTwoCaresEach()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        var t1 = await Issue(f, memo: "一"); var t2 = await Issue(f, memo: "二");
        var first = C(); first.ticket_code = t1.code;
        var second = C(); second.ticket_code = t2.code;
        var (orderId, c1) = await Draft(f, CoachFixture.StudentA, first);
        var (_, c2) = await Draft(f, CoachFixture.StudentA, second, orderId: orderId);
        Assert.Equal(0, (await Place(f, orderId)).code);
        Assert.Equal(t1.code, (await SavedCare(f, c1)).ticket_code); Assert.Equal(t2.code, (await SavedCare(f, c2)).ticket_code);
        Assert.Equal(0, (await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId)).total_amount);
    }

    // ================= 全流程：封板 → 取板发券 → 下次开板免费刮蜡 =================

    [Fact]
    public async Task EndToEnd_SealThenPickup_ThenTheTicketPaysForTheNextSeasonsScrape()
    {
        await using var f = await CoachFixture.Create();
        await AddTemplate(f);
        // 1) 万龙服务中心热蜡 + 封板（不刮蜡）下单、生效
        var sealed1 = C(wax: 1, unwax: 0, seal: 1);
        var (o1, c1) = await Draft(f, CoachFixture.StudentA, sealed1);
        Assert.Equal(0, (await Place(f, o1)).code);
        Assert.Equal((1, 0, 1), ((await SavedCare(f, c1)).need_wax, (await SavedCare(f, c1)).need_unwax, (await SavedCare(f, c1)).seal_board));
        await Ctl(f).EffectCareOrder(o1);
        // 2) 取板（发板完成）→ 顾客拿到一张开板刮蜡券
        int finish = await f.NewContext().careTask.AsNoTracking().Where(t => t.care_id == c1 && t.task_name == "发板").Select(t => t.id).SingleAsync();
        await Ctl(f).SetTaskStatus(finish, "已完成", "扫码取板", "session-7");
        var granted = Assert.Single(await f.NewContext().ticket.AsNoTracking().Where(t => t.template_id == Tpl).ToListAsync());
        Assert.Equal(CoachFixture.StudentA, granted.member_id);
        // 3) 下个雪季回来只刮蜡：选这张券 → 0 元，核销
        var next = C(); next.ticket_code = granted.code;
        var preview = await Preview(f, next, CoachFixture.StudentA);
        Assert.Equal(100, preview.GetProperty("commonCharge").GetDouble());
        Assert.Equal(100, preview.GetProperty("ticketDiscount").GetDouble());
        var (o2, c2) = await Draft(f, CoachFixture.StudentA, next);
        Assert.Equal(0, (await Place(f, o2)).code);
        Assert.Equal(0, (await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == o2)).total_amount);
        await Ctl(f).EffectCareOrder(o2);
        var used = await Reload(f, granted.code);
        Assert.Equal((1, c2), (used.used, used.biz_id));
        // 4) 这次是单刮蜡（没封板），取板不会再发开板刮蜡券
        int finish2 = await f.NewContext().careTask.AsNoTracking().Where(t => t.care_id == c2 && t.task_name == "发板").Select(t => t.id).SingleAsync();
        await Ctl(f).SetTaskStatus(finish2, "已完成", "扫码取板", "session-7");
        Assert.Single(await f.NewContext().ticket.AsNoTracking().Where(t => t.template_id == Tpl).ToListAsync());
    }
}

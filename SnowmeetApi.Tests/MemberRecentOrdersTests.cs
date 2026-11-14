using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 会员详情页「最近订单」（2026-11-14 用户要求）：每个订单除了单号、类型、日期，还要带订单金额、减免金额、实际支付金额，
/// 以及用了哪些优惠券、哪些卡（次卡带「记几次、余几次」）。口径和订单自己的计算属性一致：
///   减免 = 有效的 discount 行合计（Order.discountAmount）；支付 = Order.availablePayments 里支付成功的合计（Order.paidAmount，含租赁押金）；
///   退款 = 这些支付上已受理的退款合计（Order.refundAmount：state = 1 或有退款单号），页面上「实付」= 支付 − 退款（2026-11-14：押金不能算实付）；
///   券 = 订单上已使用的券（ticket.order_id，或养护装备的 ticket_code / 券 biz 关联）；
///   卡 = 次卡 / 季卡核销（punch_card_used）和学员卡使用（coach_card_use）；次卡「余几次」= 这一单用完之后还剩几次。
/// 只读，不改任何数据；新增字段，旧版小程序读不到也不受影响。
/// </summary>
public class MemberRecentOrdersTests
{
    private static readonly DateTime Now = DateTime.Now;

    private static MemberAdminController Controller(CoachFixture f) => new(f.NewContext(), f.Config, new HttpContextAccessor());

    private static JsonElement Detail(ActionResult<ApiResult<object>> a)
    {
        var r = (ApiResult<object>)((OkObjectResult)a.Result!).Value!;
        Assert.Equal(0, r.code);
        return JsonSerializer.SerializeToElement(r.data);
    }

    private static Order NewOrder(string type, string code, double total, DateTime date, int memberId = CoachFixture.StudentA, int valid = 1) => new()
    {
        type = type, code = code, shop = "万龙服务中心", member_id = memberId, valid = valid, total_amount = total,
        biz_date = date, create_date = date, pay_option = "普通"
    };

    private static OrderPayment Pay(int orderId, double amount, string method = "微信支付", string status = "支付成功", int valid = 1) => new()
    {
        order_id = orderId, amount = amount, pay_method = method, status = status, valid = valid, create_date = Now
    };

    private static JsonElement Row(JsonElement detail, int orderId) =>
        detail.GetProperty("recentOrders").EnumerateArray().Single(o => o.GetProperty("id").GetInt32() == orderId);

    private sealed record Seeded(int Rent1, int Rent2, int Care, int Retail, int Ski, int Season, int Other, int Cancelled);

    private static async Task<Seeded> Seed(CoachFixture f)
    {
        var db = f.Db;
        var rent1 = NewOrder("租赁", "WT_ZL_1", 0, Now.AddDays(-10)); var rent2 = NewOrder("租赁", "WT_ZL_2", 0, Now.AddDays(-5));
        var care = NewOrder("养护", "WF_YH_1", 230, Now.AddDays(-3));
        var retail = NewOrder("零售", "WT_LS_1", 99, Now.AddDays(-2));
        var ski = NewOrder("雪票", "QJ_XP_1", 309, Now.AddDays(-1));
        var season = NewOrder("养护", "WF_YH_2", 0, Now.AddHours(-5));
        var other = NewOrder("零售", "OTHER_1", 50, Now.AddHours(-4), memberId: CoachFixture.StudentB);
        var cancelled = NewOrder("零售", "CANCEL_1", 10, Now.AddHours(-3), valid: 0);
        db.order.AddRange(rent1, rent2, care, retail, ski, season, other, cancelled);
        await db.SaveChangesAsync();

        // 实付：只算 availablePayments（微信 / 支付宝成功；其它方式要 valid=1）里支付成功的；待支付、失败的不算
        db.orderPayment.AddRange(
            Pay(rent1.id, 5000), Pay(rent1.id, 300, status: "待支付"),
            Pay(rent2.id, 5000), Pay(rent2.id, 40, method: "现金", valid: 0), Pay(rent2.id, 60, method: "现金", valid: 1),
            Pay(care.id, 200), Pay(retail.id, 99, method: "支付宝"), Pay(ski.id, 309));
        await db.SaveChangesAsync();
        // 退款：只算「可用支付」上已受理的（state = 1 或有退款单号）；没受理的、挂在不可用支付上的不算
        var rent2Pays = db.orderPayment.Local.Where(p => p.order_id == rent2.id).ToList();
        int goodPay = rent2Pays.Single(p => p.pay_method == "微信支付" && p.amount == 5000).id;
        int unavailablePay = rent2Pays.Single(p => p.pay_method == "现金" && p.valid == 0).id;
        var carePay = db.orderPayment.Local.Single(p => p.order_id == care.id).id;
        db.Set<OrderPaymentRefund>().AddRange(
            new OrderPaymentRefund { order_id = rent2.id, payment_id = goodPay, amount = 4780, state = 0, refund_id = "R-1" },
            new OrderPaymentRefund { order_id = rent2.id, payment_id = goodPay, amount = 100, state = 1, refund_id = "" },
            new OrderPaymentRefund { order_id = rent2.id, payment_id = goodPay, amount = 999, state = 0, refund_id = "" },
            new OrderPaymentRefund { order_id = rent2.id, payment_id = unavailablePay, amount = 40, state = 1, refund_id = "R-2" },
            new OrderPaymentRefund { order_id = care.id, payment_id = carePay, amount = 20, state = 1, refund_id = "R-3" });
        // 减免：只算 valid=1 的
        db.discount.AddRange(
            new Discount { order_id = care.id, amount = 20, valid = 1 }, new Discount { order_id = care.id, amount = 10, valid = 1 },
            new Discount { order_id = care.id, amount = 99, valid = 0 });

        // 次卡：10 次卡，租赁一单用 1 次、另一单用 2 次（先用的那单 create_date 更早）；季卡用 3 次；已作废的核销不算
        var card = new PunchCard { biz_type = "租赁", card_name = "租赁10次卡", member_id = CoachFixture.StudentA, total = 10, punches = 3 };
        var seasonCard = new PunchCard { biz_type = "养护", card_name = "机打蜡季卡", member_id = CoachFixture.StudentA, total = null, punches = 0 };
        db.punchCard.AddRange(card, seasonCard);
        await db.SaveChangesAsync();
        db.punchCardUsed.AddRange(
            new PunchCardUsed { card_id = card.id, order_id = rent1.id, biz_type = "租赁", biz_id = 1, punch_count = 1, create_date = Now.AddDays(-10) },
            new PunchCardUsed { card_id = card.id, order_id = rent2.id, biz_type = "租赁", biz_id = 2, punch_count = 2, create_date = Now.AddDays(-5) },
            new PunchCardUsed { card_id = card.id, order_id = rent2.id, biz_type = "租赁", biz_id = 3, punch_count = 5, valid = false, create_date = Now.AddDays(-5) },
            new PunchCardUsed { card_id = seasonCard.id, order_id = season.id, biz_type = "养护", biz_id = 4, punch_count = 1, create_date = Now.AddHours(-5) });

        // 券：养护装备的 ticket_code 指向的已使用券 + 学员卡使用；另一张券直接挂 ticket.order_id（雪票单）
        var careRow = new Care { order_id = care.id, equipment = "双板", valid = 1, create_date = Now.AddDays(-3), ticket_code = "T-CARE", task_flow_code = "F" };
        var careRow2 = new Care { order_id = care.id, equipment = "单板", valid = 1, create_date = Now.AddDays(-3), ticket_code = "T-CARE2", task_flow_code = "F" };
        db.care.AddRange(careRow, careRow2);
        db.ticket.AddRange(
            new Ticket { code = "T-CARE", name = "老顾客优惠券", template_id = 16, member_id = CoachFixture.StudentA, open_id = "mini-3", used = 1, create_date = Now.AddDays(-30) },
            new Ticket { code = "T-CARE2", name = "老顾客优惠券", template_id = 16, member_id = CoachFixture.StudentA, open_id = "mini-3", used = 1, create_date = Now.AddDays(-30) },
            new Ticket { code = "T-SKI", name = "免费打蜡券", template_id = 12, member_id = CoachFixture.StudentA, open_id = "mini-3", used = 1, order_id = ski.id, create_date = Now.AddDays(-30) },
            new Ticket { code = "T-UNUSED", name = "没用的券", template_id = 12, member_id = CoachFixture.StudentA, open_id = "mini-3", used = 0, order_id = ski.id, create_date = Now.AddDays(-30) });
        var coachCard = new CoachCard { coach_id = f.CoachId, token = "tok-recent-orders", status = "claimed", member_id = CoachFixture.StudentA };
        db.coachCard.Add(coachCard);
        await db.SaveChangesAsync();
        db.coachCardUse.Add(new CoachCardUse { card_id = coachCard.id, coach_id = f.CoachId, member_id = CoachFixture.StudentA, order_id = care.id, biz_type = "养护", biz_id = careRow.id });
        await db.SaveChangesAsync();
        return new Seeded(rent1.id, rent2.id, care.id, retail.id, ski.id, season.id, other.id, cancelled.id);
    }

    [Fact]
    public async Task OnlyTheMembersValidOrdersAreListedNewestFirst()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        var d = Detail(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7"));
        var ids = d.GetProperty("recentOrders").EnumerateArray().Select(o => o.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(new[] { s.Season, s.Ski, s.Retail, s.Care, s.Rent2, s.Rent1 }, ids);
        Assert.DoesNotContain(s.Other, ids); Assert.DoesNotContain(s.Cancelled, ids);
        var first = d.GetProperty("recentOrders")[0];
        Assert.Equal("WF_YH_2", first.GetProperty("code").GetString()); Assert.Equal("养护", first.GetProperty("type").GetString());
        Assert.True(first.TryGetProperty("bizDate", out _));
    }

    [Fact]
    public async Task AmountsFollowTheOrdersOwnRules_TotalDiscountAndPaid()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        var d = Detail(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7"));

        var care = Row(d, s.Care);
        Assert.Equal(230, care.GetProperty("totalAmount").GetDouble());
        Assert.Equal(30, care.GetProperty("discountAmount").GetDouble());          // 20 + 10，valid=0 的 99 不算
        Assert.Equal(200, care.GetProperty("paidAmount").GetDouble());
        var rent1 = Row(d, s.Rent1);
        Assert.Equal(0, rent1.GetProperty("totalAmount").GetDouble());
        Assert.Equal(0, rent1.GetProperty("discountAmount").GetDouble());
        Assert.Equal(5000, rent1.GetProperty("paidAmount").GetDouble());           // 待支付的 300 不算
        Assert.Equal(5060, Row(d, s.Rent2).GetProperty("paidAmount").GetDouble()); // 现金 valid=0 的 40 不算，valid=1 的 60 算
        Assert.Equal(99, Row(d, s.Retail).GetProperty("paidAmount").GetDouble());  // 支付宝
        // 退款：已受理的才算（4780 有退款单号 + 100 state=1）；没受理的 999、挂在不可用支付（现金 valid=0）上的 40 都不算
        Assert.Equal(4880, Row(d, s.Rent2).GetProperty("refundAmount").GetDouble());
        Assert.Equal(20, care.GetProperty("refundAmount").GetDouble());
        Assert.Equal(0, rent1.GetProperty("refundAmount").GetDouble());
        Assert.Equal(0, Row(d, s.Retail).GetProperty("refundAmount").GetDouble());
        Assert.Equal(309, Row(d, s.Ski).GetProperty("paidAmount").GetDouble());
    }

    [Fact]
    public async Task UsedCouponsAreNamed_FromCareTicketCodesAndOrderLinks_UnusedOnesAreNot()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        var d = Detail(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7"));

        var careTickets = Row(d, s.Care).GetProperty("tickets").EnumerateArray().ToList();
        Assert.Single(careTickets);                                                // 两块板各用一张同名券 → 合并成一条，数量 2
        Assert.Equal("老顾客优惠券", careTickets[0].GetProperty("name").GetString());
        Assert.Equal(2, careTickets[0].GetProperty("count").GetInt32());

        var skiTickets = Row(d, s.Ski).GetProperty("tickets").EnumerateArray().ToList();
        Assert.Single(skiTickets);                                                 // ticket.order_id 关联；used=0 的不算
        Assert.Equal("免费打蜡券", skiTickets[0].GetProperty("name").GetString());
        Assert.Equal(1, skiTickets[0].GetProperty("count").GetInt32());

        Assert.Empty(Row(d, s.Retail).GetProperty("tickets").EnumerateArray());
        Assert.Empty(Row(d, s.Retail).GetProperty("cards").EnumerateArray());
    }

    [Fact]
    public async Task PunchCardsShowUsedAndRemainingAfterThatOrder_SeasonCardsHaveNoRemaining_CoachCardsAreNamed()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        var d = Detail(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7"));

        var c1 = Row(d, s.Rent1).GetProperty("cards").EnumerateArray().Single();   // 先用的一单：记 1 次，当时余 9 次
        Assert.Equal(("租赁10次卡", "punch", 1, 9), (c1.GetProperty("name").GetString(), c1.GetProperty("kind").GetString(), c1.GetProperty("used").GetInt32(), c1.GetProperty("remaining").GetInt32()));
        var c2 = Row(d, s.Rent2).GetProperty("cards").EnumerateArray().Single();   // 后用的一单：记 2 次（作废的 5 次不算），用完余 7 次
        Assert.Equal(("punch", 2, 7), (c2.GetProperty("kind").GetString(), c2.GetProperty("used").GetInt32(), c2.GetProperty("remaining").GetInt32()));

        var seasonCard = Row(d, s.Season).GetProperty("cards").EnumerateArray().Single();
        Assert.Equal(("机打蜡季卡", "season", 1), (seasonCard.GetProperty("name").GetString(), seasonCard.GetProperty("kind").GetString(), seasonCard.GetProperty("used").GetInt32()));
        Assert.Equal(JsonValueKind.Null, seasonCard.GetProperty("remaining").ValueKind);

        var coach = Row(d, s.Care).GetProperty("cards").EnumerateArray().Single();
        Assert.Equal("coach", coach.GetProperty("kind").GetString());
        Assert.Equal("张教练的学员卡", coach.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, coach.GetProperty("remaining").ValueKind);
    }

    [Fact]
    public async Task MemberWithoutOrdersStillWorks_AndTheOtherFieldsOfTheDetailAreUnchanged()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var d = Detail(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentC, "session-7"));
        Assert.Empty(d.GetProperty("recentOrders").EnumerateArray());
        foreach (var key in new[] { "id", "name", "isVip", "phone", "accounts", "punchCards", "depositTotal", "points", "sys", "custom" })
            Assert.True(d.TryGetProperty(key, out _), key);
    }
}

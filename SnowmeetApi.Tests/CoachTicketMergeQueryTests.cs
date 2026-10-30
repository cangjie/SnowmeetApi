using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

public class CoachTicketMergeQueryTests
{
    private static TicketMigrationTests.TestTicketController Tickets(CoachFixture f) => new(f.NewContext(), f.Config);
    private static ApiResult<Ticket> R(ActionResult<ApiResult<Ticket>> a) => TicketMigrationTests.Result(a);

    private static async Task<string> GrantOne(CoachFixture f, int coachId, int templateId, string request = "g1")
    {
        Staff op = await f.Db.staff.AsNoTracking().FirstAsync(s => s.id == 9);
        await f.Admin_().GrantCouponsAsync(op, new() { coachId }, templateId, 1, request);
        return await f.Db.coachCouponGrant.AsNoTracking().Where(g => g.grant_key.StartsWith(request + "-")).Select(g => g.ticket_code).FirstAsync();
    }

    // ---------- 教练券转发 ----------

    [Fact]
    public async Task CoachGrantedTicket_CanBeForwardedEvenWhenTheTemplateIsNotTransferable_AndNoReward()
    {
        await using var f = await CoachFixture.Create();
        string code = await GrantOne(f, f.CoachId, 99);   // 模板 99：不在 12/16 里、sharable=0
        Assert.Equal(0, R(await Tickets(f).SetTicketToMiniShare(code, "session-2")).code);
        Assert.Equal(0, R(await Tickets(f).AcceptMiniTicket(code, "", "session-3")).code);
        Assert.Equal(CoachFixture.StudentA, (await f.Db.ticket.AsNoTracking().SingleAsync(t => t.code == code)).member_id);
        Assert.Equal(0, await f.Db.ticket.AsNoTracking().CountAsync(t => t.create_memo == "转赠被领取回赠"));
        // 一张券只能被一个人接受
        Assert.Equal(1, R(await Tickets(f).AcceptMiniTicket(code, "", "session-4")).code);
    }

    [Fact]
    public async Task CoachForwardingTemplate16_DoesNotRewardTheCoach()
    {
        await using var f = await CoachFixture.Create();
        string code = await GrantOne(f, f.CoachId, 16);
        Assert.Equal(0, R(await Tickets(f).SetTicketToMiniShare(code, "session-2")).code);
        Assert.Equal(0, R(await Tickets(f).AcceptMiniTicket(code, "", "session-3")).code);
        Assert.Equal(0, await f.Db.ticket.AsNoTracking().CountAsync(t => t.create_memo == "转赠被领取回赠"));
        // 对照：普通顾客转赠模板 16 仍然回赠（规则不变）
        f.Db.ticket.Add(new Ticket { code = "555555555", name = "老顾客券", open_id = "mini-4", member_id = CoachFixture.StudentB, template_id = 16,
            valid = 1, used = 0, is_active = 1, expire_date = DateTime.Now.AddDays(30), start_date = DateTime.Now.AddDays(-1), create_date = DateTime.Now });
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, R(await Tickets(f).SetTicketToMiniShare("555555555", "session-4")).code);
        Assert.Equal(0, R(await Tickets(f).AcceptMiniTicket("555555555", "", "session-5")).code);
        Assert.Equal(1, await f.Db.ticket.AsNoTracking().CountAsync(t => t.create_memo == "转赠被领取回赠" && t.member_id == CoachFixture.StudentB));
    }

    [Fact]
    public async Task OrdinaryHolderStillCannotTransferANonWhitelistedTemplate()
    {
        await using var f = await CoachFixture.Create();
        f.Db.ticket.Add(new Ticket { code = "444444444", name = "教练专属券", open_id = "mini-3", member_id = CoachFixture.StudentA, template_id = 99,
            valid = 1, used = 0, is_active = 1, expire_date = DateTime.Now.AddDays(30), start_date = DateTime.Now.AddDays(-1), create_date = DateTime.Now });
        await f.Db.SaveChangesAsync();
        Assert.Equal(1, R(await Tickets(f).SetTicketToMiniShare("444444444", "session-3")).code);
    }

    [Fact]
    public async Task CoachCouponsOverview_ShowsForwardedAndAcceptedWithStudentTail()
    {
        await using var f = await CoachFixture.Create();
        Staff op = await f.Db.staff.AsNoTracking().FirstAsync(s => s.id == 9);
        await f.Admin_().GrantCouponsAsync(op, new() { f.CoachId }, 99, 3, "ov");
        var codes = await f.Db.coachCouponGrant.AsNoTracking().OrderBy(g => g.id).Select(g => g.ticket_code).ToListAsync();
        await Tickets(f).SetTicketToMiniShare(codes[0], "session-2"); await Tickets(f).AcceptMiniTicket(codes[0], "", "session-3");   // 已被接受
        await Tickets(f).SetTicketToMiniShare(codes[1], "session-2");                                                                    // 已转出待接受
        // codes[2] 在手
        var overview = JsonSerializer.SerializeToElement(await f.Query().GetCouponsAsync(f.CoachId, DateTime.Now));
        var summary = overview.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("granted").GetInt32());
        Assert.Equal(1, summary.GetProperty("held").GetInt32());
        Assert.Equal(1, summary.GetProperty("pending").GetInt32());
        Assert.Equal(1, summary.GetProperty("accepted").GetInt32());
        var accepted = overview.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("state").GetString() == "accepted");
        Assert.Equal("学员A", accepted.GetProperty("acceptedName").GetString());
        Assert.Equal("1003", accepted.GetProperty("acceptedPhoneTail").GetString());
        // 控制器：教练看自己的券，学员看不到教练接口
        var ctl = new CoachController(f.NewContext(), f.Config);
        Assert.Equal(0, (await ctl.GetMyCoupons("session-2")).code);
        Assert.Equal(3, (await ctl.GetMyCoupons("session-3")).code);
    }

    // ---------- 学员与消费 ----------

    private static async Task<int> PaidOrder(CoachFixture f, int memberId, string type, double paid, DateTime date, double refund = 0, double deposit = 0, string? careEquip = null)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = type, shop = "万龙服务中心", member_id = memberId, valid = 1, biz_date = date, total_amount = paid };
        db.order.Add(order); await db.SaveChangesAsync();
        var pay = new OrderPayment { order_id = order.id, amount = paid, pay_method = "微信支付", status = "支付成功", valid = 1 };
        db.orderPayment.Add(pay); await db.SaveChangesAsync();
        if (refund > 0) { db.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = order.id, payment_id = pay.id, amount = refund, state = 1, refund_id = "r-" + order.id }); await db.SaveChangesAsync(); }
        if (deposit > 0)
        {
            var g = new Guaranty { order_id = order.id, amount = deposit, valid = 1, biz_type = "租赁" };
            db.guaranty.Add(g); await db.SaveChangesAsync();
            db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = g.id, payment_id = pay.id }); await db.SaveChangesAsync();
        }
        if (careEquip != null) { db.care.Add(new Care { order_id = order.id, equipment = careEquip, need_edge = 1, need_wax = 1, valid = 1 }); await db.SaveChangesAsync(); }
        return order.id;
    }

    [Fact]
    public async Task Students_AreCardClaimersAndCouponAcceptersWithNetConsumptionFromTheirStartDate()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);                 // 学员A：领卡
        string code = await GrantOne(f, f.CoachId, 16);
        await Tickets(f).SetTicketToMiniShare(code, "session-2"); await Tickets(f).AcceptMiniTicket(code, "", "session-4");   // 学员B：接券
        DateTime claim = card.claimed_at!.Value;
        // 学员A：领卡前的订单不算；领卡后：养护 134.4（用了学员卡）、租赁 986（含押金 500，未退）、零售 198 部分退 98
        await PaidOrder(f, CoachFixture.StudentA, "养护", 999, claim.AddDays(-10));
        int care = await PaidOrder(f, CoachFixture.StudentA, "养护", 134.4, claim.AddMinutes(5), careEquip: "双板");
        await PaidOrder(f, CoachFixture.StudentA, "租赁", 986, claim.AddMinutes(10), deposit: 500);
        await PaidOrder(f, CoachFixture.StudentA, "零售", 198, claim.AddMinutes(20), refund: 98);
        await f.Pricing().RecordUseAsync(card, care, "养护", 1, 0, "rate", 0.8, 168, 33.6, DateTime.Now);
        // 别人（非学员）的订单不出现；学员B 在接券后下了一单
        await PaidOrder(f, CoachFixture.StudentC, "养护", 500, claim.AddMinutes(5));
        await PaidOrder(f, CoachFixture.StudentB, "养护", 70, DateTime.Now.AddMinutes(1));

        var students = await f.Query().GetStudentsAsync(f.CoachId);
        Assert.Equal(new[] { CoachFixture.StudentA, CoachFixture.StudentB }.OrderBy(x => x), students.Select(s => s.memberId).OrderBy(x => x));
        var a = students.Single(s => s.memberId == CoachFixture.StudentA);
        Assert.Equal(new[] { "card" }, a.sources);
        Assert.Equal(3, a.orderCount);
        Assert.Equal(CoachCardRules.Money(134.4 + 486 + 100), a.amount);   // 押金不算消费；零售 198−98
        Assert.Equal(33.6, a.cardDiscount);
        Assert.Equal("学员A", a.name);
        Assert.Equal("1003", a.phoneTail);
        var b = students.Single(s => s.memberId == CoachFixture.StudentB);
        Assert.Equal(new[] { "coupon" }, b.sources);
        Assert.Equal(70, b.amount);

        var (student, orders) = await f.Query().GetStudentOrdersAsync(f.CoachId, CoachFixture.StudentA);
        Assert.Equal(3, orders.Count);
        Assert.Single(orders, o => o.usedCoachCard && o.cardDiscount == 33.6 && o.type == "养护" && o.itemName.Contains("双项"));
        await Assert.ThrowsAsync<CoachException>(() => f.Query().GetStudentOrdersAsync(f.CoachId, CoachFixture.StudentC));   // 不是我的学员

        // 控制器：汇总
        var ctl = new CoachController(f.NewContext(), f.Config);
        var res = await ctl.GetStudents("session-2");
        var json = JsonSerializer.SerializeToElement(res.data);
        Assert.Equal(2, json.GetProperty("summary").GetProperty("students").GetInt32());
        Assert.Equal(CoachCardRules.Money(134.4 + 486 + 100 + 70), json.GetProperty("summary").GetProperty("amount").GetDouble());
    }

    [Fact]
    public void NetConsumption_DepositAlreadyRefundedIsNotSubtractedTwice()
    {
        var order = new SnowmeetApi.Models.Order { type = "租赁" };
        var pay = new OrderPayment { amount = 986, pay_method = "微信支付", status = "支付成功", valid = 1,
            refunds = new() { new OrderPaymentRefund { amount = 500, state = 1, refund_id = "x" } } };
        order.payments = new() { pay };
        order.guarantys = new() { new Guaranty { amount = 500, valid = 1, guarantyPayments = new() { new GuarantyPayment { payment = pay } } } };
        Assert.Equal(486, CoachQueryService.NetConsumption(order));   // 押金已退：986 − 500 退款 = 486，不再扣押金
        order.payments[0].refunds = new();
        Assert.Equal(486, CoachQueryService.NetConsumption(order));   // 押金未退：986 − 500 押金 = 486
    }

    // ---------- 会员合并 ----------

    [Fact]
    public async Task MergeMember_MovesCoachCardsAndUses_DuplicateSeasonCardIsMerged()
    {
        await using var f = await CoachFixture.Create();
        // 来源学员A 与目标学员B 各领了张教练的卡 → 合并后只留目标的，来源的作废
        CoachCard a = await f.IssueAndClaim(CoachFixture.StudentA);
        CoachCard b = await f.IssueAndClaim(CoachFixture.StudentB);
        // 来源学员A 另有李教练的卡（目标没有）→ 迁移
        await f.Admin_().SetPricingAsync(f.Coach2Id, CoachFixture.Standard());
        CoachCard other = await f.IssueAndClaim(CoachFixture.StudentA, f.Coach2Id);
        await f.Pricing().RecordUseAsync(a, 1, "养护", 11, 0, "rate", 0.8, 168, 33.6, DateTime.Now);
        await new MemberController(f.NewContext(), f.Config).MergeMember(CoachFixture.StudentA, CoachFixture.StudentB);
        var cards = await f.Db.coachCard.AsNoTracking().ToListAsync();
        Assert.Equal(CoachCard.StatusMerged, cards.Single(c => c.id == a.id).status);
        Assert.Equal(CoachFixture.StudentA, cards.Single(c => c.id == a.id).member_id);       // 作废的不迁移
        Assert.Equal(CoachFixture.StudentB, cards.Single(c => c.id == b.id).member_id);
        Assert.Equal(CoachFixture.StudentB, cards.Single(c => c.id == other.id).member_id);   // 迁移到目标
        Assert.Equal(CoachCard.StatusClaimed, cards.Single(c => c.id == other.id).status);
        Assert.Equal(CoachFixture.StudentB, (await f.Db.coachCardUse.AsNoTracking().SingleAsync()).member_id);
    }

    [Fact]
    public async Task MergeMember_CoachIdentityMovesToTarget_ButTwoCoachesCannotMerge()
    {
        await using var f = await CoachFixture.Create();
        var helper = () => new MemberController(f.NewContext(), f.Config);
        Assert.Contains("都是教练", (await helper().MergeBlockReasonAsync(CoachFixture.CoachMember, CoachFixture.CoachMember2))!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper().MergeMember(CoachFixture.CoachMember, CoachFixture.CoachMember2));
        Assert.Equal(CoachFixture.CoachMember, (await f.Db.coach.AsNoTracking().SingleAsync(c => c.id == f.CoachId)).member_id);   // 没有被改
        Assert.Null(await helper().MergeBlockReasonAsync(CoachFixture.CoachMember, CoachFixture.StudentC));
        await helper().MergeMember(CoachFixture.CoachMember, CoachFixture.StudentC);
        Assert.Equal(CoachFixture.StudentC, (await f.Db.coach.AsNoTracking().SingleAsync(c => c.id == f.CoachId)).member_id);
    }
}

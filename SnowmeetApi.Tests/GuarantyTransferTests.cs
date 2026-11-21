using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Rent;

namespace SnowmeetApi.Tests;

public class GuarantyTransferTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config, Http());
    private static OrderController Orders(CoachFixture f) => new(f.NewContext(), f.Config, Http());
    private static ApiResult<Order> Data(ActionResult<ApiResult<Order>> r) => (ApiResult<Order>)((OkObjectResult)r.Result!).Value!;

    private static async Task<(int orderId, int sourceId, int draftId, int paymentId)> Seed(CoachFixture f, double deposit = 5000, double charge = 0, double refund = 0)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1, dealed = 1, staff_id = 9 };
        db.order.Add(order); await db.SaveChangesAsync();
        var oldRental = new Rental { order_id = order.id, name = "旧雪板", valid = 1, settled = 1,
            guaranty = deposit, guaranty_discount = 0, start_date = DateTime.Now };
        var newRental = new Rental { order_id = order.id, name = "FIS", valid = 1, appending = true,
            guaranty = 5000, guaranty_discount = 0, start_date = DateTime.Now };
        db.rental.AddRange(oldRental, newRental); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = newRental.id, class_name = "FIS", name = "FIS", noCode = true, valid = 1, atOnce = false });
        if (charge > 0) db.rentalDetail.Add(new RentalDetail { rental_id = oldRental.id, rental_date = DateTime.Now.Date, amount = charge, valid = 1, charge_type = "租金" });
        var pay = new OrderPayment { order_id = order.id, amount = deposit, valid = 1, status = "支付成功", pay_method = "现金", out_trade_no = "TEST" };
        var guarantee = new Guaranty { order_id = order.id, biz_type = "租赁", biz_id = oldRental.id, guaranty_type = "在线支付", amount = deposit, valid = 1, relieve = 1 };
        db.orderPayment.Add(pay); db.guaranty.Add(guarantee); await db.SaveChangesAsync();
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = guarantee.id, payment_id = pay.id });
        if (refund > 0) db.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = order.id, payment_id = pay.id, amount = refund, state = 1, refund_id = "manual" });
        await db.SaveChangesAsync();
        return (order.id, guarantee.id, newRental.id, pay.id);
    }

    private static async Task<ApiResult<Order>> Commit(CoachFixture f, int orderId, double target)
    {
        var order = await Orders(f).GetOrder(orderId);
        var draft = order.appendingRentals.Single();
        draft.guaranty = target;
        return Data(await Rent(f).SaveAppendings(orderId, new List<Rental> { draft }, "session-1", commit: true));
    }

    [Fact]
    public async Task FullOffsetReusesPaidDepositAndActivatesWithoutNewPayment()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, draftId, paymentId) = await Seed(f);
        var result = await Commit(f, orderId, 5000);
        Assert.Equal(0, result.code);
        var db = f.NewContext();
        Assert.Equal(0, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        var transfer = await db.guarantyTransfer.AsNoTracking().SingleAsync();
        Assert.Equal(5000, transfer.amount);
        Assert.True(await db.guarantyPayment.AnyAsync(gp => gp.guaranty_id == transfer.target_guaranty_id && gp.payment_id == paymentId));
        var rental = await db.rental.AsNoTracking().SingleAsync(r => r.id == draftId);
        Assert.NotNull(rental.append_commit_time);
        Assert.True((await db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).paying_amount is null or 0);
        var order = await Orders(f).GetOrder(orderId);
        Assert.Equal(5000, order.totalGuarantyAmount);
    }

    [Fact]
    public async Task ChargesAndRefundsReduceOffsetAndCancellationRestoresSource()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, draftId, _) = await Seed(f, charge: 1000, refund: 1000);
        var result = await Commit(f, orderId, 5000);
        Assert.Equal(0, result.code);
        var db = f.NewContext();
        Assert.Equal(2000, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        Assert.Equal(3000, (await db.guarantyTransfer.AsNoTracking().SingleAsync()).amount);
        Assert.Equal(2000, (await db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).paying_amount);
        var deleted = await Rent(f).RemoveAppendingRental(draftId, "session-1");
        Assert.Equal(0, Data(deleted).code);
        Assert.Equal(5000, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        Assert.Equal(0, (await db.guarantyTransfer.AsNoTracking().SingleAsync()).valid);
        Assert.Empty(await db.guaranty.AsNoTracking().Where(g => g.biz_id == draftId && g.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task NoReleasedDepositDoesNotOffsetAndPendingRefundIsCounted()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, _, _) = await Seed(f);
        var db = f.NewContext();
        var source = await db.guaranty.SingleAsync(g => g.id == sourceId);
        source.relieve = 0; db.Entry(source).State = EntityState.Modified; await db.SaveChangesAsync();
        var order = await Orders(f).GetOrder(orderId);
        var quote = await new GuarantyTransferService(f.NewContext()).CalculateAsync(order, order.appendingRentals);
        Assert.Equal(0, quote.offset);
        Assert.Equal(5000, quote.payable);
    }
    [Fact]
    public async Task OffsetPreviewChangingRejectsCommitWithoutChangingMoney()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, draftId, _) = await Seed(f);
        var order = await Orders(f).GetOrder(orderId);
        var result = await Rent(f).SaveAppendings(orderId, order.appendingRentals, "session-1", commit: true, expectedOffset: 3000);
        Assert.Equal(1, Data(result).code);
        var db = f.NewContext();
        Assert.Equal(5000, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        Assert.True((await db.rental.AsNoTracking().SingleAsync(r => r.id == draftId)).appending);
        Assert.Empty(await db.guarantyTransfer.ToListAsync());
    }

    [Fact]
    public async Task PendingOrPaidAdditionalPaymentPreventsUndoUntilPaymentIsResolved()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, draftId, _) = await Seed(f);
        Assert.Equal(0, (await Commit(f, orderId, 6000)).code);
        var db = f.NewContext();
        db.orderPayment.Add(new OrderPayment { order_id = orderId, amount = 1000, valid = 1, status = "待支付", pay_method = "微信支付", create_date = DateTime.Now.AddSeconds(1) });
        await db.SaveChangesAsync();
        var result = await Rent(f).RemoveAppendingRental(draftId, "session-1");
        Assert.Equal(1, Data(result).code);
        Assert.Equal(0, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        Assert.Equal(1, (await db.guarantyTransfer.AsNoTracking().SingleAsync()).valid);
    }

    [Fact]
    public async Task RefundCannotUseDepositAlreadyTransferredToUnreturnedRental()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _, _, paymentId) = await Seed(f);
        Assert.Equal(0, (await Commit(f, orderId, 6000)).code);
        var controller = Orders(f);
        var order = await controller.GetOrder(orderId);
        var refund = await controller.RefundCore(order, null, new List<OrderPaymentRefund> { new() { payment_id = paymentId, amount = 1 } });
        Assert.Equal(1, refund.code);
        Assert.Empty(await f.Db.orderPaymentRefund.AsNoTracking().Where(r => r.order_id == orderId).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentConfirmationsAndLateDraftSaveDoNotTransferTwice()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, _, _) = await Seed(f);
        var one = (await Orders(f).GetOrder(orderId)).appendingRentals;
        var two = (await Orders(f).GetOrder(orderId)).appendingRentals;
        var results = await Task.WhenAll(Rent(f).SaveAppendings(orderId, one, "session-1", commit: true),
            Rent(f).SaveAppendings(orderId, two, "session-1", commit: true));
        Assert.Equal(new[] { 0, 1 }, results.Select(x => Data(x).code).OrderBy(x => x));
        var stale = await Rent(f).SaveAppendings(orderId, two, "session-1", commit: false);
        Assert.Equal(1, Data(stale).code);
        var db = f.NewContext();
        Assert.Equal(0, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        Assert.Single(await db.guarantyTransfer.AsNoTracking().Where(t => t.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task SupplementPaymentLinksOnlyUnpaidRemainderAndActivatesRental()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _, draftId, _) = await Seed(f);
        Assert.Equal(0, (await Commit(f, orderId, 6000)).code);
        var db = f.NewContext();
        var item = await db.rentItem.SingleAsync(i => i.rental_id == draftId);
        item.atOnce = true; db.Entry(item).State = EntityState.Modified;
        var extra = new OrderPayment { order_id = orderId, amount = 1000, valid = 1, status = "支付成功", pay_method = "现金" };
        db.orderPayment.Add(extra); await db.SaveChangesAsync();
        await Rent(f).EffectRentOrder(orderId, extra.id);
        await Rent(f).EffectRentOrder(orderId, extra.id);
        Assert.Single(await db.rentItemLog.AsNoTracking().Where(l => l.rent_item_id == item.id && l.status == "已发放").ToListAsync());
        var targets = await db.guaranty.AsNoTracking().Where(g => g.biz_id == draftId && g.valid == 1)
            .Include(g => g.guarantyPayments).ThenInclude(gp => gp.payment).OrderBy(g => g.amount).ToListAsync();
        Assert.Equal(new double?[] { 1000, 5000 }, targets.Select(g => g.amount));
        Assert.All(targets, g => Assert.Equal("支付完成", g.payStatus));
        Assert.Single(targets[0].guarantyPayments);
        Assert.Equal(extra.id, targets[0].guarantyPayments[0].payment_id);
        Assert.NotNull((await db.rental.AsNoTracking().SingleAsync(r => r.id == draftId)).append_commit_time);
    }
    [Fact]
    public async Task ReturnedTransferredDepositCanBeRefundedOnce()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _, draftId, paymentId) = await Seed(f);
        Assert.Equal(0, (await Commit(f, orderId, 5000)).code);
        var db = f.NewContext();
        var target = await db.guaranty.SingleAsync(g => g.biz_id == draftId && g.valid == 1);
        target.relieve = 1; db.Entry(target).State = EntityState.Modified;
        var rental = await db.rental.SingleAsync(r => r.id == draftId);
        rental.settled = 1; db.Entry(rental).State = EntityState.Modified;
        await db.SaveChangesAsync();
        var controller = Orders(f);
        var refund = await controller.RefundCore(await controller.GetOrder(orderId), null,
            new List<OrderPaymentRefund> { new() { payment_id = paymentId, amount = 5000 } });
        Assert.Equal(0, refund.code);
        var repeated = await Orders(f).RefundCore(await Orders(f).GetOrder(orderId), null,
            new List<OrderPaymentRefund> { new() { payment_id = paymentId, amount = 1 } });
        Assert.Equal(1, repeated.code);
        Assert.Single(await db.orderPaymentRefund.AsNoTracking().Where(r => r.order_id == orderId).ToListAsync());
    }

    [Fact]
    public async Task MultipleAppendedRentalsShareTheSameReleasedPoolOnlyOnce()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, sourceId, draftId, _) = await Seed(f);
        var db = f.NewContext();
        var second = new Rental { order_id = orderId, name = "雪鞋", valid = 1, appending = true,
            guaranty = 3000, guaranty_discount = 0, start_date = DateTime.Now };
        db.rental.Add(second); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = second.id, class_name = "雪鞋", noCode = true, valid = 1, atOnce = false });
        await db.SaveChangesAsync();
        var order = await Orders(f).GetOrder(orderId);
        order.appendingRentals.Single(r => r.id == draftId).guaranty = 3000;
        var result = await Rent(f).SaveAppendings(orderId, order.appendingRentals, "session-1", commit: true);
        Assert.Equal(0, Data(result).code);
        var transfers = await db.guarantyTransfer.AsNoTracking().Where(t => t.order_id == orderId && t.valid == 1).ToListAsync();
        Assert.Equal(5000, transfers.Sum(t => t.amount));
        Assert.Equal(0, (await db.guaranty.AsNoTracking().SingleAsync(g => g.id == sourceId)).amount);
        Assert.Equal(1000, (await db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).paying_amount);
        Assert.Equal(1, await db.rental.AsNoTracking().CountAsync(r => (r.id == draftId || r.id == second.id) && r.append_commit_time != null));
    }
    [Fact]
    public async Task DirectPaymentRefundEndpointAlsoProtectsTransferredDeposit()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _, _, paymentId) = await Seed(f);
        Assert.Equal(0, (await Commit(f, orderId, 6000)).code);
        var db = f.NewContext();
        var payment = await db.orderPayment.AsNoTracking().SingleAsync(p => p.id == paymentId);
        var controller = new SnowmeetApi.Controllers.Order.OrderPaymentController(f.NewContext(), f.Config, Http());
        var refund = await controller.RefundCore(payment, 1, "租赁押金", 9, "");
        Assert.Null(refund);
        Assert.Empty(await db.orderPaymentRefund.AsNoTracking().Where(r => r.order_id == orderId).ToListAsync());
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Rent;

public sealed class GuarantyTransferService
{
    private readonly ApplicationDBContext db;
    public GuarantyTransferService(ApplicationDBContext db) => this.db = db;

    public sealed class Quote
    {
        public double deposit { get; set; }
        public double refundable { get; set; }
        public double offset { get; set; }
        public double payable { get; set; }
    }

    private static long Cents(double value) => double.IsFinite(value)
        ? checked((long)Math.Round(value * 100, MidpointRounding.AwayFromZero)) : 0;
    private static double Money(long cents) => cents / 100.0;
    private static bool IsPaid(Guaranty g) => g.valid == 1 && (g.guaranty_type ?? "").Trim() == "在线支付"
        && g.guarantyPayments != null && g.guarantyPayments.Count > 0
        && g.guarantyPayments.All(gp => gp.payment != null && (gp.payment.status ?? "").Trim() == "支付成功");

    // 可转用金额按实际押金、已解除占用、租赁费用、已申请退款和仍占用的押金共同封顶。
    public async Task<Quote> CalculateAsync(Order order, IEnumerable<Rental> drafts)
    {
        var gs = (order.guarantys ?? new List<Guaranty>()).Where(IsPaid).ToList();
        long paid = gs.Sum(g => Cents(g.amount ?? 0));
        long cashPaid = (order.availablePayments ?? new List<OrderPayment>())
            .Where(p => (p.status ?? "").Trim() == "支付成功" && (p.pay_method ?? "").Trim() != "储值支付")
            .Sum(p => Cents(p.amount));
        long released = gs.Where(g => g.relieve == 1).Sum(g => Cents(g.amount ?? 0));
        long locked = gs.Where(g => g.relieve != 1).Sum(g => Cents(g.amount ?? 0));
        long requestedRefund = Cents(await db.orderPaymentRefund.AsNoTracking().Where(r => r.order_id == order.id)
            .Where(SnowmeetApi.Helpers.RefundRequestRules.CountsAsRequested).SumAsync(r => (double?)r.amount) ?? 0);
        long charges = Math.Max(0, Cents(order.totalRentSummaryAmount ?? 0));
        long storedRent = Math.Max(0, Cents(order.depositPaidAmount));
        long cardPurchase = (order.retails ?? new List<Retail>())
            .Where(r => r.valid == 1 && r.product_id != null && !(r.memo ?? "").Contains("补差价"))
            .Sum(r => Cents(r.deal_price));
        long free = Math.Max(0, Math.Min(released, Math.Min(paid, cashPaid) - locked - requestedRefund - charges + storedRent - cardPurchase));
        long required = (drafts ?? Enumerable.Empty<Rental>()).Where(r => r != null && !r.noGuaranty)
            .Sum(r => Math.Max(0, Cents((r.guaranty ?? 0) - (r.guaranty_discount ?? 0))));
        long used = Math.Min(free, required);
        return new Quote { deposit = Money(required), refundable = Money(free), offset = Money(used), payable = Money(required - used) };
    }

    // 调用者须在订单级互斥与数据库事务中执行。
    public async Task ApplyAsync(Order order, IList<Rental> targets, int staffId)
    {
        var quote = await CalculateAsync(order, targets);
        long remaining = Cents(quote.offset);
        if (remaining <= 0) return;
        var sourceIds = (order.guarantys ?? new List<Guaranty>()).Where(g => IsPaid(g) && g.relieve == 1 && (g.amount ?? 0) > 0)
            .OrderBy(g => g.id).Select(g => g.id).ToList();
        var sources = await db.guaranty.AsTracking().Include(g => g.guarantyPayments).ThenInclude(gp => gp.payment)
            .Where(g => sourceIds.Contains(g.id)).OrderBy(g => g.id).ToListAsync();
        foreach (var target in targets)
        {
            if (remaining <= 0) break;
            var unpaid = await db.guaranty.AsTracking().Include(g => g.guarantyPayments).ThenInclude(gp => gp.payment)
                .Where(g => g.order_id == order.id && g.biz_type == "租赁" && g.biz_id == target.id && g.valid == 1)
                .OrderBy(g => g.id).ToListAsync();
            foreach (var needed in unpaid.Where(g => g.payStatus == "未支付"))
            {
                long neededCents = Cents(needed.amount ?? 0);
                foreach (var source in sources)
                {
                    if (remaining <= 0 || neededCents <= 0) break;
                    long sourceCents = Cents(source.amount ?? 0);
                    long take = Math.Min(Math.Min(sourceCents, neededCents), remaining);
                    if (take <= 0) continue;
                    source.amount = Money(sourceCents - take);
                    source.update_date = DateTime.Now;
                    db.Entry(source).State = EntityState.Modified;
                    var credited = new Guaranty { order_id = order.id, biz_type = "租赁", biz_id = target.id,
                        guaranty_type = "在线支付", amount = Money(take), valid = 1, relieve = 0, staff_id = staffId,
                        memo = "转用已解抵押的押金", create_date = DateTime.Now };
                    db.guaranty.Add(credited);
                    await db.SaveChangesAsync();
                    foreach (var gp in source.guarantyPayments.Where(x => x.payment != null && (x.payment.status ?? "").Trim() == "支付成功"))
                        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = credited.id, payment_id = gp.payment_id });
                    db.guarantyTransfer.Add(new GuarantyTransfer { order_id = order.id, source_guaranty_id = source.id,
                        target_guaranty_id = credited.id, amount = Money(take), staff_id = staffId });
                    neededCents -= take;
                    remaining -= take;
                }
                needed.amount = Money(neededCents);
                if (neededCents == 0) needed.valid = 0;
                needed.update_date = DateTime.Now;
                db.Entry(needed).State = EntityState.Modified;
            }
        }
        if (remaining != 0) throw new InvalidOperationException("可抵扣押金已变化，请刷新重试");
        await db.SaveChangesAsync();
    }

    // 取消待支付追加时，恢复来源押金并保留作废的转用记录供核对。
    public async Task ReverseAsync(int orderId, IEnumerable<int> targetGuarantyIds)
    {
        var ids = targetGuarantyIds.ToArray();
        var transfers = await db.guarantyTransfer.AsTracking().Where(t => t.order_id == orderId && t.valid == 1 && ids.Contains(t.target_guaranty_id)).ToListAsync();
        foreach (var t in transfers)
        {
            var source = await db.guaranty.AsTracking().FirstAsync(g => g.id == t.source_guaranty_id && g.order_id == orderId);
            source.amount = Money(Cents(source.amount ?? 0) + Cents(t.amount));
            source.update_date = DateTime.Now;
            db.Entry(source).State = EntityState.Modified;
            t.valid = 0;
            t.update_date = DateTime.Now;
            db.Entry(t).State = EntityState.Modified;
        }
        await db.SaveChangesAsync();
    }
}

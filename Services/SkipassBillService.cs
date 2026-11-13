using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

public sealed record SupplierBillPage(int PageCount, IReadOnlyList<ZiwoyouListOrder> Rows);

public sealed class SkipassBillRow
{
    public string orderId { get; init; } = "";
    public DateTime orderDate { get; init; }
    public string productName { get; init; } = "";
    public int num { get; init; }
    public int orderState { get; init; }
    public decimal settlementAmount { get; init; }
    public string linkMan { get; init; } = "";
    public string linkPhone { get; init; } = "";
    public bool isSystemOrder { get; init; }
    public decimal paidAmount { get; init; }
    public decimal refundAmount { get; init; }
    public decimal shareAmount { get; init; }
    public string staffName { get; init; } = "";
}

// 查询指定供应商账号，按供应商单号关联本地票；不依赖旧账号的定时缓存，不写订单或缓存表。
public sealed class SkipassBillService(ApplicationDBContext db,
    Func<string, DateTime, DateTime, int, Task<SupplierBillPage>> fetchPage)
{
    public async Task<List<SkipassBillRow>> GetAsync(string source, DateTime start, DateTime end)
    {
        var supplier = new Dictionary<string, ZiwoyouListOrder>();
        int pages = 1;
        for (int page = 0; page < pages; page++)
        {
            SupplierBillPage response = await fetchPage(source, start.Date, end.Date.AddDays(1).AddTicks(-1), page);
            if (response.Rows == null || response.PageCount < 0 || response.PageCount > 1000
                || (page > 0 && response.Rows.Count == 0))
                throw new InvalidOperationException("供应商订单列表不完整，请重试");
            pages = Math.Max(pages, response.PageCount);
            foreach (var row in response.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.orderId))
                    throw new InvalidOperationException("供应商返回了无单号的订单，请重试");
                if (row.orderDate >= start.Date && row.orderDate < end.Date.AddDays(1))
                    supplier[row.orderId.Trim()] = row;
            }
        }
        if (supplier.Count == 0) return new();

        string[] numbers = supplier.Keys.ToArray();
        var matches = await db.skiPass.AsNoTracking()
            .Where(s => s.reserve_no != null && numbers.Contains(s.reserve_no)
                && s.order_id != null && s.skiPassProduct != null && s.skiPassProduct.source == source)
            .Select(s => new { s.id, s.order_id, s.reserve_no }).ToListAsync();
        int[] orderIds = matches.Select(s => s.order_id!.Value).Distinct().ToArray();
        var orders = await db.order.AsNoTracking().Where(o => orderIds.Contains(o.id))
            .Select(o => new { o.id, staffName = o.staff == null ? "" : o.staff.name }).ToDictionaryAsync(o => o.id);
        var passes = await db.skiPass.AsNoTracking().Where(s => s.order_id != null && orderIds.Contains(s.order_id.Value))
            .Select(s => new { s.id, orderId = s.order_id!.Value, price = s.deal_price ?? s.ticket_price ?? 0 })
            .ToListAsync();
        var payments = await db.orderPayment.AsNoTracking()
            .Where(p => orderIds.Contains(p.order_id) && p.status == "支付成功"
                && (p.valid == 1 || p.pay_method == "微信支付" || p.pay_method == "支付宝"))
            .Select(p => new { p.id, p.order_id, p.amount }).ToListAsync();
        int[] paymentIds = payments.Select(p => p.id).ToArray();
        var refunds = await db.Set<OrderPaymentRefund>().AsNoTracking()
            .Where(r => paymentIds.Contains(r.payment_id)
                && (r.state == 1 || (r.refund_id != null && r.refund_id.Trim() != "")))
            .Select(r => new { r.order_id, r.skipass_id, r.amount }).ToListAsync();
        var shares = await db.orderShare.AsNoTracking().Where(s => s.valid && orderIds.Contains(s.order_id))
            .Include(s => s.paymentShares).ToListAsync();

        var paid = new Dictionary<int, decimal>();
        var refunded = new Dictionary<int, decimal>();
        var shared = new Dictionary<int, decimal>();
        var paymentsByOrder = payments.ToLookup(p => p.order_id);
        var refundsByOrder = refunds.ToLookup(r => r.order_id);
        var sharesByOrder = shares.ToLookup(s => s.order_id);
        var matchesByNumber = matches.ToLookup(s => s.reserve_no!);
        foreach (var group in passes.GroupBy(s => s.orderId))
        {
            var weights = group.OrderBy(s => s.id).Select(s => (s.id, Math.Max(0, (decimal)s.price))).ToArray();
            Add(paid, Allocate(paymentsByOrder[group.Key].Sum(p => (decimal)p.amount), weights));
            foreach (var refund in refundsByOrder[group.Key])
                Add(refunded, refund.skipass_id is int id
                    ? new Dictionary<int, decimal> { [id] = Money((decimal)refund.amount) }
                    : Allocate((decimal)refund.amount, weights));
            foreach (var share in sharesByOrder[group.Key])
            {
                decimal amount = Money((decimal)share.successSharedAmount);
                Add(shared, share.skipass_id is int id
                    ? new Dictionary<int, decimal> { [id] = amount } : Allocate(amount, weights));
            }
        }
        return supplier.Values.OrderByDescending(s => s.orderDate).ThenBy(s => s.orderId).Select(row =>
        {
            var mine = matchesByNumber[row.orderId.Trim()].ToArray();
            return new SkipassBillRow
            {
                orderId = row.orderId, orderDate = row.orderDate, productName = row.productName ?? "",
                num = row.num, orderState = row.orderState,
                settlementAmount = row.orderState == 3 ? 0 : Money((decimal)row.settlementPrice * row.num),
                linkMan = row.linkMan ?? "", linkPhone = row.linkPhone ?? "", isSystemOrder = mine.Length > 0,
                paidAmount = mine.Sum(s => paid.GetValueOrDefault(s.id)),
                refundAmount = mine.Sum(s => refunded.GetValueOrDefault(s.id)),
                shareAmount = mine.Sum(s => shared.GetValueOrDefault(s.id)),
                staffName = string.Join("、", mine.Select(s => orders.GetValueOrDefault(s.order_id!.Value)?.staffName?.Trim())
                    .Where(name => !string.IsNullOrEmpty(name)).Distinct())
            };
        }).ToList();
    }

    // 购物车一笔收款对应多张票，按票价分配到分；累计取整保证总和与实际收款完全一致。
    static Dictionary<int, decimal> Allocate(decimal amount, (int id, decimal price)[] passes)
    {
        var result = new Dictionary<int, decimal>();
        decimal total = passes.Sum(s => s.price), accumulated = 0, allocated = 0;
        foreach (var pass in passes)
        {
            accumulated += total > 0 ? pass.price : 1;
            decimal target = Money(Money(amount) * accumulated / (total > 0 ? total : passes.Length));
            result[pass.id] = target - allocated;
            allocated = target;
        }
        return result;
    }

    static void Add(Dictionary<int, decimal> target, Dictionary<int, decimal> values)
    {
        foreach (var item in values) target[item.Key] = target.GetValueOrDefault(item.Key) + item.Value;
    }
    static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

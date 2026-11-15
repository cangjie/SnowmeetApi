using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

public sealed class DistributorCustomerSummary
{
    public int memberId { get; set; }
    public string name { get; set; } = "";
    public string phoneTail { get; set; } = "";
    /// <summary>mine = 归属本分销商；other = 已归属别的分销商，只有这一笔（几笔）记在我名下；none = 还没有归属（例如雪票还没取）。</summary>
    public string relation { get; set; } = "none";
    public DateTime? since { get; set; }
    public int orderCount { get; set; }
    public double amount { get; set; }
    public DateTime? lastOrderDate { get; set; }
}

public sealed class DistributorOrderView
{
    public int orderId { get; set; }
    public DateTime date { get; set; }
    public string type { get; set; } = "";
    public string itemName { get; set; } = "";
    /// <summary>实付净额（已付 − 已退，租赁再扣掉尚未退还的押金）</summary>
    public double amount { get; set; }
    /// <summary>这一单为什么算在我名下：雪票码 / 学员卡 / 优惠券（可能多个）</summary>
    public List<string> sources { get; set; } = new();
}

/// <summary>
/// 我的顾客与消费。消费 = **归属本分销商的订单**的实付净额，逐单归属：
/// 通过我的雪票码订的（distributor_order）、用了我的学员卡的（coach_card_use）、用了我转发出去的券的（discount.ticket_code）。
/// 顾客本人归属谁不影响：已归属别人的顾客，这一笔也记在我名下。实付 = 已付 − 已退；0 的单不算。
/// </summary>
public sealed class DistributorConsumptionService(ApplicationDBContext db)
{
    public const string FromQr = "雪票码", FromCard = "学员卡", FromCoupon = "优惠券";

    private sealed record Attributed(int orderId, string source);

    /// <summary>归属本分销商的订单 id → 来源（可多个）。</summary>
    private async Task<Dictionary<int, List<string>>> AttributedOrdersAsync(Introducer me)
    {
        var map = new Dictionary<int, List<string>>();
        void Add(int orderId, string source)
        {
            if (!map.TryGetValue(orderId, out var list)) map[orderId] = list = new();
            if (!list.Contains(source)) list.Add(source);
        }
        foreach (int id in await db.distributorOrder.AsNoTracking().Where(o => o.introducer_id == me.id).Select(o => o.order_id).ToListAsync())
            Add(id, FromQr);
        if (me.coach_id != null)
        {
            int coachId = me.coach_id.Value;
            foreach (int id in await db.coachCardUse.AsNoTracking()
                .Where(u => u.coach_id == coachId && u.order_id != null && u.status == CoachCardUse.StatusEffective)
                .Select(u => u.order_id!.Value).ToListAsync())
                Add(id, FromCard);
            List<string> codes = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coachId).Select(g => g.ticket_code).ToListAsync();
            if (codes.Count > 0)
            {
                foreach (int id in await db.discount.AsNoTracking()
                    .Where(d => d.valid == 1 && d.order_id != null && d.ticket_code != null && codes.Contains(d.ticket_code))
                    .Select(d => d.order_id!.Value).ToListAsync())
                    Add(id, FromCoupon);
            }
        }
        return map;
    }

    private async Task<List<Models.Order>> LoadOrdersAsync(IEnumerable<int> orderIds)
    {
        List<int> ids = orderIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        return await db.order.AsNoTracking()
            .Where(o => o.valid == 1 && o.member_id != null && ids.Contains(o.id))
            .Include(o => o.payments).ThenInclude(p => p.refunds)
            .Include(o => o.guarantys).ThenInclude(g => g.guarantyPayments).ThenInclude(gp => gp.payment)
            .Include(o => o.cares).Include(o => o.rentals)
            .AsSplitQuery().ToListAsync();
    }

    private static DistributorOrderView ToView(Models.Order o, double amount, List<string> sources) => new()
    {
        orderId = o.id, date = o.biz_date, type = (o.type ?? "").Trim(), itemName = CoachQueryService.ItemName(o),
        amount = CoachCardRules.Money(amount), sources = sources,
    };

    public async Task<(object summary, List<DistributorCustomerSummary> customers)> GetCustomersAsync(Introducer me)
    {
        Dictionary<int, List<string>> attributed = await AttributedOrdersAsync(me);
        List<Models.Order> orders = await LoadOrdersAsync(attributed.Keys);
        // 本分销商名下的订单，逐单按实付净额汇总到顾客；顾客本人（分销商自己）的订单不算
        var perMember = new Dictionary<int, List<(Models.Order order, double amount)>>();
        foreach (Models.Order o in orders)
        {
            int mid = o.member_id!.Value;
            if (mid == me.member_id) continue;
            double amount = CoachQueryService.NetConsumption(o);
            if (amount <= 0.0001) continue;
            if (!perMember.TryGetValue(mid, out var list)) perMember[mid] = list = new();
            list.Add((o, amount));
        }
        List<DistributorCustomer> mine = await db.distributorCustomer.AsNoTracking()
            .Where(c => c.introducer_id == me.id && c.valid == 1 && c.member_id != me.member_id).ToListAsync();
        List<int> memberIds = mine.Select(c => c.member_id).Concat(perMember.Keys).Distinct().ToList();
        Dictionary<int, int> owner = (await db.distributorCustomer.AsNoTracking().Where(c => c.valid == 1 && memberIds.Contains(c.member_id))
            .Select(c => new { c.member_id, c.introducer_id }).ToListAsync()).ToDictionary(c => c.member_id, c => c.introducer_id);
        Dictionary<int, (string name, string tail)> people = await new CoachQueryService(db).LoadPeopleAsync(memberIds);
        var customers = memberIds.Select(id =>
        {
            perMember.TryGetValue(id, out var os);
            os ??= new();
            DistributorCustomer? rel = mine.FirstOrDefault(c => c.member_id == id);
            return new DistributorCustomerSummary
            {
                memberId = id, name = people[id].name, phoneTail = people[id].tail,
                relation = owner.TryGetValue(id, out int oid) ? (oid == me.id ? "mine" : "other") : "none",
                since = rel?.create_date,
                orderCount = os.Count, amount = CoachCardRules.Money(os.Sum(x => x.amount)),
                lastOrderDate = os.Count == 0 ? null : os.Max(x => x.order.biz_date),
            };
        }).OrderByDescending(c => c.amount).ThenBy(c => c.name).ToList();
        var summary = new
        {
            customers = customers.Count(c => c.relation == "mine"),
            orders = customers.Sum(c => c.orderCount),
            amount = CoachCardRules.Money(customers.Sum(c => c.amount)),
        };
        return (summary, customers);
    }

    public async Task<(DistributorCustomerSummary customer, List<DistributorOrderView> orders)> GetCustomerOrdersAsync(Introducer me, int memberId)
    {
        var (_, customers) = await GetCustomersAsync(me);
        DistributorCustomerSummary customer = customers.FirstOrDefault(c => c.memberId == memberId)
            ?? throw new CoachException("这位不是你的顾客");
        Dictionary<int, List<string>> attributed = await AttributedOrdersAsync(me);
        List<Models.Order> orders = await LoadOrdersAsync(attributed.Keys);
        var views = new List<DistributorOrderView>();
        foreach (Models.Order o in orders.Where(o => o.member_id == memberId))
        {
            double amount = CoachQueryService.NetConsumption(o);
            if (amount <= 0.0001) continue;
            views.Add(ToView(o, amount, attributed[o.id]));
        }
        return (customer, views.OrderByDescending(v => v.date).ToList());
    }
}

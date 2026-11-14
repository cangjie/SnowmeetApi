using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services
{
    /// <summary>
    /// 会员详情页「最近订单」：最近 30 单，每单带订单金额、减免、实付、用了哪些券和卡（2026-11-14 用户要求）。
    /// 只读，一批订单只做固定几条按订单号 IN 的聚合查询，不逐单查。口径和订单自己的计算属性一致：
    ///   减免 = 有效（valid = 1）的 discount 行合计（Order.discountAmount）；
    ///   实付 = Order.availablePayments 里支付成功的合计（Order.paidAmount）：微信 / 支付宝成功就算，其它方式要 valid = 1；
    ///   券 = 订单上已使用的券：ticket.order_id，或这单养护装备的 ticket_code（同名券合并，带数量）；
    ///   卡 = 次卡 / 季卡核销（punch_card_used，valid）和学员卡使用（coach_card_use）。
    ///   次卡「余几次」= 这一单用完之后还剩几次（按核销时间先后累计），不是卡现在的余额；季卡不限次数没有余次。
    /// </summary>
    public class MemberRecentOrderService
    {
        public const string KindPunch = "punch";
        public const string KindSeason = "season";
        public const string KindCoach = "coach";

        private readonly ApplicationDBContext _db;

        public MemberRecentOrderService(ApplicationDBContext db)
        {
            _db = db;
        }

        public async Task<List<MemberRecentOrder>> GetAsync(int memberId, int take = 30)
        {
            var orders = await _db.order
                .Where(o => o.member_id == memberId && o.valid == 1)
                .OrderByDescending(o => o.id).Take(take)
                .Select(o => new { o.id, o.code, o.type, o.biz_date, o.total_amount })
                .AsNoTracking().ToListAsync();
            var result = orders.Select(o => new MemberRecentOrder { id = o.id, code = o.code, type = o.type, bizDate = o.biz_date, totalAmount = o.total_amount }).ToList();
            if (result.Count == 0) return result;
            List<int> ids = orders.Select(o => o.id).ToList();
            var byId = result.ToDictionary(r => r.id);

            // 减免 / 实付
            var discounts = await _db.discount
                .Where(d => d.order_id != null && ids.Contains(d.order_id.Value) && d.valid == 1)
                .GroupBy(d => d.order_id).Select(g => new { orderId = g.Key, sum = g.Sum(d => d.amount) })
                .AsNoTracking().ToListAsync();
            foreach (var d in discounts) if (d.orderId != null && byId.TryGetValue(d.orderId.Value, out var r)) r.discountAmount = Math.Round(d.sum, 2);
            var paid = await _db.orderPayment
                .Where(p => ids.Contains(p.order_id) && p.status == "支付成功"
                    && (p.pay_method == "微信支付" || p.pay_method == "支付宝" || p.valid == 1))
                .GroupBy(p => p.order_id).Select(g => new { orderId = g.Key, sum = g.Sum(p => p.amount) })
                .AsNoTracking().ToListAsync();
            foreach (var p in paid) if (byId.TryGetValue(p.orderId, out var r)) r.paidAmount = Math.Round(p.sum, 2);

            await AddTicketsAsync(ids, byId);
            await AddPunchCardsAsync(ids, byId);
            await AddCoachCardsAsync(ids, byId);
            return result;
        }

        private async Task AddTicketsAsync(List<int> ids, Dictionary<int, MemberRecentOrder> byId)
        {
            var cares = await _db.care
                .Where(c => c.valid == 1 && c.order_id != null && ids.Contains(c.order_id.Value) && c.ticket_code != null && c.ticket_code != "")
                .Select(c => new { orderId = c.order_id!.Value, code = c.ticket_code! })
                .AsNoTracking().ToListAsync();
            List<string> careCodes = cares.Select(c => c.code).Distinct().ToList();
            var tickets = await _db.ticket
                .Where(t => t.used == 1 && ((t.order_id != null && ids.Contains(t.order_id.Value)) || careCodes.Contains(t.code)))
                .Select(t => new { t.code, t.name, t.order_id })
                .AsNoTracking().ToListAsync();
            // 每张已使用的券挂到订单上：ticket.order_id 优先，没有就按养护装备上的券号；同一张券只算一次
            var seen = new HashSet<(int, string)>();
            foreach (var t in tickets)
            {
                var orderIds = new List<int>();
                if (t.order_id != null && byId.ContainsKey(t.order_id.Value)) orderIds.Add(t.order_id.Value);
                orderIds.AddRange(cares.Where(c => c.code == t.code).Select(c => c.orderId));
                foreach (int orderId in orderIds.Distinct())
                {
                    if (!byId.TryGetValue(orderId, out var order) || !seen.Add((orderId, t.code))) continue;
                    string name = string.IsNullOrWhiteSpace(t.name) ? "优惠券" : t.name.Trim();
                    var entry = order.tickets.FirstOrDefault(x => x.name == name);
                    if (entry == null) order.tickets.Add(new MemberOrderTicket { name = name, count = 1 });
                    else entry.count++;
                }
            }
        }

        private async Task AddPunchCardsAsync(List<int> ids, Dictionary<int, MemberRecentOrder> byId)
        {
            var uses = await _db.punchCardUsed
                .Where(u => u.valid && ids.Contains(u.order_id))
                .Select(u => new { u.card_id, u.order_id, u.punch_count, u.create_date, u.id })
                .AsNoTracking().ToListAsync();
            if (uses.Count == 0) return;
            List<int> cardIds = uses.Select(u => u.card_id).Distinct().ToList();
            var cards = (await _db.punchCard.Where(c => cardIds.Contains(c.id))
                .Select(c => new { c.id, c.card_name, c.total }).AsNoTracking().ToListAsync()).ToDictionary(c => c.id);
            // 「这一单用完还剩几次」要累计这张卡所有有效核销，所以把这些卡的全部核销都读出来（按核销时间、id 先后）
            var allUses = await _db.punchCardUsed
                .Where(u => u.valid && cardIds.Contains(u.card_id))
                .Select(u => new { u.card_id, u.punch_count, u.create_date, u.id })
                .AsNoTracking().ToListAsync();
            foreach (var group in uses.GroupBy(u => (u.order_id, u.card_id)))
            {
                if (!byId.TryGetValue(group.Key.order_id, out var order) || !cards.TryGetValue(group.Key.card_id, out var card)) continue;
                int used = group.Sum(u => u.punch_count);
                var last = group.OrderBy(u => u.create_date).ThenBy(u => u.id).Last();
                int usedUpToThisOrder = allUses.Where(u => u.card_id == card.id
                    && (u.create_date < last.create_date || (u.create_date == last.create_date && u.id <= last.id))).Sum(u => u.punch_count);
                order.cards.Add(new MemberOrderCard
                {
                    name = card.card_name, kind = card.total == null ? KindSeason : KindPunch, used = used,
                    remaining = card.total == null ? null : Math.Clamp(card.total.Value - usedUpToThisOrder, 0, card.total.Value)
                });
            }
        }

        private async Task AddCoachCardsAsync(List<int> ids, Dictionary<int, MemberRecentOrder> byId)
        {
            var uses = await _db.coachCardUse
                .Where(u => u.order_id != null && ids.Contains(u.order_id.Value))
                .Select(u => new { orderId = u.order_id!.Value, u.card_id, u.coach_id })
                .AsNoTracking().ToListAsync();
            if (uses.Count == 0) return;
            List<int> coachIds = uses.Select(u => u.coach_id).Distinct().ToList();
            var coaches = (await _db.coach.Where(c => coachIds.Contains(c.id)).Select(c => new { c.id, c.display_name })
                .AsNoTracking().ToListAsync()).ToDictionary(c => c.id, c => c.display_name);
            foreach (var u in uses.GroupBy(x => (x.orderId, x.card_id)).Select(g => g.First()))
            {
                if (!byId.TryGetValue(u.orderId, out var order)) continue;
                string coach = coaches.TryGetValue(u.coach_id, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : "";
                order.cards.Add(new MemberOrderCard { name = coach + "的学员卡", kind = KindCoach, used = 1, remaining = null });
            }
        }
    }

    // 属性名保持小驼峰：会员详情接口历来直接用这些名字（id / code / type / bizDate），小程序按名字读。
    public class MemberRecentOrder
    {
        public int id { get; set; }
        public string? code { get; set; }
        public string? type { get; set; }
        public DateTime? bizDate { get; set; }
        public double totalAmount { get; set; }
        public double discountAmount { get; set; }
        public double paidAmount { get; set; }
        public List<MemberOrderTicket> tickets { get; set; } = new();
        public List<MemberOrderCard> cards { get; set; } = new();
    }

    public class MemberOrderTicket
    {
        public string name { get; set; } = "";
        public int count { get; set; }
    }

    public class MemberOrderCard
    {
        public string name { get; set; } = "";
        /// <summary>punch 次卡 | season 季卡 | coach 学员卡</summary>
        public string kind { get; set; } = "";
        public int used { get; set; }
        /// <summary>次卡这一单用完之后还剩几次；季卡、学员卡没有。</summary>
        public int? remaining { get; set; }
    }
}

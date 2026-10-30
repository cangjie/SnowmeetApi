using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Services.CoachPlatform;

public sealed class CoachCardView
{
    public int id { get; set; }
    public string tokenTail { get; set; } = "";
    public string token { get; set; } = "";
    public string status { get; set; } = "";
    public DateTime createDate { get; set; }
    public int? memberId { get; set; }
    public string? studentName { get; set; }
    public string? phoneTail { get; set; }
    public DateTime? claimedAt { get; set; }
    public DateTime? validUntil { get; set; }
    public int useCount { get; set; }
    public List<CoachCardRules.CarePriceCell> carePrices { get; set; } = new();
    public double? rentalRate { get; set; }
}

public sealed class StudentSummary
{
    public int memberId { get; set; }
    public string name { get; set; } = "";
    public string phoneTail { get; set; } = "";
    /// <summary>card = 领了学员卡；coupon = 接了教练转发的券</summary>
    public List<string> sources { get; set; } = new();
    public DateTime since { get; set; }
    public int orderCount { get; set; }
    public double amount { get; set; }
    public double cardDiscount { get; set; }
    public DateTime? lastOrderDate { get; set; }
}

public sealed class StudentOrderView
{
    public int orderId { get; set; }
    public DateTime date { get; set; }
    public string type { get; set; } = "";
    public string itemName { get; set; } = "";
    /// <summary>实付净额（已付 − 已退，租赁再扣掉尚未退还的押金）</summary>
    public double amount { get; set; }
    public bool usedCoachCard { get; set; }
    public double cardDiscount { get; set; }
}

public sealed class CoachCouponItem
{
    public string code { get; set; } = "";
    public string name { get; set; } = "";
    public int templateId { get; set; }
    /// <summary>held 在手 | pending 已转出待接受 | accepted 已被接受 | used 已使用 | expired 已过期</summary>
    public string state { get; set; } = "held";
    public string? acceptedName { get; set; }
    public string? acceptedPhoneTail { get; set; }
    public DateTime? acceptedAt { get; set; }
    public DateTime? sharedAt { get; set; }
    public DateTime? expireDate { get; set; }
}

/// <summary>教练首页 / 管理员教练详情用到的只读查询：卡的领取情况、学员及其消费、券的转发状态。</summary>
public sealed class CoachQueryService(ApplicationDBContext db)
{
    public async Task<List<CoachCardView>> ListCardsAsync(int coachId, string? status)
    {
        IQueryable<CoachCard> q = db.coachCard.AsNoTracking().Where(c => c.coach_id == coachId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.status == status);
        List<CoachCard> cards = await q.OrderByDescending(c => c.create_date).ThenByDescending(c => c.id).ToListAsync();
        List<int> memberIds = cards.Where(c => c.member_id != null).Select(c => c.member_id!.Value).Distinct().ToList();
        Dictionary<int, (string name, string tail)> people = await LoadPeopleAsync(memberIds);
        List<int> cardIds = cards.Select(c => c.id).ToList();
        Dictionary<int, int> uses = (await db.coachCardUse.AsNoTracking()
            .Where(u => cardIds.Contains(u.card_id) && u.status == CoachCardUse.StatusEffective)
            .GroupBy(u => u.card_id).Select(g => new { id = g.Key, n = g.Count() }).ToListAsync()).ToDictionary(x => x.id, x => x.n);
        return cards.Select(c =>
        {
            people.TryGetValue(c.member_id ?? 0, out var p);
            return new CoachCardView
            {
                id = c.id, token = c.token, tokenTail = c.token.Length >= 4 ? c.token.Substring(c.token.Length - 4).ToUpperInvariant() : c.token,
                status = c.status, createDate = c.create_date, memberId = c.member_id,
                studentName = c.member_id == null ? null : p.name, phoneTail = c.member_id == null ? null : p.tail,
                claimedAt = c.claimed_at, validUntil = c.end_date, useCount = uses.TryGetValue(c.id, out int n) ? n : 0,
                carePrices = CoachCardRules.ParseCarePrices(c.care_price_snapshot), rentalRate = c.rental_rate_snapshot,
            };
        }).ToList();
    }

    public async Task<object> CardCountsAsync(int coachId)
    {
        var rows = await db.coachCard.AsNoTracking().Where(c => c.coach_id == coachId)
            .GroupBy(c => c.status).Select(g => new { status = g.Key, n = g.Count() }).ToListAsync();
        int Count(string s) => rows.Where(r => r.status == s).Sum(r => r.n);
        return new
        {
            total = rows.Sum(r => r.n), issued = Count(CoachCard.StatusIssued),
            claimed = Count(CoachCard.StatusClaimed), revoked = Count(CoachCard.StatusRevoked),
        };
    }

    // ---------- 学员 ----------

    private sealed record Link(int memberId, string source, DateTime since);

    /// <summary>学员 = 领过该教练学员卡的人 ∪ 接了该教练转发券的人；起算日 = 领卡/接券当天（同一人取最早）。</summary>
    private async Task<List<Link>> LoadLinksAsync(Models.Coach coach)
    {
        List<Link> links = new();
        var claimed = await db.coachCard.AsNoTracking()
            .Where(c => c.coach_id == coach.id && c.member_id != null
                && (c.status == CoachCard.StatusClaimed || c.status == CoachCard.StatusRevoked))
            .Select(c => new { member = c.member_id!.Value, at = c.claimed_at }).ToListAsync();
        links.AddRange(claimed.Where(c => c.at != null).Select(c => new Link(c.member, "card", c.at!.Value)));
        links.AddRange(await LoadCouponAcceptersAsync(coach));
        return links.Where(l => l.memberId != coach.member_id).ToList();
    }

    /// <summary>该教练名下发放的券里，被转给别人并被接受的：接受人（会员）和接受时间。</summary>
    private async Task<List<Link>> LoadCouponAcceptersAsync(Models.Coach coach)
    {
        List<string> codes = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coach.id).Select(g => g.ticket_code).ToListAsync();
        if (codes.Count == 0) return new();
        string? coachOpenId = await OpenIdOfAsync(coach.member_id);
        if (string.IsNullOrEmpty(coachOpenId)) return new();
        var logs = await db.ticketLog.AsNoTracking().Where(l => codes.Contains(l.code)).Where(TicketTransferRules.TransferLogFilter())
            .Where(l => l.sender_open_id == coachOpenId).ToListAsync();
        if (logs.Count == 0) return new();
        List<string> openIds = logs.Select(l => l.accepter_open_id).Distinct().ToList();
        Dictionary<string, int> map = (await db.memberSocialAccount.AsNoTracking()
            .Where(m => m.valid == 1 && m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && openIds.Contains(m.num))
            .Select(m => new { m.num, m.member_id }).ToListAsync())
            .GroupBy(m => m.num).ToDictionary(g => g.Key, g => g.First().member_id);
        return logs.Where(l => map.ContainsKey(l.accepter_open_id))
            .Select(l => new Link(map[l.accepter_open_id], "coupon", l.transact_time)).ToList();
    }

    private async Task<string?> OpenIdOfAsync(int memberId) => await db.memberSocialAccount.AsNoTracking()
        .Where(m => m.member_id == memberId && m.valid == 1 && m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID)
        .OrderByDescending(m => m.id).Select(m => m.num).FirstOrDefaultAsync();

    private async Task<Dictionary<int, (string name, string tail)>> LoadPeopleAsync(List<int> memberIds)
    {
        Dictionary<int, (string, string)> result = new();
        if (memberIds.Count == 0) return result;
        var members = await db.member.AsNoTracking().Where(m => memberIds.Contains(m.id)).Select(m => new { m.id, m.real_name }).ToListAsync();
        var accounts = await db.memberSocialAccount.AsNoTracking().Where(a => a.valid == 1 && memberIds.Contains(a.member_id)
            && (a.type == MemberSocialAccount.TYPE_CELL)).OrderByDescending(a => a.id).Select(a => new { a.member_id, a.num }).ToListAsync();
        var nicks = await db.MiniAppUsers.AsNoTracking().Where(u => memberIds.Contains(u.member_id)).Select(u => new { u.member_id, u.nick }).ToListAsync();
        foreach (int id in memberIds)
        {
            string? real = members.FirstOrDefault(m => m.id == id)?.real_name;
            string? nick = nicks.FirstOrDefault(n => n.member_id == id && !string.IsNullOrWhiteSpace(n.nick))?.nick;
            string? cell = accounts.FirstOrDefault(a => a.member_id == id)?.num;
            result[id] = (CoachCardRules.StudentName(real, nick), CoachCardRules.PhoneTail(cell));
        }
        return result;
    }

    public async Task<List<StudentSummary>> GetStudentsAsync(int coachId)
    {
        Models.Coach coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId) ?? throw new CoachException("没有找到这位教练");
        List<Link> links = await LoadLinksAsync(coach);
        if (links.Count == 0) return new();
        List<int> memberIds = links.Select(l => l.memberId).Distinct().ToList();
        Dictionary<int, (string name, string tail)> people = await LoadPeopleAsync(memberIds);
        Dictionary<int, DateTime> since = links.GroupBy(l => l.memberId).ToDictionary(g => g.Key, g => g.Min(l => l.since));
        Dictionary<int, List<StudentOrderView>> orders = await LoadOrdersAsync(coachId, since);
        return memberIds.Select(id =>
        {
            List<StudentOrderView> os = orders.TryGetValue(id, out var list) ? list : new();
            return new StudentSummary
            {
                memberId = id, name = people[id].name, phoneTail = people[id].tail, since = since[id],
                sources = links.Where(l => l.memberId == id).Select(l => l.source).Distinct().OrderBy(s => s).ToList(),
                orderCount = os.Count, amount = CoachCardRules.Money(os.Sum(o => o.amount)),
                cardDiscount = CoachCardRules.Money(os.Sum(o => o.cardDiscount)),
                lastOrderDate = os.Count == 0 ? null : os.Max(o => o.date),
            };
        }).OrderByDescending(s => s.amount).ThenBy(s => s.name).ToList();
    }

    public async Task<(StudentSummary student, List<StudentOrderView> orders)> GetStudentOrdersAsync(int coachId, int memberId)
    {
        List<StudentSummary> all = await GetStudentsAsync(coachId);
        StudentSummary student = all.FirstOrDefault(s => s.memberId == memberId) ?? throw new CoachException("这位不是你的学员");
        Dictionary<int, List<StudentOrderView>> orders = await LoadOrdersAsync(coachId, new Dictionary<int, DateTime> { [memberId] = student.since });
        return (student, orders.TryGetValue(memberId, out var list) ? list.OrderByDescending(o => o.date).ToList() : new());
    }

    /// <summary>学员自起算日起的全部已付消费（所有类型）。按订单汇总，实付净额 &gt; 0 才算。</summary>
    private async Task<Dictionary<int, List<StudentOrderView>>> LoadOrdersAsync(int coachId, Dictionary<int, DateTime> since)
    {
        Dictionary<int, List<StudentOrderView>> result = new();
        if (since.Count == 0) return result;
        List<int> memberIds = since.Keys.ToList();
        DateTime min = since.Values.Min();
        List<Models.Order> orders = await db.order.AsNoTracking()
            .Where(o => o.valid == 1 && o.member_id != null && memberIds.Contains(o.member_id.Value) && o.biz_date >= min)
            .Include(o => o.payments).ThenInclude(p => p.refunds)
            .Include(o => o.guarantys).ThenInclude(g => g.guarantyPayments).ThenInclude(gp => gp.payment)
            .Include(o => o.cares).Include(o => o.rentals)
            .AsSplitQuery().ToListAsync();
        List<int> orderIds = orders.Select(o => o.id).ToList();
        Dictionary<int, double> cardDiscount = (await db.coachCardUse.AsNoTracking()
            .Where(u => u.coach_id == coachId && u.order_id != null && orderIds.Contains(u.order_id.Value) && u.status == CoachCardUse.StatusEffective)
            .GroupBy(u => u.order_id!.Value).Select(g => new { id = g.Key, d = g.Sum(x => x.discount_amount) }).ToListAsync())
            .ToDictionary(x => x.id, x => x.d);
        foreach (Models.Order o in orders)
        {
            int mid = o.member_id!.Value;
            if (o.biz_date < since[mid]) continue;
            double amount = NetConsumption(o);
            if (amount <= 0.0001) continue;
            if (!result.TryGetValue(mid, out var list)) result[mid] = list = new();
            cardDiscount.TryGetValue(o.id, out double d);
            list.Add(new StudentOrderView
            {
                orderId = o.id, date = o.biz_date, type = (o.type ?? "").Trim(), itemName = ItemName(o),
                amount = CoachCardRules.Money(amount), usedCoachCard = d > 0, cardDiscount = CoachCardRules.Money(d),
            });
        }
        return result;
    }

    /// <summary>实付净额 = 已付 − 已退；租赁订单再扣掉已收但还没退的押金（押金不算消费）。</summary>
    public static double NetConsumption(Models.Order o)
    {
        double net = o.paidAmount - o.refundAmount;
        if ((o.type ?? "").Trim() == "租赁")
        {
            double heldDeposit = Math.Max(0, o.guarantyAmount - o.refundAmount);
            net -= heldDeposit;
        }
        return Math.Max(net, 0);
    }

    private static string ItemName(Models.Order o)
    {
        string type = (o.type ?? "").Trim();
        if (type == "养护" && o.cares != null && o.cares.Count > 0)
        {
            var first = o.cares[0];
            string tier = CoachCardRules.ResolveTier(first) ?? "";
            string name = (tier + " " + (first.equipment ?? "")).Trim();
            return "养护 · " + name + (o.cares.Count > 1 ? " 等 " + o.cares.Count + " 件" : "");
        }
        if (type == "租赁" && o.rentals != null && o.rentals.Count > 0)
        {
            string n = (o.rentals[0].name ?? "").Trim();
            return "租赁 · " + (n == "" ? "租赁物" : n) + (o.rentals.Count > 1 ? " 等 " + o.rentals.Count + " 项" : "");
        }
        string sub = (o.sub_type ?? "").Trim();
        return sub == "" ? type : type + " · " + sub;
    }

    // ---------- 教练的优惠券 ----------

    public async Task<object> GetCouponsAsync(int coachId, DateTime now)
    {
        Models.Coach coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId) ?? throw new CoachException("没有找到这位教练");
        List<CoachCouponGrant> grants = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coachId).OrderByDescending(g => g.id).ToListAsync();
        List<string> codes = grants.Select(g => g.ticket_code).ToList();
        List<Ticket> tickets = codes.Count == 0 ? new() : await db.ticket.AsNoTracking().Where(t => codes.Contains(t.code)).ToListAsync();
        List<Link> accepts = await LoadCouponAcceptersAsync(coach);
        string? coachOpenId = await OpenIdOfAsync(coach.member_id);
        Dictionary<string, TicketLog> acceptLog = new();
        if (!string.IsNullOrEmpty(coachOpenId) && codes.Count > 0)
        {
            var logs = await db.ticketLog.AsNoTracking().Where(l => codes.Contains(l.code)).Where(TicketTransferRules.TransferLogFilter())
                .Where(l => l.sender_open_id == coachOpenId).OrderBy(l => l.transact_time).ToListAsync();
            foreach (var l in logs) acceptLog.TryAdd(l.code, l);
        }
        Dictionary<string, int> acceptorByCode = new();
        if (acceptLog.Count > 0)
        {
            List<string> openIds = acceptLog.Values.Select(l => l.accepter_open_id).Distinct().ToList();
            Dictionary<string, int> byOpen = (await db.memberSocialAccount.AsNoTracking()
                .Where(m => m.valid == 1 && m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && openIds.Contains(m.num))
                .Select(m => new { m.num, m.member_id }).ToListAsync()).GroupBy(m => m.num).ToDictionary(g => g.Key, g => g.First().member_id);
            foreach (var kv in acceptLog) if (byOpen.TryGetValue(kv.Value.accepter_open_id, out int mid)) acceptorByCode[kv.Key] = mid;
        }
        Dictionary<int, (string name, string tail)> people = await LoadPeopleAsync(acceptorByCode.Values.Distinct().ToList());
        List<CoachCouponItem> items = new();
        foreach (CoachCouponGrant g in grants)
        {
            Ticket? t = tickets.FirstOrDefault(x => x.code == g.ticket_code);
            if (t == null) continue;
            CoachCouponItem item = new() { code = t.code, name = t.name ?? "", templateId = t.template_id, expireDate = t.expire_date };
            if (acceptLog.TryGetValue(t.code, out TicketLog? log))
            {
                item.state = t.used == 1 && t.member_id != coach.member_id ? "used" : "accepted";
                item.acceptedAt = log.transact_time;
                if (acceptorByCode.TryGetValue(t.code, out int mid) && people.TryGetValue(mid, out var p)) { item.acceptedName = p.name; item.acceptedPhoneTail = p.tail; }
            }
            else if (t.used == 1) item.state = "used";
            else if (t.valid != 1 || (t.expire_date != null && t.expire_date < now)) item.state = "expired";
            else if (t.shared == 1) { item.state = "pending"; item.sharedAt = t.shared_time; }
            else item.state = "held";
            items.Add(item);
        }
        return new
        {
            summary = new
            {
                granted = items.Count, held = items.Count(i => i.state == "held"), pending = items.Count(i => i.state == "pending"),
                accepted = items.Count(i => i.state == "accepted" || (i.state == "used" && i.acceptedAt != null)),
                used = items.Count(i => i.state == "used"),
            },
            items,
        };
    }

    // ---------- 列表汇总 ----------

    /// <summary>教练列表里每位教练的业绩摘要：学员数、本季消费。</summary>
    public async Task<(int students, double amount)> SummaryAsync(int coachId)
    {
        List<StudentSummary> students = await GetStudentsAsync(coachId);
        return (students.Count, CoachCardRules.Money(students.Sum(s => s.amount)));
    }
}

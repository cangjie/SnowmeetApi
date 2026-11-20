using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

/// <summary>管理员查看一个分销商的卡种、券种及领取记录；不返回分享凭证。</summary>
public sealed class CoachDistributionReportService(ApplicationDBContext db)
{
    public sealed record Kind(int id, string name, string memo, string source, int claimed, int used, int unused, int expired, int invalid, int unknown);
    public sealed record Recipient(int? memberId, string name, string phone, DateTime? claimedAt, string status, string? code);
    public sealed record PageResult(int total, int page, int pageSize, List<Recipient> items);
    private sealed record CouponFact(int templateId, string code, int? memberId, DateTime claimedAt, Ticket? ticket);
    private sealed record Person(string name, string phone);

    private async Task<Coach> CoachAsync(int id) => await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == id)
        ?? throw new CoachException("没有找到这位分销商");

    public async Task<object> SummaryAsync(int coachId, DateTime now)
    {
        Coach coach = await CoachAsync(coachId);
        var cardGroups = await db.coachCard.AsNoTracking()
            .Where(c => c.coach_id == coachId && c.member_id != null)
            .GroupBy(c => c.template_id).Select(g => new { id = g.Key, count = g.Count() }).ToListAsync();
        var cardIds = cardGroups.Where(c => c.id != null).Select(c => c.id!.Value).ToList();
        var templates = await db.coachCardTemplate.AsNoTracking()
            .Where(t => t.coach_id == coachId && (t.valid == 1 || cardIds.Contains(t.id)))
            .OrderByDescending(t => t.id).ToListAsync();
        var cards = templates.Select(t => new { id = t.id, name = t.name, claimed = cardGroups.FirstOrDefault(g => g.id == t.id)?.count ?? 0 }).ToList();
        foreach (var orphan in cardGroups.Where(g => g.id != null && templates.All(t => t.id != g.id)))
            cards.Add(new { id = orphan.id!.Value, name = "历史会籍卡（卡种 " + orphan.id + "）", claimed = orphan.count });
        var old = cardGroups.FirstOrDefault(g => g.id == null);
        if (old != null) cards.Add(new { id = 0, name = "历史会籍卡", claimed = old.count });

        var (couponIds, facts) = await CouponFactsAsync(coach, null);
        var ownKinds = await db.coachCouponBatch.AsNoTracking().Where(b => b.coach_id == coachId && b.valid == 1).Select(b => b.template_id).Distinct().ToListAsync();
        var grantSources = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coachId)
            .Select(g => new { g.template_id, g.staff_id }).Distinct().ToListAsync();
        var couponTemplates = await db.ticketTemplate.AsNoTracking().Where(t => couponIds.Contains(t.id)).ToListAsync();
        var coupons = couponIds.OrderByDescending(id => id).Select(id => {
            var tpl = couponTemplates.FirstOrDefault(t => t.id == id);
            var rows = facts.Where(f => f.templateId == id).ToList();
            bool own = ownKinds.Contains(id) || grantSources.Any(g => g.template_id == id && g.staff_id == 0);
            bool admin = grantSources.Any(g => g.template_id == id && g.staff_id != 0);
            string source = own && admin ? "分销商制作、管理员发放" : own ? "分销商制作" : admin ? "管理员发放" : "历史记录";
            return new Kind(id, tpl?.name?.Trim() ?? "历史优惠券", tpl?.memo?.Trim() ?? "", source, rows.Count,
                rows.Count(f => f.ticket?.used == 1), rows.Count(f => f.ticket != null && f.ticket.used != 1),
                rows.Count(f => f.ticket != null && f.ticket.used != 1 && IsExpired(f.ticket, now)),
                rows.Count(f => f.ticket != null && f.ticket.used != 1 && f.ticket.valid != 1 && !IsExpired(f.ticket, now)),
                rows.Count(f => f.ticket == null));
        }).ToList();
        return new { cards, coupons };
    }

    public async Task<PageResult> RecipientsAsync(int coachId, string kind, int templateId, int page, int pageSize, DateTime now)
    {
        Coach coach = await CoachAsync(coachId);
        if (kind != "card" && kind != "coupon") throw new CoachException("记录类型无效");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        if (kind == "card")
        {
            var query = db.coachCard.AsNoTracking().Where(c => c.coach_id == coachId && c.member_id != null
                && (templateId == 0 ? c.template_id == null : c.template_id == templateId));
            int total = await query.CountAsync();
            var rows = await query.OrderByDescending(c => c.claimed_at).ThenByDescending(c => c.id).Skip(skip).Take(pageSize).ToListAsync();
            var people = await PeopleAsync(rows.Select(c => c.member_id!.Value).Distinct().ToList());
            return new(total, page, pageSize, rows.Select(c => {
                people.TryGetValue(c.member_id!.Value, out var p);
                string status = c.status == CoachCard.StatusRevoked ? "已撤销" : c.status == CoachCard.StatusMerged ? "会员已合并" : "已领取";
                return new Recipient(c.member_id, p?.name ?? "领取人信息缺失", p?.phone ?? "", c.claimed_at, status, CoachCardRules.CardNo(c.id));
            }).ToList());
        }
        var (_, facts) = await CouponFactsAsync(coach, templateId);
        var ordered = facts.OrderByDescending(f => f.claimedAt).ThenBy(f => f.code, StringComparer.Ordinal).ToList();
        var selected = ordered.Skip(skip).Take(pageSize).ToList();
        var persons = await PeopleAsync(selected.Where(f => f.memberId != null).Select(f => f.memberId!.Value).Distinct().ToList());
        return new(ordered.Count, page, pageSize, selected.Select(f => {
            persons.TryGetValue(f.memberId ?? 0, out var p);
            string status = f.ticket == null ? "券记录缺失" : f.ticket.used == 1 ? "已使用" : IsExpired(f.ticket, now) ? "未使用 · 已过期" : f.ticket.valid != 1 ? "未使用 · 已失效" : "未使用";
            return new Recipient(f.memberId, p?.name ?? "领取人信息缺失", p?.phone ?? "", f.claimedAt, status, f.code);
        }).ToList());
    }

    private static bool IsExpired(Ticket ticket, DateTime now) => ticket.expire_date != null && ticket.expire_date.Value.Date < now.Date;

    private async Task<(List<int> templateIds, List<CouponFact> facts)> CouponFactsAsync(Coach coach, int? templateId)
    {
        var batches = await db.coachCouponBatch.AsNoTracking().Where(b => b.coach_id == coach.id && (templateId == null || b.template_id == templateId))
            .Select(b => new { b.id, b.template_id, b.valid }).ToListAsync();
        var batchIds = batches.Select(b => b.id).ToList();
        var claims = await db.coachCouponClaim.AsNoTracking().Where(c => c.coach_id == coach.id && batchIds.Contains(c.batch_id)).ToListAsync();
        var grants = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coach.id && (templateId == null || g.template_id == templateId)).ToListAsync();
        var ids = batches.Where(b => b.valid == 1 || claims.Any(c => c.batch_id == b.id)).Select(b => b.template_id)
            .Concat(grants.Select(g => g.template_id)).Distinct().ToList();
        var codes = claims.Select(c => c.ticket_code).Concat(grants.Select(g => g.ticket_code)).Distinct().ToList();
        var tickets = codes.Count == 0 ? new Dictionary<string, Ticket>() :
            await db.ticket.AsNoTracking().Where(t => codes.Contains(t.code)).ToDictionaryAsync(t => t.code);
        var facts = new Dictionary<string, CouponFact>();
        var batchMap = batches.ToDictionary(b => b.id, b => b.template_id);
        foreach (var c in claims)
        {
            if (!batchMap.TryGetValue(c.batch_id, out int id)) continue;
            tickets.TryGetValue(c.ticket_code, out var ticket);
            facts.TryAdd(c.ticket_code, new CouponFact(id, c.ticket_code, c.member_id, c.create_date, ticket));
        }

        // 旧版先发券到分销商，再由他转赠；按首次从该分销商转出的记录识别原始领取人。
        var legacyCodes = grants.Select(g => g.ticket_code).Where(code => !facts.ContainsKey(code)).Distinct().ToList();
        if (legacyCodes.Count > 0)
        {
            var coachOpenIds = await db.memberSocialAccount.AsNoTracking().Where(a => a.member_id == coach.member_id
                && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID).Select(a => a.num).Distinct().ToListAsync();
            var logs = await db.ticketLog.AsNoTracking().Where(l => legacyCodes.Contains(l.code) && coachOpenIds.Contains(l.sender_open_id))
                .Where(TicketTransferRules.TransferLogFilter()).OrderBy(l => l.transact_time).ThenBy(l => l.id).ToListAsync();
            var receiverIds = logs.Select(l => l.accepter_open_id).Distinct().ToList();
            var accounts = await db.memberSocialAccount.AsNoTracking().Where(a => a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && receiverIds.Contains(a.num))
                .OrderByDescending(a => a.valid).ThenByDescending(a => a.id).Select(a => new { a.num, a.member_id }).ToListAsync();
            var memberMap = accounts.GroupBy(a => a.num).ToDictionary(g => g.Key, g => g.First().member_id);
            var grantsByCode = grants.GroupBy(g => g.ticket_code).ToDictionary(g => g.Key, g => g.First());
            foreach (var log in logs)
            {
                if (facts.ContainsKey(log.code) || !grantsByCode.TryGetValue(log.code, out var grant)) continue;
                tickets.TryGetValue(log.code, out var ticket);
                int? member = memberMap.TryGetValue(log.accepter_open_id, out int mid) ? mid : null;
                facts.Add(log.code, new CouponFact(grant.template_id, log.code, member, log.transact_time, ticket));
            }
        }
        return (ids, facts.Values.ToList());
    }

    private async Task<Dictionary<int, Person>> PeopleAsync(List<int> ids)
    {
        if (ids.Count == 0) return new();
        var members = await db.member.AsNoTracking().Where(m => ids.Contains(m.id)).Select(m => new { m.id, m.real_name }).ToListAsync();
        var profiles = await db.memberProfile.AsNoTracking().Where(p => ids.Contains(p.member_id)).Select(p => new { p.member_id, p.nick_name }).ToListAsync();
        var phones = await db.memberSocialAccount.AsNoTracking().Where(a => ids.Contains(a.member_id) && a.valid == 1 && a.type == MemberSocialAccount.TYPE_CELL)
            .OrderByDescending(a => a.id).Select(a => new { a.member_id, a.num }).ToListAsync();
        return members.ToDictionary(m => m.id, m => new Person(CoachCardRules.StudentName(m.real_name, profiles.FirstOrDefault(p => p.member_id == m.id)?.nick_name),
            (phones.FirstOrDefault(p => p.member_id == m.id)?.num ?? "").Trim()));
    }
}

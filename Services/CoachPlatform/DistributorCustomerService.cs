using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

/// <summary>
/// 分销商的顾客归属与订单归属。规则（用户 2026-11-15 定）：
/// 顾客接受了分销商转发的优惠券，或通过分销商的雪票二维码订购了雪票后——还没有归属分销商就建立归属，
/// 已有归属就不变；但这一笔订单记在当前分销商名下。
/// </summary>
public sealed class DistributorCustomerService(ApplicationDBContext db)
{
    /// <summary>雪票码上的分销商：必须启用、是教练或机构；顾客本人就是这位分销商时不算（自己买自己的码不分账、不建关系）。</summary>
    public async Task<Introducer?> ResolveSkipassDistributorAsync(int? introducerId, int buyerMemberId)
    {
        if (introducerId == null || introducerId <= 0) return null;
        Introducer? intro = await db.introducer.AsNoTracking().FirstOrDefaultAsync(i => i.id == introducerId && i.valid == 1);
        if (intro == null || !DistributorMenuRules.HasMenu(intro.type) || intro.member_id == buyerMemberId) return null;
        return intro;
    }

    /// <summary>建立顾客归属。成功建立返回 true；已有归属、分销商无效或是顾客本人返回 false（不动原关系）。</summary>
    public async Task<bool> EstablishAsync(int memberId, int introducerId, string source, string? sourceRef, DateTime at)
    {
        Introducer? intro = await db.introducer.AsNoTracking().FirstOrDefaultAsync(i => i.id == introducerId && i.valid == 1);
        if (intro == null || intro.member_id == memberId) return false;
        if (await db.distributorCustomer.AsNoTracking().AnyAsync(c => c.member_id == memberId && c.valid == 1)) return false;
        var row = new DistributorCustomer { member_id = memberId, introducer_id = introducerId, source = source, source_ref = sourceRef, valid = 1, create_date = at };
        db.distributorCustomer.Add(row);
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // 唯一索引兜底：并发下同一顾客已被别人先归属
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>订单归属：同一单同一位分销商只记一次。</summary>
    public async Task RecordOrderAsync(int orderId, int introducerId, int memberId, string source, DateTime at)
    {
        if (await db.distributorOrder.AsNoTracking().AnyAsync(o => o.order_id == orderId && o.introducer_id == introducerId)) return;
        var row = new DistributorOrder { order_id = orderId, introducer_id = introducerId, member_id = memberId, source = source, create_date = at };
        db.distributorOrder.Add(row);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.Entry(row).State = EntityState.Detached; }
    }

    /// <summary>雪票取票成功：这一单是通过分销商二维码订的，就按规则建立顾客归属（与员工推荐关系同一时机）。</summary>
    public async Task EstablishForPickedUpOrderAsync(int orderId, DateTime at)
    {
        List<DistributorOrder> rows = await db.distributorOrder.AsNoTracking().Where(o => o.order_id == orderId).OrderBy(o => o.id).ToListAsync();
        foreach (DistributorOrder row in rows)
            if (await EstablishAsync(row.member_id, row.introducer_id, DistributorCustomer.SourceQr, orderId.ToString(), at)) break;
    }

    /// <summary>顾客领了教练 / 机构的学员卡。</summary>
    public async Task OnCardClaimedAsync(int coachId, int memberId, int cardId, DateTime at)
    {
        Introducer? intro = await IntroducerOfCoachAsync(coachId);
        if (intro != null) await EstablishAsync(memberId, intro.id, DistributorCustomer.SourceCard, cardId.ToString(), at);
    }

    /// <summary>顾客接受了转发的优惠券：券是管理员发给某位分销商的，且转出的人就是这位分销商。</summary>
    public async Task OnTicketAcceptedAsync(string ticketCode, int? senderMemberId, int accepterMemberId, DateTime at)
    {
        if (senderMemberId == null) return;
        var grants = await db.coachCouponGrant.AsNoTracking().Where(g => g.ticket_code == ticketCode).Select(g => g.coach_id).ToListAsync();
        foreach (int coachId in grants)
        {
            Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId);
            if (coach == null || coach.member_id != senderMemberId) continue;
            Introducer? intro = await IntroducerOfCoachAsync(coachId);
            if (intro != null && await EstablishAsync(accepterMemberId, intro.id, DistributorCustomer.SourceCoupon, ticketCode, at)) return;
        }
    }

    /// <summary>
    /// 把现有的学员关系（领卡 / 接券）补录成顾客归属：按时间从早到晚，先到先得，已有归属的不动。可重复执行。
    /// 返回新建的条数。
    /// </summary>
    public async Task<int> BackfillLegacyLinksAsync()
    {
        var query = new CoachQueryService(db);
        var all = new List<(int memberId, string source, DateTime since, int introducerId)>();
        foreach (Introducer intro in await db.introducer.AsNoTracking().Where(i => i.valid == 1 && i.coach_id != null).ToListAsync())
        {
            foreach (var link in await query.GetStudentLinksAsync(intro.coach_id!.Value))
                all.Add((link.memberId, link.source, link.since, intro.id));
        }
        int created = 0;
        foreach (var link in all.OrderBy(l => l.since).ThenBy(l => l.introducerId))
        {
            string source = link.source == "card" ? DistributorCustomer.SourceCard : DistributorCustomer.SourceCoupon;
            if (await EstablishAsync(link.memberId, link.introducerId, source, null, link.since)) created++;
        }
        return created;
    }

    private async Task<Introducer?> IntroducerOfCoachAsync(int coachId)
    {
        Introducer? intro = await db.introducer.AsNoTracking().FirstOrDefaultAsync(i => i.coach_id == coachId && i.valid == 1);
        if (intro != null) return intro;
        Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId);
        return coach == null ? null : await db.introducer.AsNoTracking().FirstOrDefaultAsync(i => i.member_id == coach.member_id && i.valid == 1);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.Rent;

/// <summary>Commits a freshly revalidated quote without performing any payment or refund.</summary>
public sealed class RentalBenefitSettlementService(ApplicationDBContext db)
{
    private static decimal Money(double n) => Math.Round((decimal)n, 2, MidpointRounding.AwayFromZero);

    private static Dictionary<int, decimal> Allocate(List<RentalDetail> details, decimal payable)
    {
        decimal total = details.Sum(d => Money(d.amount));
        if (total == 0) return details.ToDictionary(d => d.id, _ => 0m);
        var rows = details.Select(d => new { d.id, exact = Money(d.amount) * payable * 100 / total })
            .Select(x => new { x.id, floor = decimal.Floor(x.exact), rem = x.exact - decimal.Floor(x.exact) }).ToList();
        int extra = (int)(payable * 100 - rows.Sum(x => x.floor));
        var up = rows.OrderByDescending(x => x.rem).ThenBy(x => x.id).Take(extra).Select(x => x.id).ToHashSet();
        return rows.ToDictionary(x => x.id, x => (x.floor + (up.Contains(x.id) ? 1 : 0)) / 100);
    }

    public async Task<RentalBenefitSettlement> CommitAsync(int orderId, string fingerprint, int staffId, DateTime now)
    {
        return await MiniTicketTransaction.Run(db, async () =>
        {
            await MiniTicketTransaction.Lock(db, "snowmeet:rental-benefit:commit");
            var previous = await db.rentalBenefitSettlement.AsNoTracking().FirstOrDefaultAsync(s => s.order_id == orderId);
            if (previous != null) return previous;
            var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == orderId && o.type == "租赁" && o.valid == 1)
                ?? throw new InvalidOperationException("未找到租赁订单");
            var quote = await new RentalBenefitQuoteService(db).QuoteAsync(orderId, false, now);
            if (!quote.canChange || quote.mode != "auto") throw new InvalidOperationException("该订单不能自动核销优惠");
            if (string.IsNullOrEmpty(fingerprint) || fingerprint != quote.fingerprint)
                throw new InvalidOperationException("优惠或租金已变化，请刷新并重新确认");
            var rentals = await db.rental.AsNoTracking().Where(r => r.order_id == orderId && r.valid == 1
                && (r.appending == null || (r.appending == false && r.append_commit_time != null))).ToListAsync();
            if (rentals.Count == 0 || rentals.Any(r => r.settled != 1))
                throw new InvalidOperationException("请先归还全部租赁物");
            if (quote.units.Count == 0) throw new InvalidOperationException("租金明细尚未生成");
            if (quote.units.Any(u => u.detailIds.Count == 0)) throw new InvalidOperationException("租金明细尚未生成");
            bool needsAssetVerification = quote.quote.Lines.Any(l => l.UsesPunch || l.UsesSeason);
            if (needsAssetVerification && !order.wechat_unverified)
                throw new InvalidOperationException("使用次卡或季卡前请先核验会员身份");
            if (order.member_id == null && (needsAssetVerification || quote.quote.CouponCode != null))
                throw new InvalidOperationException("未找到会员");

            var punchCards = await db.punchCard.AsNoTracking().Where(c => c.member_id == order.member_id && c.biz_type == "租赁"
                && !c.is_refund && !c.refund_pending && c.total != null && c.total > (c.punches ?? 0)).OrderBy(c => c.id).ToListAsync();
            var seasonCards = await db.punchCard.AsNoTracking().Where(c => c.member_id == order.member_id && c.biz_type == "租赁"
                && !c.is_refund && !c.refund_pending && c.total == null).OrderBy(c => c.id).ToListAsync();
            var punchConsumption = new Dictionary<(int card, int rental), int>();
            var seasonTaken = new HashSet<(int card, DateTime day)>();
            var allIds = quote.units.SelectMany(u => u.detailIds).ToList();
            var details = await db.rentalDetail.Where(d => allIds.Contains(d.id) && d.valid == 1).ToListAsync();
            if (details.Count != allIds.Count) throw new InvalidOperationException("租金明细已变化，请刷新");
            var discounts = await db.discount.Where(d => d.valid == 1 && d.biz_type == "租赁" && d.sub_biz_type == "日租金"
                && d.sub_biz_id != null && allIds.Contains(d.sub_biz_id.Value)).ToListAsync();
            var cardUses = await db.coachCardUse.Where(u => u.biz_type == "租赁" && allIds.Contains(u.sub_biz_id)).ToListAsync();
            var coachIds = quote.units.Where(u => u.coachCardId != null).Select(u => u.coachCardId!.Value).Distinct().ToList();
            var coachCards = await db.coachCard.AsNoTracking().Where(c => coachIds.Contains(c.id)).ToListAsync();
            await new CoachCardBenefits(db).ApplyCurrentAsync(coachCards);

            foreach (var unit in quote.units)
            {
                var selected = quote.quote.Lines.Single(l => l.Id == unit.id);
                if (unit.manual && !unit.repriceManualCard) continue;
                var rows = details.Where(d => unit.detailIds.Contains(d.id)).OrderBy(d => d.id).ToList();
                foreach (var discount in discounts.Where(d => d.biz_id == unit.rentalId && unit.detailIds.Contains(d.sub_biz_id!.Value)))
                { discount.valid = 0; discount.update_date = now; db.Entry(discount).State = EntityState.Modified; }
                foreach (var use in cardUses.Where(u => u.biz_id == unit.rentalId && unit.detailIds.Contains(u.sub_biz_id)
                    && u.status == CoachCardUse.StatusEffective))
                { use.status = CoachCardUse.StatusReversed; use.reverse_date = now; use.reverse_reason = "租赁优惠重新匹配"; db.Entry(use).State = EntityState.Modified; }

                if (selected.UsesPunch || selected.UsesSeason)
                {
                    foreach (var row in rows) { row.valid = 0; row.update_date = now; db.Entry(row).State = EntityState.Modified; }
                    if (selected.UsesPunch)
                    {
                        var card = punchCards.FirstOrDefault(c => (c.punches ?? 0) < c.total);
                        if (card == null) throw new InvalidOperationException("次卡余额已变化，请刷新");
                        card.punches = (card.punches ?? 0) + 1;
                        var key = (card.id, unit.rentalId);
                        punchConsumption[key] = punchConsumption.GetValueOrDefault(key) + 1;
                    }
                    else
                    {
                        var day = unit.date.Date;
                        PunchCard? card = null;
                        foreach (var c in seasonCards)
                            if (!seasonTaken.Contains((c.id, day)) && !await db.rentalSeasonCardUse.AnyAsync(u => u.card_id == c.id && u.rental_date == day && u.valid))
                            { card = c; break; }
                        if (card == null) throw new InvalidOperationException("季卡当天已使用，请刷新");
                        seasonTaken.Add((card.id, day));
                        db.rentalSeasonCardUse.Add(new RentalSeasonCardUse { card_id = card.id, order_id = orderId,
                            rental_detail_id = rows[0].id, rental_date = day, valid = true, create_date = now });
                    }
                    continue;
                }
                var allocation = Allocate(rows, selected.Payable);
                bool useCoach = quote.quote.CouponCode == null && unit.coachCardId != null;
                var selectedCoach = useCoach ? coachCards.FirstOrDefault(c => c.id == unit.coachCardId) : null;
                foreach (var row in rows)
                {
                    decimal saved = Money(row.amount) - allocation[row.id];
                    if (saved <= 0) continue;
                    // The legacy rental total counts non-null ticket_code twice. The coupon
                    // is linked by rental_benefit_settlement instead of putting it on this row.
                    db.discount.Add(new Discount { order_id = orderId, biz_type = "租赁", biz_id = unit.rentalId,
                        sub_biz_type = "日租金", sub_biz_id = row.id, amount = (double)saved, valid = 1,
                        staff_id = staffId, memo = quote.quote.CouponCode == null ? "会籍卡优惠" : "租赁整单优惠券", create_date = now });
                    if (selectedCoach != null)
                    {
                        var use = cardUses.FirstOrDefault(u => u.biz_id == unit.rentalId && u.sub_biz_id == row.id);
                        if (use == null)
                        {
                            use = new CoachCardUse { biz_type = "租赁", biz_id = unit.rentalId, sub_biz_id = row.id };
                            db.coachCardUse.Add(use);
                        }
                        else db.Entry(use).State = EntityState.Modified;
                        use.card_id = selectedCoach.id; use.coach_id = selectedCoach.coach_id; use.member_id = order.member_id ?? 0;
                        use.order_id = orderId; use.mode = CoachCardRules.ModeRate; use.mode_value = selectedCoach.rental_rate_snapshot ?? 0;
                        use.base_amount = row.amount; use.discount_amount = (double)saved; use.status = CoachCardUse.StatusEffective;
                        use.reverse_date = null; use.reverse_reason = null; use.create_date = now;
                    }
                }
            }
            foreach (var byCard in punchConsumption.GroupBy(x => x.Key.card))
            {
                int count = byCard.Sum(x => x.Value);
                int changed = await db.punchCard.Where(c => c.id == byCard.Key && c.member_id == order.member_id && !c.is_refund && !c.refund_pending
                        && c.total != null && c.total - (c.punches ?? 0) >= count)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.punches, c => (c.punches ?? 0) + count).SetProperty(c => c.update_date, now));
                if (changed != 1) throw new InvalidOperationException("次卡余额已变化，请刷新");
                foreach (var entry in byCard)
                    db.punchCardUsed.Add(new PunchCardUsed { card_id = byCard.Key, order_id = orderId, biz_type = "租赁",
                        biz_id = entry.Key.rental, punch_count = entry.Value, valid = true, create_date = now });
            }
            if (quote.quote.CouponCode != null)
            {
                string code = quote.quote.CouponCode;
                int changed = await db.ticket.Where(t => t.code == code && t.member_id == order.member_id && t.valid == 1
                        && t.is_active == 1 && t.used == 0 && t.shared == 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.used, 1).SetProperty(t => t.used_time, now)
                        .SetProperty(t => t.biz_type, "租赁").SetProperty(t => t.biz_id, orderId)
                        .SetProperty(t => t.use_memo, "租赁整单优惠"));
                if (changed != 1) throw new InvalidOperationException("优惠券已不可用，请刷新");
            }
            var settlement = new RentalBenefitSettlement { order_id = orderId, coupon_code = quote.quote.CouponCode,
                rent_payable = (double)quote.quote.Payable, staff_id = staffId, create_date = now };
            db.rentalBenefitSettlement.Add(settlement);
            await db.SaveChangesAsync();
            return settlement;
        });
    }
}

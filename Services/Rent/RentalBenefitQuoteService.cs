using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Services.Rent;

/// <summary>Read-only quote. Asset balances are checked again under a lock before use.</summary>
public sealed class RentalBenefitQuoteService(ApplicationDBContext db)
{
    public sealed record Unit(int id, int rentalId, DateTime date, IReadOnlyList<int> detailIds,
        decimal gross, decimal currentPayable, int? coachCardId, bool manual, bool eligible, bool repriceManualCard);
    public sealed record Result(string mode, bool canChange, string? reason,
        RentalBenefitOptimizer.Quote quote, IReadOnlyList<Unit> units, string fingerprint);

    private static decimal Money(double n) => Math.Round((decimal)n, 2, MidpointRounding.AwayFromZero);

    public async Task<Result> QuoteAsync(int orderId, bool draftAuto, DateTime now)
    {
        var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == orderId && o.type == "租赁")
            ?? throw new InvalidOperationException("未找到租赁订单");
        var preference = await db.rentalBenefitPreference.AsNoTracking().FirstOrDefaultAsync(p => p.order_id == orderId);
        var finalized = await db.rentalBenefitSettlement.AsNoTracking().AnyAsync(s => s.order_id == orderId);
        // A pending refund is also frozen: changing the price while the payment provider
        // is processing it would invalidate the amount already requested.
        var hasRefund = await db.orderPaymentRefund.AsNoTracking().AnyAsync(r => r.order_id == orderId);
        bool canChange = order.closed != 1 && !hasRefund && !finalized;
        string mode = order.valid == 0 && draftAuto ? "auto" : preference?.mode ?? "none";
        var rentals = await db.rental.Where(r => r.order_id == orderId
                && (order.valid == 0 || (r.valid == 1 && (r.appending == null || (r.appending == false && r.append_commit_time != null)))))
            .Include(r => r.details.Where(d => d.valid == 1))
            .Include(r => r.discounts.Where(d => d.valid == 1 && d.biz_type == "租赁"))
            .Include(r => r.pricePresets)
            .Include(r => r.rentItems.Where(i => i.valid == 1 || order.valid == 0)).ThenInclude(i => i.category)
            .AsSplitQuery().AsNoTracking().ToListAsync();
        bool legacyOrder = order.valid == 1 && preference == null;
        // Legacy orders with an explicitly selected membership card keep that card
        // and receive its current rate. Unselected legacy rentals stay untouched.
        if (legacyOrder && rentals.Any(r => r.coach_card_id != null)) mode = "auto";
        var rentalIds = rentals.Select(r => r.id).ToList();
        var preferences = await db.rentalBenefitRentalPreference.AsNoTracking()
            .Where(p => rentalIds.Contains(p.rental_id)).ToDictionaryAsync(p => p.rental_id);
        var uses = await db.coachCardUse.AsNoTracking().Where(u => u.biz_type == "租赁" && rentalIds.Contains(u.biz_id)
            && u.status == CoachCardUse.StatusEffective).ToListAsync();
        var detailIds = rentals.SelectMany(r => r.details).Select(d => d.id).ToList();
        var manualDetailIds = (await db.rentalBenefitDetailPreference.AsNoTracking()
            .Where(p => detailIds.Contains(p.rental_detail_id)).Select(p => p.rental_detail_id).ToListAsync()).ToHashSet();
        var memberId = order.member_id;
        var cards = memberId == null ? new List<CoachCard>() : await db.coachCard.AsNoTracking()
            .Where(c => c.member_id == memberId && c.status == CoachCard.StatusClaimed).ToListAsync();
        await new CoachCardBenefits(db).ApplyCurrentAsync(cards);
        cards = cards.Where(c => memberId != null && CoachCardRules.IsEffectiveOnDate(c, memberId.Value, now)).ToList();
        var punchCards = memberId == null ? new List<PunchCard>() : await db.punchCard.AsNoTracking()
            .Where(c => c.member_id == memberId && c.biz_type == "租赁" && !c.is_refund && !c.refund_pending).OrderBy(c => c.id).ToListAsync();
        int punches = punchCards.Where(c => c.total != null).Sum(c => Math.Max(0, c.total!.Value - (c.punches ?? 0)));
        var seasonIds = punchCards.Where(c => c.total == null).Select(c => c.id).ToList();
        var seasonUses = await db.rentalSeasonCardUse.AsNoTracking().Where(u => u.valid && seasonIds.Contains(u.card_id)).ToListAsync();
        var units = new List<Unit>();
        var lines = new List<RentalBenefitOptimizer.Line>();
        var seasonCounts = new Dictionary<DateOnly, int>();
        foreach (var rental in rentals.OrderBy(r => r.id))
        {
            if (rental.entertain || rental.experience) continue;
            bool explicitNone = preferences.TryGetValue(rental.id, out var pref) && (pref.mode == "none" || pref.mode == "manual");
            if (pref?.mode == "none" && rental.coach_card_id != null)
                throw new InvalidOperationException("租赁优惠选择冲突，请重新选择");
            bool selectedCard = rental.coach_card_id != null;
            bool ski = rental.rentItems.Any(i => !i.noNeed && i.category?.code != null
                && new[] { "01", "02", "03", "04" }.Any(p => i.category.code.StartsWith(p)));
            var rentDetails = rental.details.Where(d => d.charge_type == "租金").ToList();
            var rentDetailIds = rentDetails.Select(d => d.id).ToHashSet();
            var otherDetailIds = rental.details.Where(d => d.charge_type != "租金").Select(d => d.id).ToHashSet();
            // Legacy order-level discounts have no reliable per-day provenance. Preserve
            // them and do not stack an automatic benefit on the same rental.
            bool unallocatedDiscount = rental.discounts.Any(d => d.sub_biz_id == null
                || (d.sub_biz_type == "日租金" ? !rentDetailIds.Contains(d.sub_biz_id.Value)
                    : !otherDetailIds.Contains(d.sub_biz_id.Value)));
            var groups = rentDetails.GroupBy(d => d.rental_date.Date).OrderBy(g => g.Key).ToList();
            foreach (var group in groups)
            {
                var rows = group.ToList();
                var rowIds = rows.Select(d => d.id).ToList();
                decimal gross = rows.Sum(d => Money(d.amount));
                if (gross < 0) throw new InvalidOperationException("租金明细无效");
                var ds = rental.discounts.Where(d => d.sub_biz_type == "日租金" && d.sub_biz_id != null && rowIds.Contains(d.sub_biz_id.Value)).ToList();
                if (mode == "auto" && canChange && ds.Any(d => d.ticket_code != null))
                    throw new InvalidOperationException("本单包含历史租赁券减免，请人工核对后处理");
                // Existing non-card discounts and manual prices are deliberate staff decisions.
                bool manualDiscount = rows.Any(row =>
                {
                    var rowDiscounts = ds.Where(d => d.sub_biz_id == row.id).ToList();
                    var cardUse = uses.FirstOrDefault(u => u.sub_biz_id == row.id && u.biz_id == rental.id);
                    return rowDiscounts.Any(d => d.ticket_code != null)
                        || (rowDiscounts.Count > 0 && (cardUse == null
                            || Money(rowDiscounts.Sum(d => d.amount)) != Money(cardUse.discount_amount)));
                });
                bool manualPrice = rental.pricePresets.Any(p => p.manual);
                decimal existing = Math.Max(0, gross - ds.Sum(d => Money(d.amount)));
                bool manual = explicitNone || selectedCard || (legacyOrder && !selectedCard) || manualDiscount || manualPrice || unallocatedDiscount || rows.Any(d => manualDetailIds.Contains(d.id)) || mode != "auto" || !canChange;
                CoachCard? best = selectedCard ? cards.FirstOrDefault(c => c.id == rental.coach_card_id
                    && CoachCardRules.IsEffectiveOnDate(c, memberId!.Value, group.Key))
                    : cards.Where(c => memberId != null && CoachCardRules.IsEffectiveOnDate(c, memberId.Value, group.Key)
                        && c.rental_rate_snapshot is > 0 and <= 1)
                        .OrderBy(c => c.rental_rate_snapshot).ThenBy(c => c.id).FirstOrDefault();
                decimal cardPayable = best == null ? gross : gross - Money(CoachPricingService.QuoteRentalDiscount(best, (double)gross));
                // Explicitly selected cards retain their identity but use the current card rate.
                bool repriceManualCard = canChange && selectedCard && best != null && !manualDiscount && !manualPrice && !unallocatedDiscount
                    && !rows.Any(d => manualDetailIds.Contains(d.id));
                decimal? manualPayable = manual ? (repriceManualCard
                    ? cardPayable : existing) : null;
                int key = rows.Min(d => d.id);
                units.Add(new Unit(key, rental.id, group.Key, rowIds, gross, existing, best?.id, manual, ski, repriceManualCard));
                lines.Add(new RentalBenefitOptimizer.Line(key, gross, cardPayable, ski, manualPayable, DateOnly.FromDateTime(group.Key)));
                var day = DateOnly.FromDateTime(group.Key);
                if (!seasonCounts.ContainsKey(day)) seasonCounts[day] = seasonIds.Count(id => !seasonUses.Any(u => u.card_id == id && u.rental_date.Date == group.Key));
            }
            if (groups.Count == 0 && order.valid == 0)
            {
                var preset = rental.pricePresets.OrderBy(p => p.rent_date).FirstOrDefault();
                if (preset == null) continue;
                decimal gross = Money(preset.price);
                var date = preset.rent_date.Date;
                var best = selectedCard ? cards.FirstOrDefault(c => c.id == rental.coach_card_id
                        && CoachCardRules.IsEffectiveOnDate(c, memberId!.Value, date))
                    : cards.Where(c => memberId != null && CoachCardRules.IsEffectiveOnDate(c, memberId.Value, date)
                        && c.rental_rate_snapshot is > 0 and <= 1).OrderBy(c => c.rental_rate_snapshot).ThenBy(c => c.id).FirstOrDefault();
                decimal cardPayable = best == null ? gross : gross - Money(CoachPricingService.QuoteRentalDiscount(best, (double)gross));
                bool manual = explicitNone || selectedCard || preset.manual || preset.discount > 0 || mode != "auto";
                decimal existing = Math.Max(0, gross - Money(preset.discount));
                decimal? manualPayable = manual ? (selectedCard && best != null && !preset.manual && preset.discount <= 0 ? cardPayable : existing) : null;
                int key = -rental.id;
                units.Add(new Unit(key, rental.id, date, Array.Empty<int>(), gross, existing, best?.id, manual, ski, selectedCard && best != null && !preset.manual && preset.discount <= 0));
                lines.Add(new RentalBenefitOptimizer.Line(key, gross, cardPayable, ski, manualPayable, DateOnly.FromDateTime(date)));
                var day = DateOnly.FromDateTime(date);
                if (!seasonCounts.ContainsKey(day)) seasonCounts[day] = seasonIds.Count(id => !seasonUses.Any(u => u.card_id == id && u.rental_date.Date == date));
            }
        }
        var coupons = new List<RentalBenefitOptimizer.Coupon>();
        if (mode == "auto" && canChange && memberId != null)
        {
            var tickets = await db.ticket.Include(t => t.template).ThenInclude(t => t.productTicketTemplates)
                .Where(t => t.member_id == memberId && t.valid == 1 && t.is_active == 1 && t.used == 0 && t.shared == 0
                    && t.template.biz_type == "租赁").AsSplitQuery().AsNoTracking().ToListAsync();
            foreach (var ticket in tickets.Where(t => (t.start_date == null || t.start_date.Value.Date <= now.Date)
                    && (t.expire_date == null || t.expire_date.Value.Date >= now.Date)))
            {
                var rules = ticket.template.productTicketTemplates.Where(r => r.valid && r.product_id == 0).ToList();
                if (rules.Count != 1) continue;
                var rule = rules[0];
                if (rule.fixed_price is >= 0) coupons.Add(new(ticket.code, "fixed", Money(rule.fixed_price.Value)));
                else if (rule.discount_rate is > 0 and <= 1) coupons.Add(new(ticket.code, "rate", (decimal)rule.discount_rate.Value));
                else if (rule.discount_amount is >= 0) coupons.Add(new(ticket.code, "amount", Money(rule.discount_amount.Value)));
            }
        }
        var quote = RentalBenefitOptimizer.FindBest(lines, mode == "auto" && canChange ? punches : 0, coupons,
            mode == "auto" && canChange ? seasonCounts : null);
        var fingerprintInput = JsonSerializer.Serialize(new { mode, canChange, quote, units,
            cards = punchCards.Select(c => new { c.id, c.total, c.punches }),
            seasonUses = seasonUses.OrderBy(u => u.id).Select(u => new { u.card_id, u.rental_date }) });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)));
        return new Result(mode, canChange, !canChange ? "订单已结算或正在退款" : null, quote, units, fingerprint);
    }
}

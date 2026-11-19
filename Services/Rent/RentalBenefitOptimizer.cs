using System;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Services.Rent;

/// <summary>
/// Pure rental quote calculation. A coupon covers the whole order's rent and cannot
/// be combined with card discounts; punches can replace individual card-priced days.
/// This class never consumes a coupon or card and does not persist a selection.
/// </summary>
public static class RentalBenefitOptimizer
{
    public sealed record Line(int Id, decimal Gross, decimal CardPayable, bool PunchEligible,
        decimal? ManualPayable = null, DateOnly? Day = null);
    public sealed record Coupon(string Code, string Mode, decimal Value);
    public sealed record LineResult(int Id, decimal Payable, bool UsesPunch, bool UsesSeason = false);
    public sealed record Quote(string? CouponCode, decimal Payable, IReadOnlyList<LineResult> Lines);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Allocate the order-wide payable back to rental lines exactly to the cent so
    // line totals, refund calculation and the order total cannot disagree.
    private static IReadOnlyList<LineResult> AllocateCoupon(IReadOnlyList<Line> lines, decimal gross, decimal payable)
    {
        if (gross == 0) return lines.Select(l => new LineResult(l.Id, 0, false)).ToList();
        var portions = lines.Select(l => new
        {
            l.Id,
            ExactCents = Money(l.Gross) * payable * 100 / gross
        }).Select(p => new { p.Id, Whole = decimal.Floor(p.ExactCents), Remainder = p.ExactCents - decimal.Floor(p.ExactCents) }).ToList();
        var extra = (int)(payable * 100 - portions.Sum(p => p.Whole));
        var roundedUp = portions.OrderByDescending(p => p.Remainder).ThenBy(p => p.Id).Take(extra).Select(p => p.Id).ToHashSet();
        return lines.Select(l => new LineResult(l.Id,
            (portions.Single(p => p.Id == l.Id).Whole + (roundedUp.Contains(l.Id) ? 1 : 0)) / 100, false)).ToList();
    }

    public static Quote FindBest(IReadOnlyList<Line> lines, int punches, IReadOnlyList<Coupon> coupons,
        IReadOnlyDictionary<DateOnly, int>? seasonCardsAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(coupons);
        if (punches < 0 || lines.Select(l => l.Id).Distinct().Count() != lines.Count)
            throw new ArgumentException("Invalid punch count or duplicated rental line.");
        foreach (var line in lines)
        {
            if (line.Gross < 0 || line.CardPayable < 0 || line.CardPayable > line.Gross
                || (line.ManualPayable != null && (line.ManualPayable < 0 || line.ManualPayable > line.Gross)))
                throw new ArgumentException("Invalid rental line amount.");
        }
        if (seasonCardsAvailable != null && seasonCardsAvailable.Values.Any(n => n < 0))
            throw new ArgumentException("Invalid season card availability.");

        // A manual selection, including an explicit choice to use no discount,
        // is represented by ManualPayable and is never replaced automatically.
        // Available season-card counts must already exclude cards used on that date
        // elsewhere. A card covers at most one eligible line per calendar day.
        var selectedSeason = lines.Where(l => l.ManualPayable == null && l.PunchEligible && l.CardPayable > 0 && l.Day != null)
            .GroupBy(l => l.Day!.Value)
            .SelectMany(g => g.OrderByDescending(l => Money(l.CardPayable)).ThenBy(l => l.Id)
                .Take(seasonCardsAvailable != null && seasonCardsAvailable.TryGetValue(g.Key, out var count) ? count : 0))
            .Select(l => l.Id).ToHashSet();
        var selectedPunches = lines.Where(l => l.ManualPayable == null && l.PunchEligible && l.CardPayable > 0
                && !selectedSeason.Contains(l.Id))
            .OrderByDescending(l => Money(l.CardPayable)).ThenBy(l => l.Id).Take(punches)
            .Select(l => l.Id).ToHashSet();
        var cardLines = lines.Select(l => new LineResult(l.Id,
            selectedPunches.Contains(l.Id) || selectedSeason.Contains(l.Id) ? 0 : Money(l.ManualPayable ?? l.CardPayable),
            selectedPunches.Contains(l.Id), selectedSeason.Contains(l.Id))).ToList();
        var best = new Quote(null, Money(cardLines.Sum(l => l.Payable)), cardLines);

        // An order-wide coupon would overwrite a line deliberately chosen by staff.
        if (lines.Any(l => l.ManualPayable != null)) return best;
        var gross = Money(lines.Sum(l => Money(l.Gross)));
        foreach (var coupon in coupons.OrderBy(c => c.Code, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(coupon.Code)) throw new ArgumentException("Coupon code is required.");
            decimal payable = coupon.Mode switch
            {
                "rate" when coupon.Value >= 0 && coupon.Value <= 1 => Money(gross * coupon.Value),
                "amount" when coupon.Value >= 0 => Money(Math.Max(0, gross - coupon.Value)),
                "fixed" when coupon.Value >= 0 => Money(Math.Min(gross, coupon.Value)),
                _ => throw new ArgumentException("Invalid coupon rule.")
            };
            // On a tie, keep the cards and avoid consuming a coupon unnecessarily.
            if (payable < best.Payable)
                best = new Quote(coupon.Code, payable, AllocateCoupon(lines, gross, payable));
        }
        return best;
    }
}

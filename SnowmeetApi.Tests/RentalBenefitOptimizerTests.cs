using SnowmeetApi.Services.Rent;
using static SnowmeetApi.Services.Rent.RentalBenefitOptimizer;

namespace SnowmeetApi.Tests;

public class RentalBenefitOptimizerTests
{
    [Fact]
    public void PunchesCoverTheMostExpensiveEligibleCardPricedDays()
    {
        var q = FindBest(new[] { new Line(1, 200, 100, true), new Line(2, 300, 150, true),
            new Line(3, 100, 50, false) }, 1, Array.Empty<Coupon>());
        Assert.Equal(150, q.Payable);
        Assert.Equal(new[] { 2 }, q.Lines.Where(l => l.UsesPunch).Select(l => l.Id));
    }

    [Fact]
    public void WholeOrderCouponAndCardsAreComparedWithoutStacking()
    {
        var lines = new[] { new Line(1, 200, 100, true), new Line(2, 100, 50, false) };
        var q = FindBest(lines, 1, new[] { new Coupon("A", "amount", 270), new Coupon("B", "rate", .5m) });
        Assert.Equal("A", q.CouponCode);
        Assert.Equal(30, q.Payable);
        Assert.DoesNotContain(q.Lines, l => l.UsesPunch);
        Assert.Equal(q.Payable, q.Lines.Sum(l => l.Payable));
    }

    [Fact]
    public void OrderCouponIsAllocatedExactlyToTheCent()
    {
        var q = FindBest(new[] { new Line(1, 1, 1, false), new Line(2, 1, 1, false), new Line(3, 1, 1, false) },
            0, new[] { new Coupon("A", "fixed", 1) });
        Assert.Equal(1m, q.Lines.Sum(l => l.Payable));
        Assert.Equal(new[] { .34m, .33m, .33m }, q.Lines.Select(l => l.Payable));
    }

    [Fact]
    public void ManualChoiceAndExplicitNoDiscountAreNotOverridden()
    {
        var q = FindBest(new[] { new Line(1, 200, 100, true, 200), new Line(2, 100, 50, true) },
            2, new[] { new Coupon("free", "fixed", 0) });
        Assert.Null(q.CouponCode);
        Assert.Equal(200, q.Payable);
        Assert.False(q.Lines.Single(l => l.Id == 1).UsesPunch);
    }

    [Fact]
    public void EqualPriceDoesNotConsumeAnExtraCouponOrPunch()
    {
        var q = FindBest(new[] { new Line(1, 100, 0, true) }, 1, new[] { new Coupon("free", "fixed", 0) });
        Assert.Null(q.CouponCode);
        Assert.False(Assert.Single(q.Lines).UsesPunch);
    }

    [Fact]
    public void EachAvailableSeasonCardCoversAtMostOneEligibleLinePerDay()
    {
        var day = new DateOnly(2026, 11, 19);
        var lines = new[] { new Line(1, 200, 100, true, Day: day), new Line(2, 300, 150, true, Day: day),
            new Line(3, 100, 50, true, Day: day.AddDays(1)) };
        var q = FindBest(lines, 0, Array.Empty<Coupon>(), new Dictionary<DateOnly, int> { [day] = 1, [day.AddDays(1)] = 1 });
        Assert.Equal(100m, q.Payable);
        Assert.Equal(new[] { 2, 3 }, q.Lines.Where(l => l.UsesSeason).Select(l => l.Id));
    }
}

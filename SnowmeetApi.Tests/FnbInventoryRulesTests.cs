using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbInventoryRulesTests
{
    [Theory]
    [InlineData("2026-01-31", 1, "month", "2026-02-28")]
    [InlineData("2028-01-31", 1, "month", "2028-02-29")]
    [InlineData("2026-03-01", 30, "day", "2026-03-31")]
    public void ShelfLifeUsesCalendarMonthAndDayRules(string production, int value, string unit, string expected)
    {
        var actual = FnbInventoryRules.CalculateExpiry(DateOnly.Parse(production), value, unit);
        Assert.Equal(DateOnly.Parse(expected), actual);
    }

    [Fact]
    public void OpenedBatchKeepsEarlierOriginalExpiry()
    {
        var actual = FnbInventoryRules.OpenedExpiry(
            new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1), 10);
        Assert.Equal(new DateOnly(2026, 10, 5), actual);
    }

    [Fact]
    public void FefoSkipsSealedExpiredAndDestroyedAndNeverOverdraws()
    {
        var today = new DateOnly(2026, 9, 22);
        var batches = new[]
        {
            new AvailableBatch(1, new DateOnly(2026, 9, 21), "bulk", 5m, 10m, true, false),
            new AvailableBatch(2, new DateOnly(2026, 9, 23), "sealed", 5m, 10m, true, false),
            new AvailableBatch(3, new DateOnly(2026, 9, 24), "bulk", 3m, 9m, true, false),
            new AvailableBatch(4, new DateOnly(2026, 9, 25), "opened", 2m, 8m, true, false),
            new AvailableBatch(5, new DateOnly(2026, 9, 26), "bulk", 2m, 8m, true, true)
        };

        var result = FnbInventoryRules.AllocateFefo(batches, 7m, today);

        Assert.Equal(5m, result.ActualQuantity);
        Assert.Equal(2m, result.ShortageQuantity);
        Assert.Collection(result.Lines,
            first => { Assert.Equal(3, first.BatchId); Assert.Equal(3m, first.Quantity); Assert.Equal(9m, first.Amount); },
            second => { Assert.Equal(4, second.BatchId); Assert.Equal(2m, second.Quantity); Assert.Equal(8m, second.Amount); });
    }

    [Fact]
    public void LastDepletionCarriesAllRemainingCost()
    {
        var result = FnbInventoryRules.AllocateFefo(
            [new AvailableBatch(7, new DateOnly(2026, 10, 1), "bulk", 3m, 1m, true, false)],
            3m, new DateOnly(2026, 9, 22));
        Assert.Equal(1m, Assert.Single(result.Lines).Amount);
    }
}

using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbStocktakeRulesTests
{
    [Fact]
    public void FingerprintDetectsBatchChangeButIgnoresEnumerationOrder()
    {
        var a = new StocktakeBatch(1, "bulk", 3m, 7m, new DateOnly(2026, 10, 1), true, false, null, [1, 2]);
        var b = new StocktakeBatch(2, "sealed", 2m, 5m, new DateOnly(2026, 10, 2), true, false, null, [3, 4]);
        var date = new DateOnly(2026, 9, 22);
        Assert.Equal(FnbStocktakeRules.Fingerprint(10, date, [a, b]), FnbStocktakeRules.Fingerprint(10, date, [b, a]));
        Assert.NotEqual(FnbStocktakeRules.Fingerprint(10, date, [a, b]),
            FnbStocktakeRules.Fingerprint(10, date, [a with { Quantity = 4m }, b]));
        Assert.NotEqual(FnbStocktakeRules.Fingerprint(10, date, [a, b]),
            FnbStocktakeRules.Fingerprint(10, date.AddDays(1), [a, b]));
    }
}

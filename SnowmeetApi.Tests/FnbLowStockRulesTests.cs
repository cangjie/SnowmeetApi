using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbLowStockRulesTests
{
    [Fact]
    public void DefaultThresholdIsTenPercentOfTheLastBatch()
    {
        Assert.Equal(100m, FnbLowStockRules.Threshold(null, null, 1000m));
        Assert.True(FnbLowStockRules.IsLow(100m, 100m));
        Assert.False(FnbLowStockRules.IsLow(100.5m, 100m));
    }

    [Fact]
    public void FixedQuantityWinsOverRatioAndNeedsNoInboundRecord()
    {
        Assert.Equal(300m, FnbLowStockRules.Threshold(0.2m, null, 1500m));
        Assert.Equal(20m, FnbLowStockRules.Threshold(null, 20m, 1000m));
        Assert.Equal(0m, FnbLowStockRules.Threshold(null, 0m, null));
        Assert.True(FnbLowStockRules.IsLow(0m, 0m));
    }

    [Fact]
    public void WithoutInboundRecordRatioCannotAlert()
    {
        Assert.Null(FnbLowStockRules.Threshold(null, null, null));
        Assert.Null(FnbLowStockRules.Threshold(0.5m, null, 0m));
        Assert.False(FnbLowStockRules.IsLow(0m, null));
    }

    [Fact]
    public void SettingAcceptsOneOfRatioOrQuantity()
    {
        Assert.Null(FnbLowStockRules.Validate(null, null));
        Assert.Null(FnbLowStockRules.Validate(0.15m, null));
        Assert.Null(FnbLowStockRules.Validate(1m, null));
        Assert.Null(FnbLowStockRules.Validate(null, 0m));
        Assert.NotNull(FnbLowStockRules.Validate(0.1m, 100m));
        Assert.NotNull(FnbLowStockRules.Validate(0m, null));
        Assert.NotNull(FnbLowStockRules.Validate(1.5m, null));
        Assert.NotNull(FnbLowStockRules.Validate(0.12345m, null));
        Assert.NotNull(FnbLowStockRules.Validate(null, -1m));
    }
}

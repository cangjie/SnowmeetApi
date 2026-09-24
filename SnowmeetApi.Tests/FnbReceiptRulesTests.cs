using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbReceiptRulesTests
{
    private static readonly FnbMaterialItem Flour = new() { id = 1, name = "面粉", base_unit_code = "g", item_type = "raw", valid = true };
    private static readonly FnbUnit Kg = new() { code = "kg", dimension = 1, factor_to_base = 1000, valid = true };
    private static readonly FnbUnit G = new() { code = "g", dimension = 1, factor_to_base = 1, valid = true };

    [Fact]
    public void BulkReceiptConvertsKilogramsAndCalculatesCost()
    {
        var input = Sample() with { Quantity = 2.5m, InputUnitCode = "kg", UnitPrice = 8m };
        var plan = FnbReceiptRules.Plan(input, Flour, Kg, G);
        Assert.Equal(2500m, plan.BaseQuantity);
        Assert.Equal(20m, plan.Amount);
        Assert.Equal(1000m, plan.InputToBase);
    }

    [Fact]
    public void SealedReceiptRequiresWholePacksAndOpenLife()
    {
        var input = Sample() with { StockForm = "sealed", Quantity = 3, PackSize = 500,
            PackUnitName = "袋", OpenStorageType = "chilled", OpenShelfLifeDays = 2 };
        var plan = FnbReceiptRules.Plan(input, Flour, Kg, G);
        Assert.Equal(1500m, plan.BaseQuantity);
        Assert.Throws<ArgumentException>(() => FnbReceiptRules.Plan(input with { Quantity = 3.5m }, Flour, Kg, G));
        Assert.Throws<ArgumentException>(() => FnbReceiptRules.Plan(input with { OpenShelfLifeDays = null }, Flour, Kg, G));
    }

    [Fact]
    public void BatchPhotosAreOptionalButMustBeValidWhenGiven()
    {
        Assert.Equal(1000m, FnbReceiptRules.Plan(Sample() with { ImageIds = [] }, Flour, Kg, G).BaseQuantity);
        Assert.Throws<ArgumentException>(() => FnbReceiptRules.Plan(Sample() with { ImageIds = [5, 5] }, Flour, Kg, G));
        Assert.Throws<ArgumentException>(() => FnbReceiptRules.Plan(Sample() with { ImageIds = [0] }, Flour, Kg, G));
    }

    [Fact]
    public void ReceiptRejectsWrongDimensionAndExpiryBeforeProduction()
    {
        var input = Sample() with { ProductionDate = new DateOnly(2026, 10, 2), ExpireDate = new DateOnly(2026, 10, 1) };
        Assert.Throws<ArgumentException>(() => FnbReceiptRules.Plan(input, Flour, Kg, G));
        input = Sample();
        Assert.Throws<ArgumentException>(() => FnbReceiptRules.Plan(input, Flour, new FnbUnit { code = "ml", dimension = 2 }, G));
    }

    private static ReceiptInput Sample() => new(10, Guid.NewGuid(), 1, "LOT-1", "bulk", "ambient", null,
        1m, "kg", 10m, new DateOnly(2026, 9, 1), 30, "day", new DateOnly(2026, 10, 1),
        3, [1], null, null, null, null, "package", null);
}

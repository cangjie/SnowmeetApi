using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbRecipeRulesTests
{
    [Fact]
    public void PreparationScalesIngredientsFromBaseOutput()
    {
        Assert.Equal(250m, FnbRecipeRules.RequiredQuantity(500m, 1000m, 500m));
        Assert.Throws<ArgumentException>(() => FnbRecipeRules.RequiredQuantity(0.000001m, 1000m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => FnbRecipeRules.RequiredQuantity(500m, 1000m, 0m));
    }
}

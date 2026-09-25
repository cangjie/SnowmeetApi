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

    [Fact]
    public void KitchenOrderAdjustsRecipeQuantitiesOnlyForRecipeItems()
    {
        var recipe = new Dictionary<int, decimal> { [1] = 10m, [2] = 1m, [3] = 15m };
        Assert.Same(recipe, FnbServeService.Adjust(recipe, null));
        var adjusted = FnbServeService.Adjust(recipe, [new KitchenIngredientInput(1, 12m), new KitchenIngredientInput(3, 0m)]);
        Assert.Equal(new Dictionary<int, decimal> { [1] = 12m, [2] = 1m }, adjusted);
        Assert.Equal(15m, recipe[3]);
        Assert.Throws<ArgumentException>(() => FnbServeService.Adjust(recipe, [new KitchenIngredientInput(9, 5m)]));
        Assert.Throws<ArgumentException>(() => FnbServeService.Adjust(recipe, [new KitchenIngredientInput(1, -1m)]));
        Assert.Throws<ArgumentException>(() => FnbServeService.Adjust(recipe,
            [new KitchenIngredientInput(1, 0m), new KitchenIngredientInput(2, 0m), new KitchenIngredientInput(3, 0m)]));
    }
}

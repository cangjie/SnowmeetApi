using System;

namespace SnowmeetApi.Services.Fnb;

public static class FnbRecipeRules
{
    public static decimal RequiredQuantity(decimal ingredientQuantity, decimal recipeOutput, decimal desiredOutput)
    {
        if (ingredientQuantity <= 0 || recipeOutput <= 0 || desiredOutput <= 0)
            throw new ArgumentOutOfRangeException(nameof(desiredOutput));
        decimal result = Math.Round(ingredientQuantity * desiredOutput / recipeOutput, 6, MidpointRounding.AwayFromZero);
        if (result <= 0 || result >= 1_000_000_000_000m)
            throw new ArgumentException("配方折算后用量超出库存精度");
        return result;
    }
}

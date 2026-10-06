using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbV4RulesTests
{
    [Fact]
    public void ItemOverridesThenCategoryThenFallback()
    {
        var category = new FnbCategory { warn_days = 3, open_days = 5 };
        var item = new FnbItem();
        Assert.Equal((3, (int?)5), FnbV4Rules.Defaults(item, category));
        item.warn_days = 0; item.open_days = 0;
        Assert.Equal((0, (int?)0), FnbV4Rules.Defaults(item, category));
        item.warn_days = null; item.open_days = null; category.warn_days = null; category.open_days = null;
        Assert.Equal((1, (int?)null), FnbV4Rules.Defaults(item, category));
    }
    [Theory]
    [InlineData(5, "cold")]
    [InlineData(6, "warm")]
    [InlineData(9, "warm")]
    [InlineData(10, "cold")]
    public void ShelfRuleUsesProductionSeasonAndItemPrecedence(int month, string season)
    {
        var rules = new[] {
            new FnbShelfLifeRule { id = 1, category_id = 10, storage_type = "frozen", season = season, days = 100, valid = true },
            new FnbShelfLifeRule { id = 2, item_id = 20, storage_type = "frozen", season = "all", days = 30, valid = true },
            new FnbShelfLifeRule { id = 3, item_id = 20, storage_type = "frozen", season = season, days = 10, valid = false } };
        Assert.Equal(2, FnbV4Rules.ShelfRule(rules, 20, 10, "frozen", month)!.id);
        rules[2].valid = true;
        Assert.Equal(3, FnbV4Rules.ShelfRule(rules, 20, 10, "frozen", month)!.id);
        Assert.Null(FnbV4Rules.ShelfRule(rules, 20, 10, "ambient", month));
    }
    [Theory]
    [InlineData(1000, 1, 1000)]
    [InlineData(8000, 400, 20)]
    [InlineData(0.5, 1, 0.5)]
    public void ConversionAllowsDecimalInputs(decimal upstream, decimal downstream, decimal expected) =>
        Assert.Equal(expected, FnbV4Rules.Ratio(upstream, downstream));
    [Fact]
    public void RejectsInvalidYieldUnitsAndUnsupportedVarcharText()
    {
        Assert.Throws<ArgumentException>(() => FnbV4Rules.Operation("切片", 1.1m, 0));
        Assert.Throws<ArgumentException>(() => FnbV4Rules.Operation("切片", 0, 0));
        Assert.Throws<ArgumentException>(() => FnbV4Rules.Quantity(0.0000001m));
        Assert.Throws<ArgumentException>(() => FnbV4Rules.RequiredText("牛肉😀", 200, "名称"));
        Assert.Equal("BEEF", FnbV4Rules.Code(" beef ", "批次编码"));
        Assert.Equal("g", FnbV4Rules.BaseUnit("weight"));
    }
}

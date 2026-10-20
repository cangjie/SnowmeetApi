using SnowmeetApi.Services.Qisemi;

namespace SnowmeetApi.Tests;

public class QisemiRulesTests
{
    [Fact]
    public void SearchTreatsSqlWildcardsAsLiteralTextAndKeepsSizeTokens()
    {
        Assert.Equal("%Fischer~_110~%~[MV~~%", QisemiRules.Like("Fischer_110%[MV~"));
        Assert.Equal(new[] { "Fischer", "U06424", "26.5" }, QisemiRules.Tokens(" Fischer  U06424\t26.5 Fischer "));
        Assert.Equal("9007199254740993", QisemiRules.Id(9007199254740993));
    }
    [Fact]
    public void CatalogContainsEveryApprovedEntryAndKeepsEmptyModules()
    {
        Assert.Equal(50, QisemiCatalog.Modules.Count);
        Assert.Equal(50, QisemiCatalog.Modules.Select(m => m.Key).Distinct().Count());
        Assert.Equal("sale", QisemiCatalog.Get("sales").DocumentType);
        Assert.Contains(QisemiCatalog.Modules, m => m.Key == "other_inbound");
        Assert.Contains(QisemiCatalog.Modules, m => m.Key == "balances");
        Assert.Throws<QisemiReadException>(() => QisemiCatalog.Get("anything; DROP TABLE qsm_source"));
    }
    [Fact]
    public void InvalidDatesPagingAndDownloadNamesAreBounded()
    {
        Assert.Throws<ArgumentException>(() => new QisemiQuery { DateFrom = new(2026, 10, 20), DateTo = new(2026, 10, 19) }.Validate());
        Assert.ThrowsAny<Exception>(() => new QisemiQuery { PageSize = 101 }.Validate());
        Assert.ThrowsAny<Exception>(() => new QisemiQuery { SortBy = "name;DROP TABLE x" }.Validate());
        Assert.ThrowsAny<Exception>(() => new QisemiQuery { SortDirection = "sideways" }.Validate());
        Assert.ThrowsAny<Exception>(() => new QisemiQuery { ResultCategoryId = -1 }.Validate());
        Assert.Equal("库存.xlsx", QisemiRules.SafeName("../../库存.xlsx\r\n"));
    }
    [Fact]
    public void RawBusinessArraysRemainVisibleWhilePrivateStorageAndCredentialsAreOmitted()
    {
        var json = QisemiRules.Json("{\"object_key\":\"private/secret\",\"Lines\":[{\"quantity\":-1,\"Password\":\"secret\",\"custom\":\"保留业务字段\"}]}");
        Assert.False(json.TryGetProperty("object_key", out _));
        Assert.False(json.GetProperty("Lines")[0].TryGetProperty("Password", out _));
        Assert.Equal(-1, json.GetProperty("Lines")[0].GetProperty("quantity").GetInt32());
        Assert.Equal("保留业务字段", json.GetProperty("Lines")[0].GetProperty("custom").GetString());
    }
}

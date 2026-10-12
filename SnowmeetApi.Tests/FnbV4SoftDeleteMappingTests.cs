using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Tests;

// 2026-10-12：v4 每张表都有 valid；后补 valid 的表查询只看有效行。只用模型元数据，不连接数据库。
public class FnbV4SoftDeleteMappingTests
{
    private static ApplicationDBContext Db() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
        .UseSqlServer("Server=localhost;Database=metadata_only;Trusted_Connection=True").Options);

    private static readonly Type[] Master = [typeof(FnbCategory), typeof(FnbItem), typeof(FnbItemForm), typeof(FnbPurchaseSpec),
        typeof(FnbShelfLifeRule), typeof(FnbDishSpec), typeof(FnbArea), typeof(FnbSupply), typeof(FnbTool), typeof(FnbCheckItem)];

    [Fact]
    public void EveryV4TableHasValidDefaultingToTrue()
    {
        using var db = Db();
        var v4 = db.GetService<IDesignTimeModel>().Model.GetEntityTypes().Where(x => (x.GetTableName() ?? "").StartsWith("fnb_v4_")).ToArray();
        Assert.Equal(31, v4.Length);
        Assert.All(v4, x =>
        {
            var valid = x.FindProperty("valid");
            Assert.True(valid != null, x.GetTableName());
            Assert.Equal(typeof(bool), valid!.ClrType);
            Assert.Equal(true, valid.GetDefaultValue());
        });
        // 新建对象不显式赋值时就是有效的，不依赖调用方记得写 valid = true。
        Assert.True(new FnbBatch().valid && new FnbStockDocument().valid && new FnbOrder().valid && new FnbCheckSheet().valid && new FnbRequest().valid);
    }

    [Fact]
    public void LateValidTablesAreFilteredAndMasterDataAndRequestsAreNot()
    {
        using var db = Db();
        var v4 = db.Model.GetEntityTypes().Where(x => (x.GetTableName() ?? "").StartsWith("fnb_v4_")).ToArray();
        var unfiltered = v4.Where(x => x.GetQueryFilter() == null).Select(x => x.ClrType).ToHashSet();
        Assert.Equal(Master.Append(typeof(FnbRequest)).ToHashSet(), unfiltered);
        Assert.Equal(20, v4.Length - unfiltered.Count);

        Assert.Contains("[valid] = CAST(1 AS bit)", db.fnbBatch.ToQueryString());
        Assert.Contains("[valid] = CAST(1 AS bit)", db.fnbStockMovement.ToQueryString());
        Assert.Contains("[valid] = CAST(1 AS bit)", db.fnbCheckSheet.Where(x => x.shop_id == 1).ToQueryString());
        Assert.DoesNotContain("WHERE", db.fnbRequest.ToQueryString());
        Assert.DoesNotContain("WHERE", db.fnbItem.ToQueryString());
        // 关联查询两边都只看有效行。
        string joined = (from m in db.fnbStockMovement join b in db.fnbBatch on m.batch_id equals b.id select new { m.id, b.batch_no }).ToQueryString();
        Assert.Equal(2, joined.Split("[valid] = CAST(1 AS bit)").Length - 1);
    }

    [Fact]
    public void NaturalKeysAreUniqueAmongValidRowsOnly()
    {
        using var db = Db();
        var model = db.GetService<IDesignTimeModel>().Model;
        string? Filter(Type type, params string[] columns) => model.FindEntityType(type)!.GetIndexes()
            .Single(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(columns)).GetFilter();
        Assert.Equal("[valid] = 1", Filter(typeof(FnbBatch), "shop_id", "batch_no"));
        Assert.Equal("[valid] = 1", Filter(typeof(FnbCheckSheet), "shop_id", "business_date"));
        Assert.Equal("[valid] = 1", Filter(typeof(FnbOrderImport), "shop_id", "source_method", "dedupe_key"));
        Assert.Equal("[valid] = 1", Filter(typeof(FnbTool), "shop_id", "asset_no"));
        Assert.Equal("[valid] = 1 AND [external_order_no] IS NOT NULL", Filter(typeof(FnbOrder), "shop_id", "source_type", "external_order_no"));
    }
}

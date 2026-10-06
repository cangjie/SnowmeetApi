using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Tests;

public class FnbSchemaMappingTests
{
    [Fact]
    public void V4SchemaReplacesOldInventoryAndUsesNoCascades()
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlServer("Server=localhost;Database=metadata_only;Trusted_Connection=True").Options);
        var entities = db.GetService<IDesignTimeModel>().Model.GetEntityTypes().ToArray();
        string[] expected = ["fnb_unit", "fnb_category", "fnb_item", "fnb_item_form", "fnb_purchase_spec",
            "fnb_shelf_life_rule", "fnb_batch", "fnb_batch_image", "fnb_stock_operation", "fnb_dish_spec",
            "fnb_recipe", "fnb_recipe_line", "fnb_stock_document", "fnb_stock_document_line", "fnb_stock_movement",
            "fnb_stocktake_line", "fnb_order", "fnb_order_line", "fnb_order_import"];
        var tables = entities.Select(x => x.GetTableName()).ToArray();
        foreach (var name in expected) Assert.Contains(name == "fnb_unit" ? name : name.Replace("fnb_", "fnb_v4_"), tables);
        Assert.DoesNotContain("fnb_material_batch", tables);
        Assert.DoesNotContain("fnb_material_batch_stock", tables);
        Assert.DoesNotContain("fnb_material_category", tables);
        Assert.Null(db.Model.FindEntityType(typeof(FnbMaterialBatchStock)));
        var fnb = entities.Where(x => (x.GetTableName() ?? "").StartsWith("fnb_")).ToArray();
        Assert.All(fnb.SelectMany(x => x.GetForeignKeys()), x => Assert.Equal(DeleteBehavior.NoAction, x.DeleteBehavior));
        Assert.All(fnb.SelectMany(x => x.GetProperties()).Where(x => x.ClrType == typeof(string)),
            x => Assert.Equal("Chinese_PRC_CI_AS", x.GetCollation()));
        Assert.Equal("datetime2(3)", db.Model.FindEntityType(typeof(FnbItem))!.FindProperty("created_at")!.GetColumnType());
        Assert.NotNull(db.Model.FindEntityType(typeof(FnbBatch))!.FindProperty("effective_expire")!.GetComputedColumnSql());
        Assert.Null(db.Model.FindEntityType(typeof(FnbShelfLifeRule))!.FindProperty("production_month"));
        Assert.Contains("vw_fnb_v4_stock", entities.Select(x => x.GetViewName()));
        Assert.Contains("vw_fnb_v4_loss", entities.Select(x => x.GetViewName()));
        Assert.NotNull(db.Model.FindEntityType(typeof(Order))!.FindProperty("order_source"));
        Assert.NotNull(db.Model.FindEntityType(typeof(Order))!.FindProperty("source_order_no"));
    }
    [Fact]
    public void FoodBigIntIdsSerializeAsStrings()
    {
        var order = new FnbOrder { id = 9_007_199_254_740_993L };
        Assert.Contains("\"id\":\"9007199254740993\"", System.Text.Json.JsonSerializer.Serialize(order));
        var operation = new FnbStockOperation { id = order.id, document_id = order.id };
        Assert.Contains("\"document_id\":\"9007199254740993\"", System.Text.Json.JsonSerializer.Serialize(operation));
    }
}

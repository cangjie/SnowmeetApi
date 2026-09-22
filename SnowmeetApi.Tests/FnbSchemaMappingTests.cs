using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class FnbSchemaMappingTests
{
    [Fact]
    public void FoodInventoryBigIntIdsSerializeAsStringsForMiniProgramClients()
    {
        var order = new SnowmeetApi.Models.Fnb.FnbOrder { id = 9_007_199_254_740_993L };
        var json = System.Text.Json.JsonSerializer.Serialize(order);
        Assert.Contains("\"id\":\"9007199254740993\"", json);
        var document = new SnowmeetApi.Models.Fnb.FnbStockDocument { id = 5, order_id = order.id };
        json = System.Text.Json.JsonSerializer.Serialize(document);
        Assert.Contains("\"order_id\":\"9007199254740993\"", json);
    }

    [Fact]
    public void InventoryTablesAndOrderSourceColumnsAreQueryableThroughEf()
    {
        var options = new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlServer("Server=localhost;Database=snowmeet_fnb_metadata_only;Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var db = new ApplicationDBContext(options);
        string[] expectedTables =
        [
            "fnb_unit", "fnb_material_category", "fnb_shelf_life_rule", "fnb_material_item",
            "fnb_material_batch_stock", "fnb_dish_spec", "fnb_recipe", "fnb_recipe_line",
            "fnb_order", "fnb_order_line", "fnb_order_import", "fnb_stock_document", "fnb_stock_document_line",
            "fnb_stock_movement", "fnb_stocktake_line"
        ];
        var mapped = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();
        foreach (string table in expectedTables)
            Assert.Contains(table, mapped);
        Assert.Contains("vw_fnb_material_stock", db.Model.GetEntityTypes().Select(e => e.GetViewName()));
        Assert.Contains("vw_fnb_material_loss", db.Model.GetEntityTypes().Select(e => e.GetViewName()));

        var order = db.Model.FindEntityType(typeof(Order));
        Assert.NotNull(order);
        Assert.NotNull(order.FindProperty("order_source"));
        Assert.NotNull(order.FindProperty("source_order_no"));
        var stock = db.Model.FindEntityType(typeof(SnowmeetApi.Models.Fnb.FnbMaterialBatchStock));
        Assert.NotNull(stock);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, stock.FindProperty("batch_id")!.ValueGenerated);
    }
}

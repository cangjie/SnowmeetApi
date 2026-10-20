using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SnowmeetApi.Services.Qisemi;

namespace SnowmeetApi.Tests;

public sealed class QisemiSnapshotFactAttribute : FactAttribute
{
    public QisemiSnapshotFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNOWMEET_QISEMI_SNAPSHOT_TEST_SQLSERVER")))
            Skip = "仅在已还原完整快照的本机 LocalDB 临时库执行";
    }
}

[Collection("Qisemi SQL Server")]
public class QisemiSnapshotTests
{
    private sealed class Factory : IQisemiConnectionFactory
    {
        public DbConnection Create()
        {
            string value = Environment.GetEnvironmentVariable("SNOWMEET_QISEMI_SNAPSHOT_TEST_SQLSERVER")!;
            var b = new SqlConnectionStringBuilder(value);
            if (!b.DataSource.Equals(@"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)
                || !b.InitialCatalog.StartsWith("snowmeet_qsm_api_snapshot_", StringComparison.Ordinal) || !b.IntegratedSecurity)
                throw new InvalidOperationException("完整快照测试只允许本机临时 LocalDB");
            return new SqlConnection(value);
        }
    }
    private sealed class NoFileIo : IQisemiFileReader
    {
        public Task<QisemiFileHandle> OpenAsync(string provider, string objectKey, string name, string? mime, CancellationToken ct)
            => throw new InvalidOperationException("完整快照查询测试不访问文件存储");
    }
    private static async Task<JsonElement> Read(string op, QisemiQuery? q = null, long? id = null, string? key = null)
    {
        q ??= new(); q.SourceId = 1; q.PageSize = 1;
        var service = new QisemiReadService(new Factory(), new NoFileIo());
        try{return JsonSerializer.SerializeToElement(await service.ReadAsync(op, q, id, key, default), new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("result");}
        catch(Exception ex){throw new InvalidOperationException("完整快照操作失败: "+op+" "+key,ex);}
    }
    [QisemiSnapshotFact]
    public async Task EveryImportedModuleNativeSheetAndApprovedNavigationEntryCanBeRead()
    {
        var navigation = await Read("modules"); Assert.Equal(50, navigation.GetArrayLength());
        var snapshot = await Read("snapshot");
        foreach (var module in snapshot.GetProperty("modules").EnumerateArray())
        {
            string name = module.GetProperty("moduleKey").GetString()!;
            JsonElement records;
            try { records = await Read("records", new() { Module = name }); }
            catch (Exception ex) { throw new InvalidOperationException("读取完整模块失败: " + name, ex); }
            Assert.True(module.GetProperty("actualCount").GetString() == records.GetProperty("total").GetString(), "模块数量不一致: " + name);
        }
        foreach (var entry in QisemiCatalog.Modules.Where(m => m.DocumentType != null && m.Kind is "document" or "finance"))
            await Read("documents", new() { Module = entry.Key });
        foreach (var name in new[] { "products_all", "accessories", "product_classes_tree", "units", "warehouses", "branches", "warehouse_positions", "clients", "client_classes", "suppliers", "supplier_classes", "accounts_all", "projects", "employees_all", "roles" })
            await Read("archives", key: name);
        foreach (var op in new[] { "inventory", "warehouses", "movements", "salesLines", "stockTotals", "warehouseStocks", "files", "sheets", "versions", "issues", "batches" }) await Read(op);
    }
    [QisemiSnapshotFact]
    public async Task DefaultThirtyStylePageReturnsStockWithoutAggregatingTheWholeCatalogFirst()
    {
        var service=new QisemiReadService(new Factory(),new NoFileIo());
        var data=JsonSerializer.SerializeToElement(await service.ReadAsync("inventory",new(){SourceId=1,PageSize=30},null,null,default),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var items=data.GetProperty("result").GetProperty("items");Assert.InRange(items.GetArrayLength(),1,30);
        Assert.All(items.EnumerateArray(),item=>Assert.True(item.TryGetProperty("quantityMain",out _)));
    }
    [QisemiSnapshotFact]
    public async Task InStockFirstPageAndWarehouseSelectionReadTheCompleteSnapshot()
    {
        var all = await Read("inventory");
        var stocked = await Read("inventory", new() { InStockOnly = true });
        Assert.InRange(long.Parse(stocked.GetProperty("total").GetString()!), 1, long.Parse(all.GetProperty("total").GetString()!) - 1);
        var warehouses = await Read("warehouses");
        long flagship = long.Parse(warehouses.EnumerateArray().Single(w => w.GetProperty("name").GetString() == "【旗舰】零售商品仓").GetProperty("id").GetString()!);
        var scoped = await Read("inventory", new() { WarehouseId = flagship, InStockOnly = true });
        Assert.NotEmpty(scoped.GetProperty("items").EnumerateArray());
        long styleId = long.Parse(scoped.GetProperty("items")[0].GetProperty("id").GetString()!);
        var detail = await Read("style", new() { WarehouseId = flagship, IncludeZero = true }, styleId);
        Assert.Contains(detail.GetProperty("sizes").EnumerateArray(), size => size.GetProperty("warehouses").EnumerateArray().Any(cell => cell.GetProperty("quantityMain").ValueKind == JsonValueKind.Number && cell.GetProperty("quantityMain").GetDecimal() > 0));
        Assert.All(detail.GetProperty("sizes").EnumerateArray(), size => Assert.Equal(QisemiRules.Id(flagship), Assert.Single(size.GetProperty("warehouses").EnumerateArray()).GetProperty("warehouseId").GetString()));
        var categories = await Read("topCategories");
        var root = categories.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "【硬件】双板鞋");
        var boots = await Read("inventory", new() { TopCategoryId = long.Parse(root.GetProperty("id").GetString()!), InStockOnly = true, WarehouseId = flagship, Keyword = "Fischer 110 26.5" });
        Assert.NotEmpty(boots.GetProperty("items").EnumerateArray());
    }
    [QisemiSnapshotFact]
    public async Task MostRecentDocumentDetailRetainsItsRawRecordAssociation()
    {
        var docs = await Read("documents");
        long id = long.Parse(docs.GetProperty("items")[0].GetProperty("id").GetString()!);
        var detail = await Read("document", id: id);
        Assert.Equal(QisemiRules.Id(id), detail.GetProperty("document").GetProperty("documentId").GetString());
        Assert.Contains(detail.GetProperty("raw").GetProperty("documents").EnumerateArray(),
            row => row.GetProperty("documentId").GetString() == QisemiRules.Id(id));
    }
    [QisemiSnapshotFact]
    public async Task RealBootStyleAndCurrentDocumentDetailsRetainTheirAssociations()
    {
        var boots = await Read("inventory", new() { Keyword = "Fischer RC4 110 MV BOA VAC 26.5" });
        Assert.NotEmpty(boots.GetProperty("items").EnumerateArray());
        long styleId = long.Parse(boots.GetProperty("items")[0].GetProperty("id").GetString()!);
        var style = await Read("style", new() { IncludeZero = true }, styleId);
        Assert.Contains(style.GetProperty("sizes").EnumerateArray(), x => x.GetProperty("sizeLabel").GetString() == "26.5");
        Assert.True(style.GetProperty("sizes").GetArrayLength() > 1);
        var docs = await Read("documents", new() { Module = "sales" });
        long id = long.Parse(docs.GetProperty("items")[0].GetProperty("id").GetString()!);
        await Read("document", id: id);
        foreach (string section in new[] { "lines", "settlements", "writeoffs", "relations", "movements", "versions", "files", "raw" }) await Read("documentSection", id: id, key: section);
        var sheets = await Read("sheets");
        long sheetId = long.Parse(sheets.GetProperty("items")[0].GetProperty("id").GetString()!);
        await Read("sheetRows", id: sheetId);
    }
}

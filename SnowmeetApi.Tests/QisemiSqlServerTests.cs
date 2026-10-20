using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Services.Qisemi;

namespace SnowmeetApi.Tests;

public sealed class QisemiLocalDbFactAttribute : FactAttribute
{
    public QisemiLocalDbFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "需要 Windows SQL Server LocalDB";
    }
}

[Collection("Qisemi SQL Server")]
public class QisemiSqlServerTests
{
    private sealed class Factory(string connection) : IQisemiConnectionFactory { public DbConnection Create() => new SqlConnection(connection); }
    private sealed class FileProbe : IQisemiFileReader
    {
        public int OpenCount { get; private set; }
        public Task<QisemiFileHandle> OpenAsync(string provider, string key, string name, string? mime, CancellationToken ct)
        {
            OpenCount++; Assert.StartsWith("private/qisemi/datasets/", key);
            return Task.FromResult(new QisemiFileHandle(new MemoryStream([1, 2, 3]), name, "image/png", true));
        }
    }
    // A new disposable database per scenario; neither config.sqlServer nor production Startup is read.
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string database = "snowmeet_qsm_api_test_" + Guid.NewGuid().ToString("N");
        private const string Master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;Encrypt=false;TrustServerCertificate=true";
        public string Connection => new SqlConnectionStringBuilder(Master) { InitialCatalog = database }.ConnectionString;
        public FileProbe Files { get; } = new();
        public IQisemiReadService Service => new QisemiReadService(new Factory(Connection), Files);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await using var master = new SqlConnection(Master); await master.OpenAsync();
            await using (var check = new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('IsLocalDB'))", master))
                Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync()));
            await using (var cmd = new SqlCommand($"CREATE DATABASE [{f.database}] COLLATE Chinese_PRC_CI_AS", master)) await cmd.ExecuteNonQueryAsync();
            try
            {
                string? schema = Environment.GetEnvironmentVariable("QISEMI_TEST_SCHEMA");
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); schema == null && dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "snowmeet_ai_doc", "sql", "2026-10-19_qisemi_schema.sql");
                    if (File.Exists(candidate)) schema = candidate;
                }
                Assert.True(schema != null, "需要相邻 snowmeet_ai_doc 仓库的已批准 DDL，或 QISEMI_TEST_SCHEMA 路径");
                await f.Exec("CREATE TABLE dbo.product(id int PRIMARY KEY); CREATE TABLE dbo.retail(id int PRIMARY KEY); CREATE TABLE dbo.shop_list(id int PRIMARY KEY)");
                await f.Exec(await File.ReadAllTextAsync(schema!)); await f.Seed(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public async Task Exec(string text, params (string, object)[] parameters)
        {
            await using var c = new SqlConnection(Connection); await c.OpenAsync(); using var command = new SqlCommand(text, c) { CommandTimeout = 90 };
            foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
            await command.ExecuteNonQueryAsync();
        }
        public async Task<JsonElement> Read(string op, QisemiQuery? query = null, long? id = null, string? key = null)
        {
            var q = query ?? new QisemiQuery(); q.SourceId ??= 1;
            return JsonSerializer.SerializeToElement(await Service.ReadAsync(op, q, id, key, default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var c = new SqlConnection(Master); await c.OpenAsync();
            // The target name is generated here and cannot come from an environment variable or production config.
            using var cmd = new SqlCommand($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END", c);
            await cmd.ExecuteNonQueryAsync();
        }
        public Task Delete(string table, long id, bool deleted) => Exec($"UPDATE dbo.qsm_{table} SET valid=@v,deleted_at=CASE WHEN @v=0 THEN SYSUTCDATETIME() END,deleted_by=CASE WHEN @v=0 THEN N'test' END,delete_reason=CASE WHEN @v=0 THEN N'test' END WHERE id=@id", ("@v", !deleted), ("@id", id));
        private async Task Raw(int id, int module, string payload = "{}", int ordinal = 1, int? sheet = null)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) gz.Write(Encoding.Unicode.GetBytes(payload));
            await Exec("SET IDENTITY_INSERT dbo.qsm_raw_record ON; INSERT dbo.qsm_raw_record(id,source_id,batch_id,module_id,row_ordinal,payload_gzip,payload_hash,sheet_id) VALUES (@id,1,1,@m,@o,@p,HASHBYTES('SHA2_256',@p),@sheet); SET IDENTITY_INSERT dbo.qsm_raw_record OFF;",
                ("@id", id), ("@m", module), ("@o", ordinal), ("@p", ms.ToArray()), ("@sheet", (object?)sheet ?? DBNull.Value));
        }
        private async Task Seed()
        {
            await Exec("SET IDENTITY_INSERT dbo.qsm_source ON; INSERT dbo.qsm_source(id,company_key,book_key,display_name) VALUES(1,N'test',N'book',N'测试账套'),(2,N'other',N'book',N'其他账套'); SET IDENTITY_INSERT dbo.qsm_source OFF;");
            await Exec("SET IDENTITY_INSERT dbo.qsm_import_batch ON; INSERT dbo.qsm_import_batch(id,source_id,batch_key,scope,dataset_hash,parser_version,capture_started_at,capture_finished_at,published_at,state,verification_passed,manifest_json,verification_json) VALUES(1,1,NEWID(),'full',HASHBYTES('SHA2_256','test'),'test','2026-10-18','2026-10-18T05:27:45','2026-10-19','published',1,N'{}',N'{}'),(2,1,NEWID(),'full',HASHBYTES('SHA2_256','staging'),'test','2026-10-19','2026-10-19',NULL,'validated',1,N'{}',N'{}'); SET IDENTITY_INSERT dbo.qsm_import_batch OFF; UPDATE dbo.qsm_source SET current_full_batch_id=1,current_inventory_batch_id=1 WHERE id=1;");
            string[] names = ["products_all", "units", "warehouses", "stock", "stock_by_warehouse", "sales", "sales_history_details", "sales_multi_account_details", "stock_movements", "sales_line_items", "media/index.jsonl", "native/sales_with_details.1"];
            for (int n = 0; n < names.Length; n++)
            {
                string coverage = n == 4 ? "{\"warehouses_result\":[{\"warehouse_id\":\"w1\",\"state\":\"complete\",\"rows\":1,\"expected\":1},{\"warehouse_id\":\"w2\",\"state\":\"complete\",\"rows\":1,\"expected\":1}]}" : "{}";
                await Exec("SET IDENTITY_INSERT dbo.qsm_import_module ON; INSERT dbo.qsm_import_module(id,source_id,batch_id,module_key,carrier,state,actual_count,coverage_json) VALUES(@id,1,1,@key,@carrier,'complete',1,@coverage); SET IDENTITY_INSERT dbo.qsm_import_module OFF;",
                    ("@id", n + 1), ("@key", names[n]), ("@carrier", n == 10 ? "media" : n == 11 ? "native_sheet" : "api"), ("@coverage", coverage));
            }
            await Raw(100, 1, "{\"ProductName\":\"Fischer 110 MV 25.5\"}"); await Raw(101, 1, "{}", 2); await Raw(102, 1, "{}", 3); await Raw(103, 1, "{}", 4);
            await Raw(110, 2); await Raw(120, 3); await Raw(121, 3, "{}", 2); await Raw(122, 3, "{}", 3);
            await Raw(130, 4); await Raw(131, 5); await Raw(132, 5, "{}", 2);
            await Raw(200, 6, "{\"SaleNo\":\"TEST-0001\",\"SaleDate\":\"2020-01-01\"}");
            await Raw(201, 8, "{\"customArray\":[{\"text\":\"完整补充字段\"}]}"); await Raw(202, 7);
            await Raw(210, 9); await Raw(211, 9, "{}", 2); await Raw(212, 10); await Raw(230, 11); await Raw(231, 11, "{}", 2);
            await Exec("INSERT dbo.qsm_unit(source_id,source_key,name,last_seen_batch_id,last_raw_record_id) VALUES(1,N'u1',N'双',1,110); INSERT dbo.qsm_warehouse(source_id,source_key,name,last_seen_batch_id,last_raw_record_id) VALUES(1,N'w1',N'【旗舰】零售商品仓',1,120),(1,N'w2',N'测试负库存仓',1,121),(1,N'w3',N'未完整采集仓',1,122);");
            await Exec("SET IDENTITY_INSERT dbo.qsm_product_style ON; INSERT dbo.qsm_product_style(id,source_id,style_key,display_name,brand,model,group_method,search_text) VALUES(9007199254740993,1,NEWID(),N'Fischer RC4 110 MV BOA VAC',N'Fischer',N'RC4 110 MV BOA VAC','rule',N'Fischer U06424 RC4 110 MV BOA VAC'),(2,1,NEWID(),N'Fischer RC4 120 MV BOA VAC',N'Fischer',N'RC4 120 MV BOA VAC','singleton',N'Fischer U06425 RC4 120 MV BOA VAC'); SET IDENTITY_INSERT dbo.qsm_product_style OFF;");
            for (int n = 0; n < 4; n++)
            {
                string size = (25.5m + n).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Exec("SET IDENTITY_INSERT dbo.qsm_product_sku ON; INSERT dbo.qsm_product_sku(id,source_id,source_key,last_seen_batch_id,last_raw_record_id,name,code,size_label,size_sort,base_unit_id,search_text) VALUES(@id,1,@key,1,@raw,@name,@code,@size,@sort,1,@search); SET IDENTITY_INSERT dbo.qsm_product_sku OFF; INSERT dbo.qsm_style_sku(source_id,style_id,sku_id,assignment_method,evidence_json) VALUES(1,@style,@id,'rule',N'{}');",
                    ("@id", 11 + n), ("@key", "sku" + n), ("@raw", 100 + n), ("@name", "Fischer RC4 " + (n == 3 ? "120" : "110") + " MV BOA VAC " + size),
                    ("@code", "U06424-" + size), ("@size", size), ("@sort", 25.5m + n), ("@search", "Fischer U06424 " + size), ("@style", n == 3 ? 2L : 9007199254740993L));
                await Exec("INSERT dbo.qsm_stock_total(source_id,batch_id,raw_record_id,sku_id,quantity_main) VALUES(1,1,130,@sku,@qty)", ("@sku", 11 + n), ("@qty", n == 0 ? 1m : 0m));
            }
            await Exec("INSERT dbo.qsm_stock_snapshot(source_id,batch_id,raw_record_id,sku_id,warehouse_id,quantity_main) VALUES(1,1,131,11,1,2),(1,1,132,11,2,-1);");
            await Exec("INSERT dbo.qsm_document(source_id,source_key,last_seen_batch_id,last_raw_record_id,document_type,document_no,listing_state) VALUES(1,N'd1',1,200,'sale',N'TEST-0001','current'); INSERT dbo.qsm_document_version(source_id,batch_id,raw_record_id,document_id,revision_key,version_kind,document_no,business_at,amount,detail_coverage_json) VALUES(1,1,200,1,HASHBYTES('SHA2_256','current'),'current',N'TEST-0001','2020-01-01',100,N'{\"supplement_raw_id\":201,\"product_lines\":{\"state\":\"complete\",\"count\":1}}'),(1,1,202,1,HASHBYTES('SHA2_256','old'),'historical',N'TEST-0001','2019-12-31',90,N'{}'); UPDATE dbo.qsm_document SET current_version_id=1 WHERE id=1; INSERT dbo.qsm_document_line(source_id,batch_id,raw_record_id,document_version_id,line_key,line_no,line_kind,sku_id,product_name,quantity,match_state) VALUES(1,1,201,1,N'line1',1,'product',11,N'Fischer',1,'exact');");
            await Exec("INSERT dbo.qsm_stock_movement(source_id,batch_id,raw_record_id,row_ordinal,row_hash,duplicate_ordinal,sku_id,warehouse_id,document_id,document_version_id,document_no,business_at,in_quantity,out_quantity,match_state) VALUES(1,1,210,1,HASHBYTES('SHA2_256','same'),1,11,1,1,1,N'TEST-0001','2020-01-01',0,1,'exact'),(1,1,211,2,HASHBYTES('SHA2_256','same'),2,11,1,1,1,N'TEST-0001','2020-01-01',0,1,'exact'); INSERT dbo.qsm_sales_report_line(source_id,batch_id,raw_record_id,source_detail_key,source_business_type,document_id,document_version_id,sku_id,document_no,business_at,quantity,match_state) VALUES(1,1,212,N's1','3',1,1,11,N'TEST-0001','2020-01-01',1,'exact');");
            await Exec("INSERT dbo.qsm_file(source_id,content_sha256,byte_length,mime_type,extension,storage_provider,object_key,first_seen_batch_id,availability_state) VALUES(1,HASHBYTES('SHA2_256','file'),3,N'image/png',N'.png','s3',N'private/qisemi/datasets/test/file.png',1,'ready'); INSERT dbo.qsm_file_ref(source_id,batch_id,raw_record_id,reference_key,file_id,sku_id,reference_kind,original_name,download_state) VALUES(1,1,230,N'image',1,11,'product_image',N'雪鞋.png','complete'); INSERT dbo.qsm_file_ref(source_id,batch_id,raw_record_id,reference_key,file_id,document_version_id,reference_kind,original_name,download_state) VALUES(1,1,231,N'document',1,1,'attachment',N'单据.png','complete'); INSERT dbo.qsm_export_sheet(source_id,batch_id,module_id,file_id,export_key,sheet_index,sheet_name,sheet_role,headers_json,row_count,binding_state) VALUES(1,1,12,1,N'sales_with_details',1,N'总表','header',N'[\"单号\",\"商品\"]',1,'unresolved');");
            await Raw(220, 12, "{\"单号\":\"TEST-0001\",\"商品\":\"Fischer\"}", sheet: 1);
        }
    }

    [QisemiLocalDbFact]
    public async Task SizeKeywordGroupsCorrectStyleAndKeepsZeroAndNegativeSizesAndUnknownCoverage()
    {
        await using var f = await Fixture.Create();
        var result = (await f.Read("inventory", new() { Keyword = "Fischer 110 26.5" })).GetProperty("result");
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal("9007199254740993", item.GetProperty("id").GetString());
        Assert.Equal("3", item.GetProperty("sizeCount").GetString()); Assert.Equal(1m, item.GetProperty("quantityMain").GetDecimal());
        var detail = (await f.Read("style", new() { IncludeZero = true }, 9007199254740993)).GetProperty("result");
        Assert.Equal("1",(await f.Read("files",new(){StyleId=9007199254740993})).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0",(await f.Read("files",new(){StyleId=2})).GetProperty("result").GetProperty("total").GetString());
        var sizes = detail.GetProperty("sizes").EnumerateArray().ToArray(); Assert.Equal(3, sizes.Length);
        var cells = sizes[0].GetProperty("warehouses").EnumerateArray().ToArray();
        Assert.Contains(cells, x => x.GetProperty("quantityMain").ValueKind == JsonValueKind.Number && x.GetProperty("quantityMain").GetDecimal() == -1m);
        Assert.Contains(cells, x => x.GetProperty("quantityState").GetString() == "unknown" && x.GetProperty("quantityMain").ValueKind == JsonValueKind.Null);
        Assert.All(sizes[1].GetProperty("warehouses").EnumerateArray().Where(x => x.GetProperty("quantityState").GetString() == "inferred_zero"), x => Assert.Equal(0m, x.GetProperty("quantityMain").GetDecimal()));
        var warehouse = (await f.Read("inventory", new() { WarehouseId = 2, Keyword = "110", NegativeOnly = true })).GetProperty("result");
        Assert.Equal(-1m, warehouse.GetProperty("items")[0].GetProperty("quantityMain").GetDecimal());
    }
    [QisemiLocalDbFact]
    public async Task InStockFilterPrecedesPagingAndUsesWholeStyleWithinSelectedWarehouse()
    {
        await using var f = await Fixture.Create();
        async Task<JsonElement> Inventory(QisemiQuery q) => (await f.Read("inventory", q)).GetProperty("result");
        Assert.Equal("2", (await Inventory(new())).GetProperty("total").GetString());
        var stocked = await Inventory(new() { InStockOnly = true, Keyword = "110 26.5" });
        var style = Assert.Single(stocked.GetProperty("items").EnumerateArray());
        Assert.Equal("9007199254740993", style.GetProperty("id").GetString());
        Assert.Equal("3", style.GetProperty("sizeCount").GetString());
        Assert.Equal(1m, style.GetProperty("quantityMain").GetDecimal());
        var flagship = await Inventory(new() { InStockOnly = true, WarehouseId = 1 });
        Assert.Equal("1", flagship.GetProperty("total").GetString());
        Assert.Equal(2m, flagship.GetProperty("items")[0].GetProperty("quantityMain").GetDecimal());
        foreach (long warehouse in new long[] { 2, 3 })
            Assert.Equal("0", (await Inventory(new() { InStockOnly = true, WarehouseId = warehouse })).GetProperty("total").GetString());
        var negative = await Inventory(new() { WarehouseId = 2 });
        Assert.Equal("2", negative.GetProperty("total").GetString());
        Assert.Contains(negative.GetProperty("items").EnumerateArray(), row => row.GetProperty("quantityMain").GetDecimal() == -1m);
        var sizes = (await f.Read("style", new() { WarehouseId = 1, IncludeZero = true }, 9007199254740993)).GetProperty("result").GetProperty("sizes");
        Assert.Equal(3, sizes.GetArrayLength());
        Assert.All(sizes.EnumerateArray(), size => Assert.Equal("1", Assert.Single(size.GetProperty("warehouses").EnumerateArray()).GetProperty("warehouseId").GetString()));
        await f.Exec("UPDATE dbo.qsm_stock_total SET quantity_main=3 WHERE sku_id=14");
        var second = await Inventory(new() { InStockOnly = true, PageSize = 1, Page = 2 });
        Assert.Equal("2", second.GetProperty("total").GetString());
        Assert.Single(second.GetProperty("items").EnumerateArray());
        await f.Delete("raw_record", 130, true);
        Assert.Equal("0", (await Inventory(new() { InStockOnly = true })).GetProperty("total").GetString());
        await f.Delete("raw_record", 131, true);
        Assert.Equal("0", (await Inventory(new() { InStockOnly = true, WarehouseId = 1 })).GetProperty("total").GetString());
        await f.Delete("raw_record", 131, false);
        await f.Delete("product_sku", 11, true);
        Assert.Equal("0", (await Inventory(new() { InStockOnly = true, WarehouseId = 1 })).GetProperty("total").GetString());
    }
    [QisemiLocalDbFact]
    public async Task ResultWarehouseFacetsCountWholeStylesAndKeepCategoryPagingAndVisibility()
    {
        await using var f = await Fixture.Create();
        async Task<JsonElement> Inventory(QisemiQuery q) => (await f.Read("inventory", q)).GetProperty("result");
        var initial = await Inventory(new() { Keyword = "Fischer", PageSize = 1 });
        var firstWarehouse = Assert.Single(initial.GetProperty("warehouseFacets").EnumerateArray());
        Assert.Equal("1", firstWarehouse.GetProperty("id").GetString());
        Assert.Equal("1", firstWarehouse.GetProperty("count").GetString());
        Assert.Equal("2", initial.GetProperty("warehouseUnfilteredTotal").GetString());
        var size = await Inventory(new() { Keyword = "26.5", ResultWarehouseId = 1 });
        var style = Assert.Single(size.GetProperty("items").EnumerateArray());
        Assert.Equal("3", style.GetProperty("sizeCount").GetString());
        Assert.Equal(2m, style.GetProperty("quantityMain").GetDecimal());
        Assert.Equal("0", (await Inventory(new() { ResultWarehouseId = 2 })).GetProperty("total").GetString());
        await f.Exec("INSERT dbo.qsm_stock_snapshot(source_id,batch_id,raw_record_id,sku_id,warehouse_id,quantity_main) VALUES(1,1,132,12,2,1),(1,1,132,14,2,5); INSERT dbo.qsm_category(source_id,source_key,last_seen_batch_id,last_raw_record_id,category_kind,parent_id,name) VALUES(1,N'boots',1,100,'product',NULL,N'雪鞋'),(1,N'helmet',1,100,'product',NULL,N'头盔'); UPDATE dbo.qsm_product_sku SET category_id=CASE WHEN id=14 THEN 2 ELSE 1 END;");
        var both = await Inventory(new() { PageSize = 1, ResultWarehouseId = 2, SortBy = "topCategory" });
        Assert.Equal("2", both.GetProperty("total").GetString());
        Assert.Equal(2, both.GetProperty("warehouseFacets").GetArrayLength());
        Assert.Equal("2", both.GetProperty("warehouseFacets").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "2").GetProperty("count").GetString());
        Assert.Equal(2, both.GetProperty("categoryFacets").GetArrayLength());
        var pageTwo = await Inventory(new() { PageSize = 1, Page = 2, ResultWarehouseId = 2, SortBy = "topCategory" });
        Assert.Equal(both.GetProperty("warehouseFacets").GetRawText(), pageTwo.GetProperty("warehouseFacets").GetRawText());
        var cancelingSizes = await Inventory(new() { ResultWarehouseId = 2, Keyword = "26.5" });
        Assert.Equal(0m, Assert.Single(cancelingSizes.GetProperty("items").EnumerateArray()).GetProperty("quantityMain").GetDecimal());
        var helmet = await Inventory(new() { ResultCategoryId = 2 });
        var helmetWarehouse = Assert.Single(helmet.GetProperty("warehouseFacets").EnumerateArray());
        Assert.Equal("2", helmetWarehouse.GetProperty("id").GetString());
        Assert.Equal("1", helmetWarehouse.GetProperty("count").GetString());
        Assert.Equal("1", helmet.GetProperty("warehouseUnfilteredTotal").GetString());
        var noHelmet = await Inventory(new() { ResultCategoryId = 2, ResultWarehouseId = 1 });
        Assert.Equal("0", noHelmet.GetProperty("total").GetString());
        Assert.Equal("2", Assert.Single(noHelmet.GetProperty("warehouseFacets").EnumerateArray()).GetProperty("id").GetString());
        var scoped = await Inventory(new() { WarehouseId = 1, InStockOnly = true });
        Assert.Equal("1", Assert.Single(scoped.GetProperty("warehouseFacets").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(400, (await Assert.ThrowsAsync<QisemiReadException>(() => Inventory(new() { WarehouseId = 1, ResultWarehouseId = 2 }))).Status);
        await f.Delete("warehouse", 1, true);
        Assert.Equal(404, (await Assert.ThrowsAsync<QisemiReadException>(() => Inventory(new() { ResultWarehouseId = 1 }))).Status);
        Assert.Equal("2", Assert.Single((await Inventory(new())).GetProperty("warehouseFacets").EnumerateArray()).GetProperty("id").GetString());
        await f.Delete("raw_record", 132, true);
        Assert.Empty((await Inventory(new())).GetProperty("warehouseFacets").EnumerateArray());
    }
    [QisemiLocalDbFact]
    public async Task InventorySortAndResultCategoryFacetsApplyBeforePaging()
    {
        await using var f = await Fixture.Create();
        await f.Exec("INSERT dbo.qsm_category(source_id,source_key,last_seen_batch_id,last_raw_record_id,category_kind,parent_id,name) VALUES(1,N'boots',1,100,'product',NULL,N'Z 雪鞋'),(1,N'helmet',1,100,'product',NULL,N'A 头盔'); INSERT dbo.qsm_category(source_id,source_key,last_seen_batch_id,last_raw_record_id,category_kind,parent_id,name) VALUES(1,N'child',1,100,'product',1,N'子分类'); UPDATE dbo.qsm_product_sku SET category_id=CASE WHEN id=14 THEN 2 ELSE 3 END;");
        async Task<JsonElement> Inventory(QisemiQuery q) => (await f.Read("inventory", q)).GetProperty("result");
        string FirstId(JsonElement result) => result.GetProperty("items")[0].GetProperty("id").GetString()!;
        var first = await Inventory(new() { Keyword = "Fischer", PageSize = 1 });
        Assert.Equal("9007199254740993", FirstId(first));
        Assert.Equal("2", first.GetProperty("total").GetString());
        Assert.Equal(2, first.GetProperty("categoryFacets").GetArrayLength());
        Assert.All(first.GetProperty("categoryFacets").EnumerateArray(), c => Assert.Equal("1", c.GetProperty("count").GetString()));
        Assert.Equal("2", FirstId(await Inventory(new() { Keyword = "Fischer", PageSize = 1, SortDirection = "desc" })));
        Assert.Equal("2", FirstId(await Inventory(new() { Keyword = "Fischer", PageSize = 1, SortBy = "topCategory" })));
        Assert.Equal("9007199254740993", FirstId(await Inventory(new() { PageSize = 1, SortBy = "topCategory", SortDirection = "desc" })));
        var secondPage = await Inventory(new() { PageSize = 1, Page = 2, SortBy = "topCategory" });
        Assert.Equal("9007199254740993", FirstId(secondPage));
        Assert.Equal(first.GetProperty("categoryFacets").GetRawText(), secondPage.GetProperty("categoryFacets").GetRawText());
        var secondary = await Inventory(new() { ResultCategoryId = 2, PageSize = 1 });
        Assert.Equal("2", FirstId(secondary));
        Assert.Equal("1", secondary.GetProperty("total").GetString());
        Assert.Equal("2", secondary.GetProperty("unfilteredTotal").GetString());
        Assert.Equal(2, secondary.GetProperty("categoryFacets").GetArrayLength());
        var primary = await Inventory(new() { TopCategoryId = 1, ResultCategoryId = 2 });
        Assert.Equal("0", primary.GetProperty("total").GetString());
        Assert.Equal("1", Assert.Single(primary.GetProperty("categoryFacets").EnumerateArray()).GetProperty("count").GetString());
        var stocked = await Inventory(new() { WarehouseId = 1, InStockOnly = true });
        Assert.Equal("1", Assert.Single(stocked.GetProperty("categoryFacets").EnumerateArray()).GetProperty("id").GetString());
        await f.Exec("UPDATE dbo.qsm_product_sku SET category_id=2 WHERE id=12");
        var mixed = await Inventory(new());
        Assert.Equal("2", mixed.GetProperty("unfilteredTotal").GetString());
        Assert.Equal("2", mixed.GetProperty("categoryFacets")[0].GetProperty("count").GetString());
        var sameSku = await Inventory(new() { Keyword = "26.5", ResultCategoryId = 1 });
        Assert.Equal("0", sameSku.GetProperty("total").GetString());
        Assert.Equal("2", Assert.Single(sameSku.GetProperty("categoryFacets").EnumerateArray()).GetProperty("id").GetString());
        await f.Exec("UPDATE dbo.qsm_product_sku SET category_id=NULL WHERE id=14");
        var unknown = await Inventory(new() { ResultCategoryId = 0, SortBy = "topCategory", SortDirection = "desc" });
        Assert.Equal("2", FirstId(unknown));
        Assert.Equal("未分类", unknown.GetProperty("categoryFacets").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "0").GetProperty("name").GetString());
        Assert.Equal(404, (await Assert.ThrowsAsync<QisemiReadException>(() => Inventory(new() { ResultCategoryId = 3 }))).Status);
    }
    [QisemiLocalDbFact]
    public async Task TopCategoryIncludesDescendantsAndCombinesWithStockWarehouseAndKeyword()
    {
        await using var f = await Fixture.Create();
        await f.Exec("INSERT dbo.qsm_category(source_id,source_key,last_seen_batch_id,last_raw_record_id,category_kind,parent_id,name) VALUES(1,N'boots',1,100,'product',NULL,N'雪鞋'); INSERT dbo.qsm_category(source_id,source_key,last_seen_batch_id,last_raw_record_id,category_kind,parent_id,name) VALUES(1,N'fischer',1,100,'product',1,N'Fischer'); INSERT dbo.qsm_category(source_id,source_key,last_seen_batch_id,last_raw_record_id,category_kind,parent_id,name) VALUES(1,N'season',1,100,'product',2,N'24-25'),(1,N'helmet',1,100,'product',NULL,N'头盔'),(1,N'empty',1,100,'product',NULL,N'空分类'); UPDATE dbo.qsm_product_sku SET category_id=CASE WHEN id=14 THEN 4 ELSE 3 END;");
        var roots = (await f.Read("topCategories")).GetProperty("result");
        Assert.Equal(3, roots.GetArrayLength()); Assert.All(roots.EnumerateArray(), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("parentId").ValueKind));
        var scoped = (await f.Read("inventory", new() { TopCategoryId = 1, InStockOnly = true, WarehouseId = 1, Keyword = "26.5" })).GetProperty("result");
        Assert.Equal("1", scoped.GetProperty("total").GetString());
        Assert.Equal("3", Assert.Single(scoped.GetProperty("items").EnumerateArray()).GetProperty("sizeCount").GetString());
        var path = Assert.Single(scoped.GetProperty("items")[0].GetProperty("categoryPaths").EnumerateArray());
        Assert.True(path.GetProperty("complete").GetBoolean());
        Assert.Equal(new[] { "雪鞋", "Fischer", "24-25" }, path.GetProperty("levels").EnumerateArray().Select(level => level.GetProperty("name").GetString()));
        Assert.Equal("1", (await f.Read("inventory", new() { Keyword = "雪 fisch 24- 110 26.5", InStockOnly = true, WarehouseId = 1, TopCategoryId = 1 })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0", (await f.Read("inventory", new() { Keyword = "24-%" })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0", (await f.Read("inventory", new() { TopCategoryId = 4, InStockOnly = true })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("1", (await f.Read("inventory", new() { TopCategoryId = 4 })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0", (await f.Read("inventory", new() { TopCategoryId = 5 })).GetProperty("result").GetProperty("total").GetString());
        await f.Exec("UPDATE dbo.qsm_product_sku SET category_id=4 WHERE id=12");
        Assert.Equal("0", (await f.Read("inventory", new() { TopCategoryId = 1, Keyword = "26.5", InStockOnly = true })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("1", (await f.Read("inventory", new() { TopCategoryId = 4, Keyword = "26.5", InStockOnly = true })).GetProperty("result").GetProperty("total").GetString());
        var mixed = (await f.Read("inventory", new() { TopCategoryId = 4, Keyword = "26.5", InStockOnly = true })).GetProperty("result").GetProperty("items")[0];
        Assert.Equal(2, mixed.GetProperty("categoryPaths").GetArrayLength());
        await f.Exec("UPDATE dbo.qsm_product_sku SET search_text=CASE WHEN id=11 THEN N'UniqueAlpha' WHEN id=12 THEN N'UniqueBeta' ELSE search_text END");
        Assert.Equal("0", (await f.Read("inventory", new() { Keyword = "UniqueAlpha UniqueBeta" })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0", (await f.Read("inventory", new() { Keyword = "UniqueAlpha 头盔" })).GetProperty("result").GetProperty("total").GetString());
        await f.Exec("UPDATE dbo.qsm_product_sku SET category_id=3 WHERE id=12");
        Assert.Equal(404, (await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("inventory", new() { TopCategoryId = 2 }))).Status);
        await f.Delete("category", 2, true);
        Assert.Equal("0", (await f.Read("inventory", new() { TopCategoryId = 1 })).GetProperty("result").GetProperty("total").GetString());
        var partial = Assert.Single((await f.Read("inventory", new() { Keyword = "110" })).GetProperty("result").GetProperty("items")[0].GetProperty("categoryPaths").EnumerateArray());
        Assert.False(partial.GetProperty("complete").GetBoolean());
        Assert.Equal("24-25", Assert.Single(partial.GetProperty("levels").EnumerateArray()).GetProperty("name").GetString());
        await f.Delete("category", 1, true);
        Assert.Equal(2, (await f.Read("topCategories")).GetProperty("result").GetArrayLength());
        Assert.Equal(404, (await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("inventory", new() { TopCategoryId = 1 }))).Status);
    }
    [QisemiLocalDbFact]
    public async Task DocumentsRawSupplementsNativeRowsAndHistoricalVersionsStaySeparate()
    {
        await using var f = await Fixture.Create();
        var current = (await f.Read("documents", new() { Module = "sales", DateFrom = new(2019, 1, 1), DateTo = new(2020, 1, 1) })).GetProperty("result");
        Assert.Equal("1", current.GetProperty("total").GetString());
        await f.Exec("UPDATE dbo.qsm_document_version SET write_back=1 WHERE id=1");
        Assert.Equal("1",(await f.Read("documents",new(){WriteBack=true})).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0",(await f.Read("documents",new(){WriteBack=false})).GetProperty("result").GetProperty("total").GetString());
        var doc = (await f.Read("document", id: 1)).GetProperty("result"); Assert.Equal("current", doc.GetProperty("document").GetProperty("versionKind").GetString());
        var lines = (await f.Read("documentSection", id: 1, key: "lines")).GetProperty("result"); Assert.Equal("1", lines.GetProperty("total").GetString());
        var raw = (await f.Read("documentSection", id: 1, key: "raw")).GetProperty("result"); Assert.Equal("2", raw.GetProperty("total").GetString());
        Assert.Contains(raw.GetProperty("items").EnumerateArray(), r => r.GetProperty("payloadJson").TryGetProperty("customArray", out _));
        var history = (await f.Read("documentSection", id: 1, key: "versions")).GetProperty("result"); Assert.Equal("2", history.GetProperty("total").GetString());
        var old = (await f.Read("version", id: 2)).GetProperty("result"); Assert.Equal("historical", old.GetProperty("document").GetProperty("versionKind").GetString());
        Assert.Equal("1", (await f.Read("versions", new() { Module = "sales_history_details" })).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("1", (await f.Read("sheetRows", id: 1)).GetProperty("result").GetProperty("rows").GetProperty("total").GetString());
        Assert.Equal("2", (await f.Read("movements")).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("1", (await f.Read("salesLines")).GetProperty("result").GetProperty("total").GetString());
    }
    [QisemiLocalDbFact]
    public async Task DeletedOwnersRawModulesAndFilesCannotBeReadThroughOtherRoutes()
    {
        await using var f = await Fixture.Create();
        await f.Delete("product_sku", 11, true);
        Assert.Equal("2", (await f.Read("style", id: 9007199254740993)).GetProperty("result").GetProperty("sizes").GetArrayLength().ToString());
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("record", id: 100));
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Service.FileAsync(1, new() { SourceId = 1 }, default));
        Assert.Equal(0, f.Files.OpenCount);
        await f.Delete("product_sku", 11, false); await f.Delete("file_ref", 1, true);
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Service.FileAsync(1, new() { SourceId = 1 }, default));
        await f.Delete("document_version", 1, true);
        Assert.Equal("0", (await f.Read("movements")).GetProperty("result").GetProperty("total").GetString());
        Assert.Equal("0", (await f.Read("salesLines")).GetProperty("result").GetProperty("total").GetString());
        await f.Delete("document_version", 1, false);
        await f.Delete("document", 1, true);
        Assert.Equal("0", (await f.Read("documents")).GetProperty("result").GetProperty("total").GetString());
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("version", id: 1));
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("record", id: 201));
        Assert.Equal("0", (await f.Read("movements")).GetProperty("result").GetProperty("total").GetString());
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Service.FileAsync(2, new() { SourceId = 1 }, default));
        await f.Delete("import_module", 1, true);
        Assert.Equal("0", (await f.Read("inventory")).GetProperty("result").GetProperty("total").GetString());
        await f.Delete("source", 1, true);
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("modules"));
    }
    [QisemiLocalDbFact]
    public async Task SnapshotPinsSourceIsolationAndLiteralSearchAreEnforced()
    {
        await using var f = await Fixture.Create();
        var error = await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("inventory", new() { BatchId = 2 })); Assert.Equal(409, error.Status);
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("document", new() { SourceId = 2 }, 1));
        Assert.Equal("0", (await f.Read("inventory", new() { Keyword = "%_[' OR 1=1 --" })).GetProperty("result").GetProperty("total").GetString());
        var mod = (await f.Read("modules")).GetProperty("result"); Assert.Equal(50, mod.GetArrayLength());
        Assert.Equal("0", (await f.Read("archives", key: "accessories")).GetProperty("result").GetProperty("total").GetString());
        await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("archives", key: "product_sku; DROP TABLE qsm_source"));
        await f.Delete("stock_snapshot", 1, true);
        var cell = (await f.Read("style", new() { IncludeZero = true, WarehouseId = 1 }, 9007199254740993)).GetProperty("result").GetProperty("sizes")[0].GetProperty("warehouses")[0];
        Assert.Equal("unknown", cell.GetProperty("quantityState").GetString());
        await f.Delete("import_batch", 1, true); await Assert.ThrowsAsync<QisemiReadException>(() => f.Read("inventory"));
    }
    [QisemiLocalDbFact]
    public async Task RealHttpPipelineReadsSqlServerAndValidatesFileReferencesBeforeOpeningStorage()
    {
        await using var f = await Fixture.Create();
        using var server = QisemiHttpContractTests.Host(s =>
        {
            s.AddSingleton<IQisemiConnectionFactory>(new Factory(f.Connection)); s.AddSingleton<IQisemiFileReader>(f.Files);
            s.AddScoped<IQisemiReadService, QisemiReadService>();
        });
        using var client = server.CreateClient();
        foreach (string path in new[] { "Sources", "Snapshot", "Modules", "Warehouses", "Inventory", "StockTotals", "WarehouseStocks", "StockMovements", "SalesLines", "Documents", "Documents/1", "Versions", "Versions/2", "Skus/11", "Files", "ExportSheets", "ExportSheets/1/Rows", "Records/100", "Records", "Modules/products_all/Records", "DataIssues", "Batches", "Archives/products_all/11" })
        {
            var probe = await client.GetAsync("/api/Qisemi/" + path + "?sourceId=1");
            Assert.True(probe.StatusCode == HttpStatusCode.OK, path + ": " + await probe.Content.ReadAsStringAsync());
        }
        foreach (string archive in new[] { "products_all", "accessories", "product_classes_tree", "units", "warehouses", "branches", "warehouse_positions", "clients", "client_classes", "suppliers", "supplier_classes", "accounts_all", "projects", "employees_all", "roles" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Qisemi/Archives/" + archive + "?sourceId=1")).StatusCode);
        foreach (string section in new[] { "lines", "settlements", "writeoffs", "relations", "movements", "versions", "files", "raw" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Qisemi/Documents/1/Sections/" + section + "?sourceId=1")).StatusCode);
        var response = await client.GetAsync("/api/Qisemi/Inventory?sourceId=1&keyword=Fischer%20110%2026.5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("9007199254740993", json.RootElement.GetProperty("data").GetProperty("result").GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Qisemi/FileReferences/1/Content?sourceId=1&batchId=1")).StatusCode);
        Assert.Equal(1, f.Files.OpenCount);
        await f.Delete("file", 1, true);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/Qisemi/FileReferences/1/Content?sourceId=1&batchId=1")).StatusCode);
        Assert.Equal(1, f.Files.OpenCount);
    }
}

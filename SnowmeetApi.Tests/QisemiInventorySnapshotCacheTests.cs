using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SnowmeetApi.Services.Qisemi;
using Xunit.Abstractions;

namespace SnowmeetApi.Tests;

[Collection("Qisemi SQL Server")]
public class QisemiInventorySnapshotCacheTests(ITestOutputHelper output)
{
    private sealed class CaptureLog(ITestOutputHelper output) : ILogger<QisemiReadService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) => output.WriteLine(formatter(state, ex));
    }
    private sealed class Factory : IQisemiConnectionFactory
    {
        public ConcurrentQueue<long> RoundTrips { get; } = new();
        public DbConnection Create()
        {
            var value = Environment.GetEnvironmentVariable("SNOWMEET_QISEMI_SNAPSHOT_TEST_SQLSERVER")!;
            var b = new SqlConnectionStringBuilder(value);
            if (!b.DataSource.Equals(@"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)
                || !b.InitialCatalog.StartsWith("snowmeet_qsm_api_snapshot_", StringComparison.Ordinal) || !b.IntegratedSecurity)
                throw new InvalidOperationException("缓存快照测试只允许本机临时 LocalDB");
            var c = new SqlConnection(value) { StatisticsEnabled = true };
            c.StateChange += (_, e) => { if (e.CurrentState == ConnectionState.Closed)
                RoundTrips.Enqueue(Convert.ToInt64(c.RetrieveStatistics()["ServerRoundtrips"])); };
            return c;
        }
    }
    private sealed class NoFiles : IQisemiFileReader
    {
        public Task<QisemiFileHandle> OpenAsync(string provider, string objectKey, string name, string? mime, CancellationToken ct) =>
            throw new InvalidOperationException("缓存查询验证不访问文件存储");
    }

    [QisemiSnapshotFact]
    public async Task FullSnapshotSearchFiltersSortsAndPagesReuseCacheWithFewerSqlRoundTrips()
    {
        var cache = new QisemiInventoryCache(); var factory = new Factory();
        var service = new QisemiReadService(factory, new NoFiles(), cache, new CaptureLog(output));
        async Task<JsonElement> Read(string label, QisemiQuery q)
        {
            q.SourceId = 1; var timer = Stopwatch.StartNew();
            var response = JsonSerializer.SerializeToElement(await service.ReadAsync("inventory", q, null, null, default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var result = response.GetProperty("result");
            output.WriteLine(JsonSerializer.Serialize(new { label, elapsedMs = timer.Elapsed.TotalMilliseconds,
                sqlRoundTrips = factory.RoundTrips.Last(), total = result.GetProperty("total").GetString(),
                builds = cache.BuildCount, hits = cache.HitCount, entries = cache.EntryCount, estimatedBytes = cache.EstimatedBytes }));
            return result;
        }
        var first = await Read("brand-cold", new() { Keyword = "Fischer", InStockOnly = true });
        long coldTrips = factory.RoundTrips.Last();
        Assert.True(long.Parse(first.GetProperty("total").GetString()!) > 30);
        long root = long.Parse(first.GetProperty("categoryFacets").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "【硬件】双板鞋").GetProperty("id").GetString()!);
        long warehouse = long.Parse(first.GetProperty("warehouseFacets").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "【旗舰】零售商品仓").GetProperty("id").GetString()!);
        await Read("brand-sort-page", new() { Keyword = "Fischer", InStockOnly = true, Page = 2, SortBy = "topCategory", SortDirection = "desc" });
        Assert.True(factory.RoundTrips.Last() < coldTrips);
        var category = await Read("brand-category", new() { Keyword = "Fischer", InStockOnly = true, ResultCategoryId = root });
        var scoped = await Read("brand-category-warehouse", new() { Keyword = "Fischer", InStockOnly = true, ResultCategoryId = root, ResultWarehouseId = warehouse });
        Assert.True(long.Parse(scoped.GetProperty("total").GetString()!) <= long.Parse(category.GetProperty("total").GetString()!));
        var restore = await Read("brand-restore", new() { Keyword = "Fischer", InStockOnly = true });
        Assert.Equal(first.GetProperty("items").GetRawText(), restore.GetProperty("items").GetRawText());
        Assert.Equal(1, cache.BuildCount); Assert.Equal(4, cache.HitCount);
        await Read("main-warehouse-cold", new() { Keyword = "Fischer", InStockOnly = true, WarehouseId = warehouse });
        Assert.Equal(2, cache.BuildCount);
        var full = await Read("catalog-cold", new() { InStockOnly = true });
        Assert.True(long.Parse(full.GetProperty("total").GetString()!) > 1000);
        var fullPage = await Read("catalog-sort-page", new() { InStockOnly = true, SortDirection = "desc", Page = 2 });
        Assert.Equal(full.GetProperty("categoryFacets").GetRawText(), fullPage.GetProperty("categoryFacets").GetRawText());
        Assert.Equal(3, cache.BuildCount); Assert.Equal(3, cache.EntryCount);
        Assert.InRange(cache.EstimatedBytes, 1, 64L * 1024 * 1024);
        // Include zero/negative/unknown stock: this is the largest catalogue and
        // exposed an OPENJSON nested-loop plan timeout during production checks.
        var all = await Read("catalog-all-cold", new() { PageSize = 1 });
        Assert.True(long.Parse(all.GetProperty("total").GetString()!) > 5000);
        var allPage = await Read("catalog-all-sort-page", new() { Page = 2, SortBy = "topCategory", SortDirection = "desc" });
        Assert.Equal(all.GetProperty("total").GetString(), allPage.GetProperty("total").GetString());
        Assert.Equal(30, allPage.GetProperty("items").GetArrayLength());
        Assert.Equal(4, cache.BuildCount); Assert.Equal(6, cache.HitCount);
        await Read("brand-force-refresh", new() { Keyword = "Fischer", InStockOnly = true, RefreshCache = true });
        Assert.Equal(5, cache.BuildCount);
    }
}

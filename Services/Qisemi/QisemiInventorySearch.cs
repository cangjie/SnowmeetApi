#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SnowmeetApi.Services.Qisemi;

public sealed partial class QisemiReadService
{
    private sealed record InventoryMatch(string StyleId, string RootId);
    private sealed record InventorySearch(
        List<Dictionary<string, object?>> Warehouses, Dictionary<string, string> Roots, InventoryMatch[] Matches,
        Dictionary<string, Dictionary<string, object?>> Items, string[] NameOrder,
        Dictionary<string, HashSet<string>> StockByWarehouse,
        Dictionary<(string Style, string Warehouse), Dictionary<string, object?>> WarehouseAmounts,
        Dictionary<string, Dictionary<string, object?>> AllAmounts,
        Dictionary<string, long> CategoryAscending, Dictionary<string, long> CategoryDescending, long EstimatedBytes)
    {
        public object Select(QisemiQuery q)
        {
            string? warehouse = q.ResultWarehouseId == null ? null : QisemiRules.Id(q.ResultWarehouseId.Value);
            if (warehouse != null && !Warehouses.Any(w => (string)w["id"]! == warehouse))
                throw new QisemiReadException(404, "仓库不存在或已删除");
            if (q.WarehouseId != null && q.ResultWarehouseId != null && q.WarehouseId != q.ResultWarehouseId)
                throw new QisemiReadException(400, "结果仓库超出当前仓库筛选范围");
            string? category = q.ResultCategoryId == null ? null : QisemiRules.Id(q.ResultCategoryId.Value);
            if (q.ResultCategoryId is > 0 && !Roots.ContainsKey(category!))
                throw new QisemiReadException(404, "结果分类不存在或不是顶级分类");
            bool InCategory(InventoryMatch row) => category == null || row.RootId == category;
            var categoryStyles = Matches.Where(InCategory).Select(row => row.StyleId).ToHashSet();
            var warehouseMatched = Matches.Where(row => warehouse == null ||
                StockByWarehouse.TryGetValue(warehouse, out var set) && set.Contains(row.StyleId)).ToArray();
            var filteredIds = warehouseMatched.Where(InCategory).Select(row => row.StyleId).ToHashSet();
            var facets = warehouseMatched.GroupBy(row => row.RootId).Select(group => new Dictionary<string, object?> {
                ["id"] = group.Key, ["name"] = group.Key == "0" ? "未分类" : Roots[group.Key],
                ["count"] = QisemiRules.Id(group.Select(row => row.StyleId).Distinct().LongCount())
            }).OrderBy(row => (string)row["id"]! == "0").ThenBy(row => (string)row["name"]!, StringComparer.OrdinalIgnoreCase).ToArray();
            var warehouseFacets = new List<Dictionary<string, object?>>();
            foreach (var w in Warehouses)
            {
                string id = (string)w["id"]!;
                if (!StockByWarehouse.TryGetValue(id, out var stocked)) continue;
                var eligible = stocked.Where(categoryStyles.Contains).ToArray();
                if (eligible.Length == 0) continue;
                var amounts = eligible.Select(style => WarehouseAmounts[(style, id)]).ToArray();
                warehouseFacets.Add(new() { ["id"] = id, ["name"] = w["name"], ["count"] = QisemiRules.Id(eligible.Length),
                    ["quantityMain"] = InventoryQuantity(amounts), ["unknownCount"] = UnknownCount(amounts) });
            }
            IEnumerable<string> ordered = NameOrder;
            if (q.SortBy == "topCategory" && category == null)
            {
                var ranks = q.SortDirection == "desc" ? CategoryDescending : CategoryAscending;
                ordered = ordered.OrderBy(id => ranks[id]);
            }
            else if (q.SortDirection == "desc") ordered = ordered.Reverse();
            var ids = ordered.Where(filteredIds.Contains).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize);
            var items = new List<Dictionary<string, object?>>();
            foreach (string id in ids)
            {
                // Never mutate a shared cached card when changing the display warehouse.
                var item = new Dictionary<string, object?>(Items[id]);
                var amount = warehouse == null ? AllAmounts[id] : WarehouseAmounts[(id, warehouse)];
                item["quantityMain"] = amount["quantityMain"]; item["sizeCount"] = amount["sizeCount"];
                item["unknownCount"] = int.Parse((string)amount["unknownCount"]!, CultureInfo.InvariantCulture);
                item["hasNegative"] = amount["hasNegative"]; items.Add(item);
            }
            var allAmounts = categoryStyles.Select(id => AllAmounts[id]).ToArray();
            return new Dictionary<string, object?> {
                ["items"] = items, ["total"] = QisemiRules.Id(filteredIds.Count), ["page"] = q.Page, ["pageSize"] = q.PageSize,
                ["categoryFacets"] = facets, ["unfilteredTotal"] = QisemiRules.Id(warehouseMatched.Select(row => row.StyleId).Distinct().LongCount()),
                ["warehouseFacets"] = warehouseFacets, ["warehouseUnfilteredTotal"] = QisemiRules.Id(categoryStyles.Count),
                ["warehouseUnfilteredQuantityMain"] = InventoryQuantity(allAmounts), ["warehouseUnfilteredUnknownCount"] = UnknownCount(allAmounts)
            };
        }

        private static string UnknownCount(IEnumerable<Dictionary<string, object?>> amounts) =>
            QisemiRules.Id(amounts.Sum(a => long.Parse((string)a["unknownCount"]!, CultureInfo.InvariantCulture)));
    }

    private static async Task<string> InventoryRevision(QisemiSql sql, QisemiScope scope)
    {
        // Use only dependencies of the inventory read. @@DBTS also moves for
        // unrelated work, and would discard usable searches on a busy database.
        // Read no raw payloads or stock values. MAX detects updates/inserts;
        // COUNT also detects deletes. RepeatableRead holds these rows through
        // the response, so a warm hit obeys the same validity/publication checks.
        string source = sql.Add(scope.SourceId);
        string[] tables = ["import_batch", "import_module", "raw_record", "product_style", "product_sku",
            "style_sku", "stock_total", "stock_snapshot", "category", "warehouse"];
        string query = string.Join(" UNION ALL ", tables.Select(table =>
            $"SELECT N'{table}' table_name,COUNT_BIG(*) row_count,MAX(CONVERT(bigint,row_version)) last_version FROM dbo.qsm_{table} WHERE source_id={source}"));
        var revision = await sql.Rows(query + " ORDER BY table_name OPTION(MAXDOP 1,MAX_GRANT_PERCENT=1)");
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(revision)));
    }

    private async Task<object> CachedInventory(QisemiSql sql, QisemiScope scope, QisemiQuery q, string database, CancellationToken ct)
    {
        string revision = await InventoryRevision(sql, scope);
        // The API is currently deliberately unauthenticated. When authorization is
        // enabled, include its resolved data scope here before sharing any entries.
        string key = JsonSerializer.Serialize(new { database, scope.SourceId, scope.BatchId, revision,
            keyword = string.Join(" ", QisemiRules.Tokens(q.Keyword)), q.WarehouseId, q.TopCategoryId,
            q.InStockOnly, q.SkuId, q.StyleId, q.CategoryId, q.NegativeOnly, q.SourceEnabled, q.SourceDeleted });
        var result = await searchCache.GetOrCreateAsync(key, q.RefreshCache, async () => {
            var data = await LoadInventorySearch(sql, scope, q);
            // A concurrent write may have changed data during the cold build.
            // Return this normal transactional read, but do not reuse it later.
            string after = await InventoryRevision(sql, scope);
            bool stable = revision == after;
            if (!stable) logger?.LogDebug("Qisemi search cache skipped a changing revision: {Before} -> {After}", revision, after);
            return (data, data.EstimatedBytes, stable);
        }, ct);
        return result.Select(q);
    }
}

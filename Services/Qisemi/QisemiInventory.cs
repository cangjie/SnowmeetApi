#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SnowmeetApi.Services.Qisemi;

public sealed partial class QisemiReadService
{
    private static async Task<List<Dictionary<string, object?>>> WarehouseRows(QisemiSql sql, QisemiScope s)
    {
        var rows = await sql.Rows($@"SELECT w.id,w.source_key,w.name,w.code,w.branch_id,w.source_enabled,w.source_deleted,w.source_missing,w.warehouse_purpose,w.retail_use,w.sort,
            CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.qsm_import_module m
              CROSS APPLY OPENJSON(m.coverage_json,'$.warehouses_result') WITH (warehouse_id nvarchar(128),state varchar(24),[rows] bigint,expected bigint) j
              WHERE m.valid=1 AND m.source_id=w.source_id AND m.batch_id={sql.Add(s.BatchId)} AND m.module_key=N'stock_by_warehouse'
                AND m.state='complete' AND LOWER(j.warehouse_id) COLLATE Latin1_General_100_BIN2=w.source_key
                AND j.state='complete' AND j.rows=j.expected) THEN 1 ELSE 0 END) coverage_complete
            FROM dbo.qsm_warehouse w WHERE w.source_id={sql.Add(s.SourceId)} AND {MasterVisible("w")} ORDER BY w.sort,w.name,w.id");
        return rows;
    }
    private static async Task<object> Warehouses(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        var rows = await WarehouseRows(sql, s);
        return rows.Where(r => QisemiRules.Tokens(q.Keyword).All(t => ((string)r["name"]!).Contains(t, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static string SkuLink(string style = "p", string link = "a", string sku = "k") => $@"dbo.qsm_style_sku {link}
        JOIN dbo.qsm_product_sku {sku} ON {sku}.id={link}.sku_id AND {sku}.source_id={link}.source_id AND {sku}.valid=1
        JOIN dbo.qsm_raw_record {sku}r ON {sku}r.id={sku}.last_raw_record_id AND {sku}r.source_id={sku}.source_id AND {sku}r.batch_id={sku}.last_seen_batch_id AND {sku}r.valid=1
        JOIN dbo.qsm_import_module {sku}m ON {sku}m.id={sku}r.module_id AND {sku}m.source_id={sku}r.source_id AND {sku}m.batch_id={sku}r.batch_id AND {sku}m.valid=1 AND {sku}m.state IN ('complete','empty')
        JOIN dbo.qsm_import_batch {sku}b ON {sku}b.id={sku}r.batch_id AND {sku}b.source_id={sku}r.source_id AND {sku}b.valid=1 AND {sku}b.state='published' AND {sku}b.verification_passed=1 AND {sku}b.published_at IS NOT NULL";

    private static Task<List<Dictionary<string, object?>>> ProductCategoryRows(QisemiSql sql, QisemiScope s)
        => sql.Rows($@"SELECT c.id,c.name,c.parent_id,c.sort,c.source_enabled,c.source_deleted FROM dbo.qsm_category c
            WHERE c.source_id={sql.Add(s.SourceId)} AND c.category_kind='product' AND {MasterVisible("c")}
            ORDER BY c.sort,c.name,c.id");

    private sealed record CategoryLevel(string Id, string Name);
    private sealed record CategoryPath(string CategoryId, CategoryLevel[] Levels, bool Complete);
    private static Task<List<Dictionary<string, object?>>> InventoryStockRows(QisemiSql sql, QisemiScope scope, bool warehouse, long? warehouseId)
    {
        string table = warehouse ? "qsm_stock_snapshot" : "qsm_stock_total";
        string columns = warehouse ? "st.sku_id,st.warehouse_id" : "st.sku_id";
        string where = $"st.source_id={sql.Add(scope.SourceId)} AND st.batch_id={sql.Add(scope.BatchId)}"
            + (warehouse ? sql.Optional("st.warehouse_id", warehouseId) : "");
        return sql.Rows($@"SELECT {columns},CASE WHEN st.valid=1 AND {RawVisible("st.raw_record_id", "st.source_id", "st.batch_id")}
            THEN st.quantity_main END quantity_main FROM dbo.{table} st WHERE {where} OPTION(LOOP JOIN,RECOMPILE,MAX_GRANT_PERCENT=1,MAXDOP 1)");
    }

    private static decimal? InventoryQuantity(IEnumerable<Dictionary<string, object?>> amounts)
    {
        var rows = amounts.ToArray();
        return rows.Any(row => row["quantityMain"] == null) ? null : rows.Sum(row => (decimal)row["quantityMain"]!);
    }

    private static Dictionary<string, CategoryPath> ProductCategoryPaths(List<Dictionary<string, object?>> categories)
    {
        var index = categories.ToDictionary(c => (string)c["id"]!);
        var paths = new Dictionary<string, CategoryPath>();
        foreach (string id in index.Keys)
        {
            var levels = new List<CategoryLevel>(); var visited = new HashSet<string>();
            string? current = id; bool complete = false;
            while (current != null && visited.Add(current) && index.TryGetValue(current, out var node))
            {
                levels.Add(new(current, (string)node["name"]!)); current = node["parentId"] as string;
                if (current == null) complete = true;
            }
            levels.Reverse(); paths[id] = new(id, levels.ToArray(), complete);
        }
        return paths;
    }

    private static async Task<InventorySearch> LoadInventorySearch(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        var warehouses = await WarehouseRows(sql, s);
        var warehouseIndex = warehouses.ToDictionary(w => (string)w["id"]!);
        if (q.WarehouseId is { } primaryWarehouseId && !warehouseIndex.ContainsKey(QisemiRules.Id(primaryWarehouseId)))
            throw new QisemiReadException(404, "仓库不存在或已删除");
        var categories = await ProductCategoryRows(sql, s);
        var categoryPaths = ProductCategoryPaths(categories);
        var roots = categories.Where(c => c["parentId"] == null).ToDictionary(c => (string)c["id"]!);
        string RootOf(Dictionary<string, object?> row) => row["categoryId"] is string leaf
            && categoryPaths.TryGetValue(leaf, out var path) && path.Complete ? path.Levels[0].Id : "0";
        string where = $"p.valid=1 AND p.source_id={sql.Add(s.SourceId)}"
            + sql.Optional("p.id", q.StyleId) + sql.Optional("p.category_id", q.CategoryId);
        // Validate SKU provenance once. Stock reads below are narrow projections,
        // rather than repeatedly joining raw owners for every style/warehouse pair.
        var visibleSkus = await sql.Rows($@"SELECT DISTINCT p.id,k.id sku_id,k.category_id,k.source_enabled,k.source_deleted
            FROM {SkuLink()} JOIN dbo.qsm_product_style p ON p.id=a.style_id AND p.source_id=a.source_id
            WHERE {where} AND a.valid=1 OPTION(HASH JOIN,RECOMPILE,MAX_GRANT_PERCENT=1,MAXDOP 1)");
        List<Dictionary<string, object?>> sourceRows = q.WarehouseId == null ? await InventoryStockRows(sql, s, false, null) : [];
        var warehouseRows = await InventoryStockRows(sql, s, true, q.WarehouseId);
        var sourceStock = sourceRows.ToDictionary(row => (string)row["skuId"]!, row => (decimal?)row["quantityMain"]);
        var warehouseStock = warehouseRows.ToDictionary(row => ((string)row["skuId"]!, (string)row["warehouseId"]!), row => (decimal?)row["quantityMain"]);
        var styleSkus = visibleSkus.ToLookup(row => (string)row["id"]!, row => (string)row["skuId"]!);
        decimal? Quantity(string sku, string? warehouse)
        {
            if (warehouse == null) return sourceStock.GetValueOrDefault(sku);
            if (warehouseStock.TryGetValue((sku, warehouse), out var value)) return value;
            return (bool)warehouseIndex[warehouse]["coverageComplete"]! ? 0m : null;
        }
        string? primaryWarehouse = q.WarehouseId == null ? null : QisemiRules.Id(q.WarehouseId.Value);
        HashSet<string>? stockedStyles = q.InStockOnly ? styleSkus.Where(g => g.Any(sku => Quantity(sku, primaryWarehouse) > 0)).Select(g => g.Key).ToHashSet() : null;
        HashSet<string>? categoryIds = null;
        if (q.TopCategoryId is { } topCategoryId)
        {
            string rootId = QisemiRules.Id(topCategoryId);
            if (!roots.ContainsKey(rootId)) throw new QisemiReadException(404, "顶级商品分类不存在或已删除");
            var children = categories.Where(c => c["parentId"] != null).ToLookup(c => (string)c["parentId"]!);
            categoryIds = new HashSet<string>(); var pending = new Queue<string>(); pending.Enqueue(rootId);
            while (pending.TryDequeue(out string? category))
                if (categoryIds.Add(category)) foreach (var child in children[category]) pending.Enqueue((string)child["id"]!);
        }
        string[] tokens = QisemiRules.Tokens(q.Keyword);
        int requiredMask = (1 << tokens.Length) - 1;
        var categoryMasks = categoryPaths.ToDictionary(x => x.Key, x => tokens.Select((token, n) =>
            x.Value.Levels.Any(level => level.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ? 1 << n : 0).Aggregate(0, (a, b) => a | b));
        var skuMasks = new Dictionary<(string Style, string Sku), int>();
        if (tokens.Length > 0)
        {
            // Keep SQL collation and literal LIKE semantics for each token.
            string keywordMask = string.Join("+", tokens.Select((token, n) =>
                $"CASE WHEN CONCAT(p.search_text,N' ',k.search_text) LIKE {sql.Add(QisemiRules.Like(token))} ESCAPE N'~' THEN {1 << n} ELSE 0 END"));
            var textMatches = await sql.Rows($@"SELECT p.id,k.id sku_id,({keywordMask}) keyword_mask
                FROM dbo.qsm_style_sku a JOIN dbo.qsm_product_sku k ON k.id=a.sku_id AND k.source_id=a.source_id AND k.valid=1
                JOIN dbo.qsm_product_style p ON p.id=a.style_id AND p.source_id=a.source_id AND p.valid=1
                WHERE a.valid=1 AND a.source_id={sql.Add(s.SourceId)} OPTION(LOOP JOIN,RECOMPILE,MAXDOP 1)");
            foreach (var row in textMatches) skuMasks[((string)row["id"]!, (string)row["skuId"]!)] = Convert.ToInt32(row["keywordMask"]);
        }
        var matched = visibleSkus.Where(row => (stockedStyles == null || stockedStyles.Contains((string)row["id"]!))
            && (q.SkuId == null || (string)row["skuId"]! == QisemiRules.Id(q.SkuId.Value))
            && (q.SourceEnabled == null || (bool?)row["sourceEnabled"] == q.SourceEnabled)
            && (q.SourceDeleted == null || (bool?)row["sourceDeleted"] == q.SourceDeleted)
            && (!q.NegativeOnly || Quantity((string)row["skuId"]!, primaryWarehouse) < 0)
            && (categoryIds == null || row["categoryId"] is string category && categoryIds.Contains(category))
            && ((skuMasks.GetValueOrDefault(((string)row["id"]!, (string)row["skuId"]!))
                | (row["categoryId"] is string leaf ? categoryMasks.GetValueOrDefault(leaf) : 0)) & requiredMask) == requiredMask).ToArray();
        var primaryStyles = matched.Select(row => (string)row["id"]!).ToHashSet();
        var owners = visibleSkus.ToLookup(row => (string)row["skuId"]!, row => (string)row["id"]!);
        var stockByWarehouse = new Dictionary<string, HashSet<string>>();
        foreach (var row in warehouseRows.Where(row => (decimal?)row["quantityMain"] > 0))
        {
            string warehouse = (string)row["warehouseId"]!;
            if (!warehouseIndex.ContainsKey(warehouse)) continue;
            foreach (string style in owners[(string)row["skuId"]!].Where(primaryStyles.Contains))
            {
                if (!stockByWarehouse.TryGetValue(warehouse, out var set)) stockByWarehouse[warehouse] = set = new();
                set.Add(style);
            }
        }
        Dictionary<string, object?> Amount(string style, string? warehouse)
        {
            var values = styleSkus[style].Select(sku => Quantity(sku, warehouse)).ToArray();
            return new() { ["styleId"] = style, ["warehouseId"] = warehouse,
                ["quantityMain"] = values.Any(v => v == null) ? null : values.Sum(v => v!.Value),
                ["unknownCount"] = QisemiRules.Id(values.LongCount(v => v == null)), ["sizeCount"] = QisemiRules.Id(values.LongLength),
                ["hasNegative"] = values.Any(v => v < 0) };
        }
        // Every amount includes ALL visible sizes, independently of the size hit.
        var targets = stockByWarehouse.SelectMany(w => w.Value.Select(style => (Style: style, Warehouse: w.Key))).ToHashSet();
        if (primaryWarehouse != null) targets.UnionWith(primaryStyles.Select(style => (style, primaryWarehouse)));
        var warehouseAmounts = targets.Select(t => Amount(t.Style, t.Warehouse)).ToList();
        var allAmounts = primaryStyles.Select(style => Amount(style, primaryWarehouse)).ToList();
        string matchedIds = sql.Add(JsonSerializer.Serialize(primaryStyles));
        where = $"p.valid=1 AND p.source_id={sql.Add(s.SourceId)} AND p.id IN(SELECT CONVERT(bigint,[value]) FROM OPENJSON({matchedIds}))";
        // OPENJSON has a small fixed row estimate. Force hash joins so a full
        // catalogue does not repeatedly parse/scan its identity and category sets.
        var items = await sql.Rows($"SELECT p.* FROM dbo.qsm_product_style p WHERE {where} ORDER BY p.display_name,p.id OPTION(HASH JOIN,RECOMPILE,MAXDOP 1,MAX_GRANT_PERCENT=1)");
        var categoryRows = visibleSkus;
        var pathsByStyle = categoryRows.Where(row => primaryStyles.Contains((string)row["id"]!)).GroupBy(row => (string)row["id"]!)
            .ToDictionary(group => group.Key, group => group.Select(row => row["categoryId"] as string).OfType<string>().Distinct()
                .Where(categoryPaths.ContainsKey).Select(id => categoryPaths[id])
                .OrderBy(path => string.Join(" / ", path.Levels.Select(level => level.Name)), StringComparer.OrdinalIgnoreCase).ThenBy(path => path.CategoryId).ToArray());
        foreach (var item in items) item["categoryPaths"] = pathsByStyle.GetValueOrDefault((string)item["id"]!) ?? [];
        var sortNames = matched.GroupBy(row => (string)row["id"]!).Select(group => new {
            id = group.Key, name = group.Select(RootOf).Where(id => id != "0").Distinct()
                .Select(id => (string)roots[id]["name"]!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
        });
        // SQL supplies both category orders: preserve database collation and keep
        // unclassified styles last in either direction, without SQL on a cache hit.
        string sortJoin = $"LEFT JOIN OPENJSON({sql.Add(JsonSerializer.Serialize(sortNames))}) WITH(id bigint,name nvarchar(512)) sc ON sc.id=p.id";
        var ranks = await sql.Rows($@"SELECT p.id,
            ROW_NUMBER() OVER(ORDER BY CASE WHEN sc.name IS NULL THEN 1 ELSE 0 END,sc.name,p.display_name,p.id) category_asc,
            ROW_NUMBER() OVER(ORDER BY CASE WHEN sc.name IS NULL THEN 1 ELSE 0 END,sc.name DESC,p.display_name DESC,p.id DESC) category_desc
            FROM dbo.qsm_product_style p {sortJoin} WHERE {where} OPTION(HASH JOIN,RECOMPILE,MAXDOP 1,MAX_GRANT_PERCENT=1)");
        var identities = matched.Select(row => new InventoryMatch((string)row["id"]!, RootOf(row))).Distinct().ToArray();
        long estimatedBytes = 65536L + 4L * JsonSerializer.SerializeToUtf8Bytes(new {
            warehouses, identities, items, stockByWarehouse, warehouseAmounts, allAmounts, ranks
        }).LongLength;
        return new(warehouses, roots.ToDictionary(x => x.Key, x => (string)x.Value["name"]!), identities,
            items.ToDictionary(row => (string)row["id"]!), items.Select(row => (string)row["id"]!).ToArray(),
            stockByWarehouse, warehouseAmounts.ToDictionary(row => ((string)row["styleId"]!, (string)row["warehouseId"]!)),
            allAmounts.ToDictionary(row => (string)row["styleId"]!),
            ranks.ToDictionary(row => (string)row["id"]!, row => long.Parse((string)row["categoryAsc"]!)),
            ranks.ToDictionary(row => (string)row["id"]!, row => long.Parse((string)row["categoryDesc"]!)), estimatedBytes);
    }

    private static async Task<object> Style(QisemiSql sql, QisemiScope s, QisemiQuery q, long id)
    {
        var style = await sql.One($"SELECT p.* FROM dbo.qsm_product_style p WHERE p.valid=1 AND p.source_id={sql.Add(s.SourceId)} AND p.id={sql.Add(id)}");
        var skus = await sql.Rows($@"SELECT k.id,k.code,k.barcode,k.name,k.size_label,k.size_sort,k.base_unit_id,k.source_enabled,k.source_deleted,k.source_missing,
            st.quantity_main source_total_quantity,u.name unit_name FROM {SkuLink()}
            LEFT JOIN dbo.qsm_unit u ON u.id=k.base_unit_id AND u.source_id=k.source_id AND {MasterVisible("u")}
            LEFT JOIN dbo.qsm_stock_total st ON st.sku_id=k.id AND st.source_id=k.source_id AND st.batch_id={sql.Add(s.BatchId)} AND st.valid=1 AND {RawVisible("st.raw_record_id", "st.source_id", "st.batch_id")}
            WHERE a.valid=1 AND a.style_id={sql.Add(id)} AND a.source_id={sql.Add(s.SourceId)} ORDER BY CASE WHEN k.size_sort IS NULL THEN 1 ELSE 0 END,k.size_sort,k.size_label,k.id");
        if (skus.Count == 0) throw new QisemiReadException(404, "款式没有有效 SKU");
        var allWarehouses = await WarehouseRows(sql, s);
        if (q.WarehouseId != null && !allWarehouses.Any(w => (string)w["id"]! == QisemiRules.Id(q.WarehouseId.Value)))
            throw new QisemiReadException(404, "仓库不存在或已删除");
        var stocks = await sql.Rows($@"SELECT st.sku_id,st.warehouse_id,st.valid,st.quantity_main,st.source_available_quantity,st.order_quantity,st.on_in_quantity,st.on_out_quantity,
            CONVERT(bit,CASE WHEN {RawVisible("st.raw_record_id", "st.source_id", "st.batch_id")} THEN 1 ELSE 0 END) raw_visible
            FROM dbo.qsm_stock_snapshot st JOIN dbo.qsm_style_sku a ON a.sku_id=st.sku_id AND a.source_id=st.source_id AND a.valid=1
            WHERE st.source_id={sql.Add(s.SourceId)} AND st.batch_id={sql.Add(s.BatchId)} AND a.style_id={sql.Add(id)}");
        var index = stocks.ToDictionary(r => ((string)r["skuId"]!, (string)r["warehouseId"]!));
        var visibleWarehouses = allWarehouses.Where(w => (q.WarehouseId == null || (string)w["id"]! == QisemiRules.Id(q.WarehouseId.Value))
            && QisemiRules.Tokens(q.WarehouseKeyword).All(t => ((string)w["name"]!).Contains(t, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var sku in skus)
        {
            var cells = new List<Dictionary<string, object?>>();
            foreach (var w in visibleWarehouses)
            {
                index.TryGetValue(((string)sku["id"]!, (string)w["id"]!), out var stock);
                bool explicitRow = stock != null && (bool)stock["valid"]! && (bool)stock["rawVisible"]!;
                bool inferred = stock == null && (bool)w["coverageComplete"]!;
                decimal? quantity = explicitRow ? (decimal)stock!["quantityMain"]! : inferred ? 0m : null;
                if (!q.IncludeZero && quantity == 0m) continue;
                cells.Add(new() { ["warehouseId"] = w["id"], ["warehouseName"] = w["name"], ["quantityMain"] = quantity,
                    ["quantityState"] = explicitRow ? "reported" : inferred ? "inferred_zero" : "unknown",
                    ["hasNegative"] = quantity < 0m, ["sourceAvailableQuantity"] = explicitRow ? stock!["sourceAvailableQuantity"] : null,
                    ["orderQuantity"] = explicitRow ? stock!["orderQuantity"] : null,
                    ["onInQuantity"] = explicitRow ? stock!["onInQuantity"] : null, ["onOutQuantity"] = explicitRow ? stock!["onOutQuantity"] : null });
            }
            // Keep every size even when the selected warehouses have zero stock.
            sku["warehouses"] = cells;
            var selectedCells = visibleWarehouses.Select(w => index.GetValueOrDefault(((string)sku["id"]!, (string)w["id"]!))).ToArray();
            bool allKnown = visibleWarehouses.Select((w, n) => selectedCells[n] is { } z ? (bool)z["valid"]! && (bool)z["rawVisible"]! : (bool)w["coverageComplete"]!).All(x => x);
            sku["selectedQuantity"] = allKnown && visibleWarehouses.Length > 0 ? selectedCells.Sum(z => z == null ? 0m : (decimal)z["quantityMain"]!) : null;
        }
        return new { style, sizes = skus, warehouses = visibleWarehouses, hiddenZeroWarehouses = !q.IncludeZero,
            warehouseScopeComplete = allWarehouses.All(w => (bool)w["coverageComplete"]!),
            note = "数量为账面库存；缺失组合只有在对应仓完整采集时推定为零，未知数量为 null。尺码始终完整返回。" };
    }

    private static async Task<object> Sku(QisemiSql sql, QisemiScope s, QisemiQuery q, long id)
    {
        var sku = await sql.One($"SELECT k.* FROM dbo.qsm_product_sku k WHERE k.source_id={sql.Add(s.SourceId)} AND k.id={sql.Add(id)} AND {MasterVisible("k")}");
        var attrs = await sql.Rows($"SELECT a.* FROM dbo.qsm_sku_attribute a WHERE a.valid=1 AND a.source_id={sql.Add(s.SourceId)} AND a.sku_id={sql.Add(id)} AND {RawVisible("a.last_raw_record_id", "a.source_id")} ORDER BY a.sort,a.id");
        var units = await sql.Rows($"SELECT a.*,u.name unit_name FROM dbo.qsm_sku_unit a JOIN dbo.qsm_unit u ON u.id=a.unit_id AND u.source_id=a.source_id AND {MasterVisible("u")} WHERE a.valid=1 AND a.source_id={sql.Add(s.SourceId)} AND a.sku_id={sql.Add(id)} AND {RawVisible("a.last_raw_record_id", "a.source_id")} ORDER BY a.is_base DESC,a.id");
        var styles = await sql.Rows($"SELECT a.style_id,a.assignment_method,p.review_state FROM dbo.qsm_style_sku a JOIN dbo.qsm_product_style p ON p.id=a.style_id AND p.source_id=a.source_id AND p.valid=1 WHERE a.valid=1 AND a.source_id={sql.Add(s.SourceId)} AND a.sku_id={sql.Add(id)}");
        var raw = sku["lastRawRecordId"] is string rid ? await Record(sql, s, long.Parse(rid), false) : null;
        return new { sku, attributes = attrs, units, styles, raw };
    }

    private static Task<Dictionary<string, object?>> StockRows(QisemiSql sql, QisemiScope s, QisemiQuery q, bool warehouse)
    {
        string table = warehouse ? "qsm_stock_snapshot" : "qsm_stock_total";
        string from = $"dbo.{table} st JOIN dbo.qsm_product_sku k ON k.id=st.sku_id AND k.source_id=st.source_id AND {MasterVisible("k")}";
        if (warehouse) from += $" JOIN dbo.qsm_warehouse w ON w.id=st.warehouse_id AND w.source_id=st.source_id AND {MasterVisible("w")}";
        string where = ScopeWhere(sql, s, "st") + $" AND {RawVisible("st.raw_record_id", "st.source_id", "st.batch_id")}" + sql.Optional("st.sku_id", q.SkuId)
            + (warehouse ? sql.Optional("st.warehouse_id", q.WarehouseId) : "") + sql.Search("k.search_text", q.Keyword);
        if (q.NegativeOnly) where += " AND st.quantity_main<0";
        return sql.Page("st.*,k.name product_name,k.code product_code,k.size_label" + (warehouse ? ",w.name warehouse_name" : ""), from, where, "st.id", q);
    }
}

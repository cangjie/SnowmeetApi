#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SnowmeetApi.Services.Qisemi;

public sealed partial class QisemiReadService(IQisemiConnectionFactory connections, IQisemiFileReader files) : IQisemiReadService
{
    public async Task<object> ReadAsync(string operation, QisemiQuery query, long? id, string? key, CancellationToken ct)
    {
        query.Validate();
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        // Hold publication and validity rows until this response's reads finish.
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var sql = new QisemiSql(connection, tx, ct);
        if (operation == "sources")
        {
            var sources = await sql.Rows("SELECT s.id,s.display_name,s.source_timezone,s.current_full_batch_id,s.current_inventory_batch_id FROM dbo.qsm_source s WHERE s.valid=1 ORDER BY s.id");
            await tx.CommitAsync(ct); return new { items = sources, authentication = "reserved_headers_no_validation" };
        }
        bool inventory = operation is "inventory" or "style" or "warehouses" or "topCategories" or "stockTotals" or "warehouseStocks";
        var scope = await Scope(sql, query, inventory);
        object data = operation switch
        {
            "snapshot" => await Snapshot(sql, scope),
            "modules" => await Modules(sql, scope),
            "inventory" => await Inventory(sql, scope, query),
            "style" => await Style(sql, scope, query, id!.Value),
            "warehouses" => await Warehouses(sql, scope, query),
            "topCategories" => (await ProductCategoryRows(sql, scope)).Where(c => c["parentId"] == null).ToArray(),
            "documents" => await Documents(sql, scope, query),
            "document" => await Document(sql, scope, id!.Value, query.VersionId),
            "version" => await Version(sql, scope, id!.Value),
            "versions" => await Versions(sql, scope, query),
            "documentSection" => await DocumentSection(sql, scope, query, id!.Value, key!),
            "archives" => await Archives(sql, scope, query, key!, id),
            "sku" => await Sku(sql, scope, query, id!.Value),
            "records" => await Records(sql, scope, query, key),
            "record" => await Record(sql, scope, id!.Value),
            "movements" => await Movements(sql, scope, query),
            "salesLines" => await SalesLines(sql, scope, query),
            "stockTotals" => await StockRows(sql, scope, query, false),
            "warehouseStocks" => await StockRows(sql, scope, query, true),
            "files" => await FileList(sql, scope, query),
            "sheets" => await Sheets(sql, scope, query),
            "sheetRows" => await SheetRows(sql, scope, query, id!.Value),
            "issues" => await Issues(sql, scope, query),
            "batches" => await Batches(sql, scope, query),
            _ => throw new QisemiReadException(404, "接口不存在")
        };
        await tx.CommitAsync(ct);
        return new { snapshot = scope.Snapshot, result = data };
    }

    private static async Task<QisemiScope> Scope(QisemiSql sql, QisemiQuery q, bool inventory)
    {
        string where = "valid=1" + sql.Optional("id", q.SourceId);
        var sources = await sql.Rows($"SELECT id,display_name,source_timezone,current_full_batch_id,current_inventory_batch_id FROM dbo.qsm_source WHERE {where}");
        if (sources.Count == 0) throw new QisemiReadException(404, "数据源不存在或已删除");
        if (sources.Count != 1) throw new QisemiReadException(400, "请指定 sourceId");
        var source = sources[0]; long sid = long.Parse((string)source["id"]!);
        var current = source[inventory ? "currentInventoryBatchId" : "currentFullBatchId"];
        if (current == null) throw new QisemiReadException(409, "尚无成功发布的数据批次");
        long bid = long.Parse((string)current);
        if (q.BatchId is { } expected && expected != bid) throw new QisemiReadException(409, "数据批次已变化，请刷新后重新查询");
        var batch = await sql.One($"SELECT id,scope,capture_started_at,capture_finished_at,published_at,parser_version FROM dbo.qsm_import_batch WHERE id={sql.Add(bid)} AND source_id={sql.Add(sid)} AND valid=1 AND state='published' AND verification_passed=1 AND published_at IS NOT NULL");
        source["batchId"] = batch["id"]; source["batch"] = batch;
        source["businessTimezone"] = "Asia/Shanghai"; source["quantityKind"] = "book_stock";
        return new(sid, bid, source);
    }

    private static string ScopeWhere(QisemiSql sql, QisemiScope s, string alias)
        => $"{alias}.valid=1 AND {alias}.source_id={sql.Add(s.SourceId)} AND {alias}.batch_id={sql.Add(s.BatchId)}";

    // Every published projection has an active raw -> module -> batch provenance chain.
    private static string RawVisible(string raw, string source, string? batch = null) => $@"EXISTS (SELECT 1 FROM dbo.qsm_raw_record vr
        JOIN dbo.qsm_import_module vm ON vm.id=vr.module_id AND vm.source_id=vr.source_id AND vm.batch_id=vr.batch_id AND vm.valid=1 AND vm.state IN ('complete','empty')
        JOIN dbo.qsm_import_batch vb ON vb.id=vr.batch_id AND vb.source_id=vr.source_id AND vb.valid=1 AND vb.state='published' AND vb.verification_passed=1 AND vb.published_at IS NOT NULL
        WHERE vr.id={raw} AND vr.source_id={source} AND vr.valid=1{(batch == null ? "" : $" AND vr.batch_id={batch}")})";
    private static string MasterVisible(string alias) => $"{alias}.valid=1 AND EXISTS(SELECT 1 FROM dbo.qsm_import_batch mb WHERE mb.id={alias}.last_seen_batch_id AND mb.source_id={alias}.source_id AND mb.valid=1 AND mb.state='published' AND mb.verification_passed=1 AND mb.published_at IS NOT NULL) AND ({alias}.is_stub=1 OR {RawVisible(alias + ".last_raw_record_id", alias + ".source_id", alias + ".last_seen_batch_id")})";
    private static string DocumentVisible(string alias) => $"{alias}.valid=1 AND {RawVisible(alias + ".raw_record_id", alias + ".source_id", alias + ".batch_id")} AND EXISTS (SELECT 1 FROM dbo.qsm_document vd WHERE vd.id={alias}.document_id AND vd.source_id={alias}.source_id AND vd.valid=1)";

    private static async Task<object> Snapshot(QisemiSql sql, QisemiScope s)
    {
        var batch = await sql.One($"SELECT id,scope,state,verification_passed,capture_started_at,capture_finished_at,published_at,verification_json FROM dbo.qsm_import_batch WHERE id={sql.Add(s.BatchId)} AND source_id={sql.Add(s.SourceId)} AND valid=1");
        var modules = await sql.Rows($"SELECT id,module_key,carrier,state,expected_count,actual_count,capture_started_at,capture_finished_at,coverage_json,error_summary FROM dbo.qsm_import_module WHERE valid=1 AND batch_id={sql.Add(s.BatchId)} AND source_id={sql.Add(s.SourceId)} ORDER BY carrier,module_key");
        return new { batch, modules, note = "采集按模块顺序进行，各模块采集时间可能不同；历史版本、原表与当前单据分别展示，不重复累计。" };
    }
    private static async Task<object> Modules(QisemiSql sql, QisemiScope s)
    {
        var rows = await sql.Rows($"SELECT id,module_key,carrier,state,actual_count FROM dbo.qsm_import_module WHERE valid=1 AND source_id={sql.Add(s.SourceId)} AND batch_id={sql.Add(s.BatchId)} ORDER BY id");
        return QisemiCatalog.Modules.Select(m => new { m.Key, m.Label, m.Tab, m.Section, m.Kind, m.DocumentType, m.Sources, m.Native,
            coverage = rows.Where(r => m.Sources.Contains((string)r["moduleKey"]!) || (m.Native ?? []).Contains(((string)r["moduleKey"]!).Replace("native/", ""))).ToArray() }).ToArray();
    }
    private static Task<Dictionary<string, object?>> Batches(QisemiSql sql, QisemiScope s, QisemiQuery q)
        => sql.Page("b.id,b.scope,b.capture_started_at,b.capture_finished_at,b.published_at,b.state,b.verification_passed", "dbo.qsm_import_batch b",
            $"b.valid=1 AND b.source_id={sql.Add(s.SourceId)} AND b.state='published' AND b.verification_passed=1", "b.id DESC", q);
    private static Task<Dictionary<string, object?>> Issues(QisemiSql sql, QisemiScope s, QisemiQuery q)
        => sql.Page("i.*", "dbo.qsm_data_issue i", ScopeWhere(sql, s, "i") + sql.Optional("i.state", q.State)
            + $" AND (i.raw_record_id IS NULL OR {RawVisible("i.raw_record_id", "i.source_id", "i.batch_id")})"
            + sql.Search("CONCAT(i.issue_code,N' ',i.summary)", q.Keyword), "i.id", q);
}

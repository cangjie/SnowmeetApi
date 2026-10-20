#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SnowmeetApi.Services.Qisemi;

public sealed partial class QisemiReadService
{
    private static string VersionFrom => "dbo.qsm_document d JOIN dbo.qsm_document_version v ON v.id=d.current_version_id AND v.document_id=d.id AND v.source_id=d.source_id";
    private static string VersionWhere(QisemiSql sql, QisemiScope s)
        => ScopeWhere(sql, s, "v") + $" AND d.valid=1 AND v.version_kind='current' AND {DocumentVisible("v")}";

    private static async Task<object> Documents(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        string where = VersionWhere(sql, s) + sql.Search("CONCAT(v.document_no,N' ',v.partner_name,N' ',v.warehouse_name,N' ',v.remark,N' ',v.business_employee_name)", q.Keyword)
            + sql.Dates("v.business_at", q) + sql.Optional("v.source_bill_state", q.State) + sql.Optional("v.write_back", q.WriteBack);
        if (q.Module != null)
        {
            string type = QisemiCatalog.Get(q.Module).DocumentType ?? throw new ArgumentException("该模块不是单据模块");
            where += type is "loan_in" or "loan_out" ? $" AND d.document_type IN ({sql.Add(type)},{sql.Add(type + "_return")})" : sql.Optional("d.document_type", type);
        }
        where += sql.Optional("d.document_type", q.DocumentType);
        if (q.WarehouseId != null) where += $" AND ({string.Join(" OR ", new[] { "v.warehouse_id", "v.from_warehouse_id", "v.to_warehouse_id" }.Select(c => c + "=" + sql.Add(q.WarehouseId)))})";
        return await sql.Page("d.id,d.document_type,d.listing_state,d.source_key,d.source_deleted,d.source_enabled,d.source_missing,v.id version_id,v.document_no,v.business_at,v.business_time_precision,v.partner_name,v.branch_name,v.warehouse_name,v.amount,v.write_back,v.source_bill_state,v.source_io_state,v.source_payment_state,v.source_audit_state,v.remark,v.detail_coverage_json",
            VersionFrom, where, "v.business_at DESC,v.id DESC", q);
    }
    private static async Task<Dictionary<string, object?>> LoadVersion(QisemiSql sql, QisemiScope s, long id, bool document)
    {
        string where = ScopeWhere(sql, s, "v") + $" AND {DocumentVisible("v")}" + sql.Optional(document ? "d.id" : "v.id", id);
        if (document) where += " AND d.current_version_id=v.id AND v.version_kind='current'";
        return await sql.One($"SELECT v.*,d.document_type,d.source_key,d.listing_state,d.source_deleted,d.source_enabled,d.source_missing FROM dbo.qsm_document_version v JOIN dbo.qsm_document d ON d.id=v.document_id AND d.source_id=v.source_id AND d.valid=1 WHERE {where}");
    }
    private static async Task<object> Document(QisemiSql sql, QisemiScope s, long id, long? versionId)
    {
        var version = await LoadVersion(sql, s, versionId ?? id, versionId == null);
        if ((string)version["documentId"]! != QisemiRules.Id(id)) throw new QisemiReadException(404, "版本不属于该单据");
        return await VersionDetail(sql, s, version);
    }
    private static async Task<object> Version(QisemiSql sql, QisemiScope s, long id)
        => await VersionDetail(sql, s, await LoadVersion(sql, s, id, false));
    private static Task<Dictionary<string, object?>> Versions(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        string? type = q.Module switch { "sales_history_details" => "sale", "purchases_history_details" => "purchase", null => q.DocumentType,
            _ => throw new ArgumentException("请选择销售或进货历史版本模块") };
        return sql.Page("v.id,v.document_id,d.document_type,v.document_no,v.business_at,v.version_kind,v.amount,v.write_back,v.source_bill_state,v.detail_coverage_json",
            "dbo.qsm_document_version v JOIN dbo.qsm_document d ON d.id=v.document_id AND d.source_id=v.source_id AND d.valid=1",
            ScopeWhere(sql, s, "v") + $" AND {DocumentVisible("v")} AND v.version_kind='historical'" + sql.Optional("d.document_type", type)
                + sql.Optional("v.document_id", q.DocumentId) + sql.Dates("v.business_at", q)
                + sql.Search("CONCAT(v.document_no,N' ',v.partner_name,N' ',v.remark)", q.Keyword), "v.business_at DESC,v.id DESC", q);
    }
    private static async Task<object> VersionDetail(QisemiSql sql, QisemiScope s, Dictionary<string, object?> version)
    {
        long raw = long.Parse((string)version["rawRecordId"]!);
        var fields = await Record(sql, s, raw);
        string[] sections = ["lines", "settlements", "writeoffs", "relations", "movements", "versions", "files", "raw"];
        return new { document = version, raw = fields, sections, sectionPageSize = 30 };
    }
    private static async Task<object> DocumentSection(QisemiSql sql, QisemiScope s, QisemiQuery q, long id, string section)
    {
        var version = await LoadVersion(sql, s, q.VersionId ?? id, q.VersionId == null);
        if ((string)version["documentId"]! != QisemiRules.Id(id)) throw new QisemiReadException(404, "版本不属于该单据");
        long vid = long.Parse((string)version["id"]!);
        if (section == "versions") return await sql.Page("v.id,v.document_id,v.document_no,v.business_at,v.version_kind,v.batch_id,v.detail_coverage_json",
            "dbo.qsm_document_version v", ScopeWhere(sql, s, "v") + sql.Optional("v.document_id", id) + $" AND {DocumentVisible("v")}", "v.id DESC", q);
        if (section == "movements") { q.DocumentId = id; q.VersionId = vid; return await Movements(sql, s, q); }
        if (section == "files") { q.DocumentId = id; q.VersionId = vid; return await FileList(sql, s, q); }
        if (section == "raw")
        {
            var rawIds = new HashSet<string> { (string)version["rawRecordId"]! };
            if (version["detailCoverageJson"] is System.Text.Json.JsonElement coverage && coverage.TryGetProperty("supplement_raw_id", out var sup) && sup.ValueKind == System.Text.Json.JsonValueKind.Number)
                rawIds.Add(sup.GetInt64().ToString());
            foreach (string originTable in new[] { "document_line", "settlement_line", "writeoff_line" })
            {
                var origins = await sql.Rows($"SELECT DISTINCT raw_record_id FROM dbo.qsm_{originTable} WHERE valid=1 AND document_version_id={sql.Add(vid)} AND source_id={sql.Add(s.SourceId)} AND batch_id={sql.Add(s.BatchId)}");
                foreach (var origin in origins) rawIds.Add((string)origin["rawRecordId"]!);
            }
            string ownership = $"r.id IN (SELECT CONVERT(bigint,[value]) FROM OPENJSON({sql.Add(System.Text.Json.JsonSerializer.Serialize(rawIds))}))";
            string from = "dbo.qsm_raw_record r JOIN dbo.qsm_import_module m ON m.id=r.module_id AND m.source_id=r.source_id AND m.batch_id=r.batch_id AND m.valid=1";
            return await sql.Page("r.id,r.module_id,m.module_key,r.sheet_id,r.row_ordinal,r.payload_json", from,
                ScopeWhere(sql, s, "r") + $" AND ({ownership}) AND {RawVisible("r.id", "r.source_id", "r.batch_id")}{await RawOwnerWhere(sql, s)}", "r.id", q);
        }
        string table = section switch { "lines" => "qsm_document_line", "settlements" => "qsm_settlement_line", "writeoffs" => "qsm_writeoff_line", "relations" => "qsm_document_relation", _ => throw new QisemiReadException(404, "明细分区不存在") };
        string column = section == "relations" ? "from_version_id" : "document_version_id";
        string where = ScopeWhere(sql, s, "x") + sql.Optional("x." + column, vid) + $" AND {RawVisible("x.raw_record_id", "x.source_id", "x.batch_id")}";
        return await sql.Page("x.*", $"dbo.{table} x", where, section == "lines" ? "x.line_no,x.id" : "x.id", q);
    }

    private sealed record ArchiveSpec(string Table, string? Kind = null, string? Value = null);
    private static readonly IReadOnlyDictionary<string, ArchiveSpec> ArchiveSpecs = new Dictionary<string, ArchiveSpec>
    {
        ["products_all"] = new("product_sku", "record_kind", "product"), ["accessories"] = new("product_sku", "record_kind", "accessory"),
        ["product_classes_tree"] = new("category", "category_kind", "product"), ["client_classes"] = new("category", "category_kind", "client"),
        ["supplier_classes"] = new("category", "category_kind", "supplier"), ["units"] = new("unit"), ["warehouses"] = new("warehouse"),
        ["branches"] = new("branch"), ["warehouse_positions"] = new("warehouse_position"), ["clients"] = new("partner", "partner_kind", "client"),
        ["suppliers"] = new("partner", "partner_kind", "supplier"), ["accounts_all"] = new("account"), ["employees_all"] = new("employee"),
        ["roles"] = new("role"), ["projects"] = new("project")
    };
    private static async Task<object> Archives(QisemiSql sql, QisemiScope s, QisemiQuery q, string module, long? id)
    {
        if (!ArchiveSpecs.TryGetValue(module, out var spec)) throw new QisemiReadException(404, "档案模块不存在");
        string where = $"x.source_id={sql.Add(s.SourceId)} AND {MasterVisible("x")}" + sql.Optional("x." + spec.Kind, spec.Value)
            + sql.Optional("x.source_enabled", q.SourceEnabled) + sql.Optional("x.source_deleted", q.SourceDeleted);
        // Avoid referencing table-specific columns in a shared query.
        where += sql.Search("x.name", q.Keyword);
        if (id != null)
        {
            var row = await sql.One($"SELECT x.* FROM dbo.qsm_{spec.Table} x WHERE {where}{sql.Optional("x.id", id)}");
            var raw = row["lastRawRecordId"] is string rid ? await Record(sql, s, long.Parse(rid), false) : null;
            return new { archive = row, raw };
        }
        return await sql.Page("x.*", $"dbo.qsm_{spec.Table} x", where, "x.name,x.id", q);
    }

    private static async Task<string> RawOwnerWhere(QisemiSql sql, QisemiScope scope, string alias = "r")
    {
        if (sql.HiddenRawIds == null)
        {
            string source = sql.Add(scope.SourceId);
            var branches = new List<string>();
            foreach (string table in new[] { "product_sku", "category", "unit", "branch", "warehouse", "warehouse_position", "partner", "account", "employee", "role", "project" })
                branches.Add($"SELECT last_raw_record_id raw_id FROM dbo.qsm_{table} WHERE source_id={source} AND valid=0 AND last_raw_record_id IS NOT NULL");
            string invalidVersion = $"FROM dbo.qsm_document_version ov JOIN dbo.qsm_document od ON od.id=ov.document_id AND od.source_id=ov.source_id WHERE ov.source_id={source} AND (ov.valid=0 OR od.valid=0)";
            branches.Add($"SELECT ov.raw_record_id raw_id {invalidVersion}");
            branches.Add($"SELECT TRY_CONVERT(bigint,JSON_VALUE(ov.detail_coverage_json,'$.supplement_raw_id')) raw_id {invalidVersion}");
            foreach (string child in new[] { "document_line", "settlement_line", "writeoff_line" })
                branches.Add($"SELECT oc.raw_record_id raw_id FROM dbo.qsm_{child} oc JOIN dbo.qsm_document_version cv ON cv.id=oc.document_version_id AND cv.source_id=oc.source_id JOIN dbo.qsm_document cd ON cd.id=cv.document_id AND cd.source_id=cv.source_id WHERE oc.source_id={source} AND (oc.valid=0 OR cv.valid=0 OR cd.valid=0)");
            foreach (string child in new[] { "stock_total", "stock_snapshot", "stock_movement", "sales_report_line" })
                branches.Add($"SELECT raw_record_id raw_id FROM dbo.qsm_{child} WHERE source_id={source} AND valid=0");
            branches.Add($"SELECT fr.raw_record_id raw_id FROM dbo.qsm_file_ref fr WHERE fr.source_id={source} AND (NOT ({FileRefVisible("fr")}) OR (fr.file_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.qsm_file ff WHERE ff.id=fr.file_id AND ff.source_id=fr.source_id AND ff.valid=1)))");
            branches.Add($"SELECT rr.id raw_id FROM dbo.qsm_export_sheet sh JOIN dbo.qsm_raw_record rr ON rr.sheet_id=sh.id AND rr.source_id=sh.source_id JOIN dbo.qsm_file sf ON sf.id=sh.file_id AND sf.source_id=sh.source_id WHERE sh.source_id={source} AND (sh.valid=0 OR sf.valid=0)");
            // Materialize the deletion mask once. Correlating all owner checks into a
            // paged raw query produces pathological nested-loop plans on full captures.
            var hidden = await sql.Rows("SET NOCOUNT ON; DECLARE @blocked TABLE(raw_id bigint); " + string.Join("; ", branches.Select(b => "INSERT INTO @blocked(raw_id) " + b)) + "; SELECT DISTINCT raw_id FROM @blocked WHERE raw_id IS NOT NULL;");
            sql.HiddenRawIds = hidden.Count == 0 ? "" : sql.Add(System.Text.Json.JsonSerializer.Serialize(hidden.Select(r => r["rawId"])));
        }
        return sql.HiddenRawIds.Length == 0 ? "" : $" AND {alias}.id NOT IN (SELECT CONVERT(bigint,[value]) FROM OPENJSON({sql.HiddenRawIds}))";
    }
    private static async Task<object> Records(QisemiSql sql, QisemiScope s, QisemiQuery q, string? key)
    {
        string? module = key ?? q.Module;
        string from = "dbo.qsm_raw_record r JOIN dbo.qsm_import_module m ON m.id=r.module_id AND m.source_id=r.source_id AND m.batch_id=r.batch_id AND m.valid=1";
        string where = ScopeWhere(sql, s, "r") + sql.Optional("m.module_key", module) + sql.Optional("m.carrier", q.Carrier)
            + $" AND {RawVisible("r.id", "r.source_id", "r.batch_id")}" + await RawOwnerWhere(sql, s)
            + sql.Search("r.payload_json", q.Keyword);
        if (module != null)
        {
            var imported = await sql.One($"SELECT id FROM dbo.qsm_import_module WHERE source_id={sql.Add(s.SourceId)} AND batch_id={sql.Add(s.BatchId)} AND valid=1 AND module_key={sql.Add(module)}");
            where += sql.Optional("r.module_id", long.Parse((string)imported["id"]!));
        }
        return await sql.Page("r.id,r.module_id,m.module_key,m.carrier,r.row_ordinal,r.source_entity_key,r.parent_source_key,r.duplicate_ordinal,r.sheet_id,r.payload_json", from, where, "r.module_id,r.row_ordinal,r.id", q);
    }
    private static async Task<object> Record(QisemiSql sql, QisemiScope s, long id, bool sameBatch = true)
    {
        string where = $"r.valid=1 AND r.source_id={sql.Add(s.SourceId)} AND r.id={sql.Add(id)} AND {RawVisible("r.id", "r.source_id", "r.batch_id")}" + await RawOwnerWhere(sql, s);
        if (sameBatch) where += sql.Optional("r.batch_id", s.BatchId);
        var record = await sql.One($"SELECT r.id,r.module_id,r.batch_id,m.module_key,m.carrier,r.row_ordinal,r.source_entity_key,r.parent_source_key,r.payload_hash,r.duplicate_ordinal,r.sheet_id,r.payload_json FROM dbo.qsm_raw_record r JOIN dbo.qsm_import_module m ON m.id=r.module_id AND m.source_id=r.source_id AND m.batch_id=r.batch_id AND m.valid=1 WHERE {where}");
        // Resolve candidate version IDs before the visibility joins. An OR with a
        // correlated line lookup can rescan all document lines for every version.
        string source = sql.Add(s.SourceId), rawId = sql.Add(id);
        var documents = await sql.Rows($@"SET NOCOUNT ON; DECLARE @versions TABLE(id bigint NOT NULL PRIMARY KEY);
            INSERT INTO @versions(id)
                SELECT id FROM dbo.qsm_document_version WHERE source_id={source} AND raw_record_id={rawId}
                UNION SELECT document_version_id FROM dbo.qsm_document_line WHERE source_id={source} AND valid=1 AND raw_record_id={rawId};
            SELECT DISTINCT v.document_id,v.id version_id,v.version_kind,d.document_type FROM @versions candidates
            JOIN dbo.qsm_document_version v ON v.id=candidates.id
            JOIN dbo.qsm_document d ON d.id=v.document_id AND d.source_id=v.source_id AND d.valid=1 WHERE {ScopeWhere(sql, s, "v")} AND {DocumentVisible("v")}");
        return new { record, documents };
    }

    private static Task<Dictionary<string, object?>> Movements(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        string where = ScopeWhere(sql, s, "x") + $" AND {RawVisible("x.raw_record_id", "x.source_id", "x.batch_id")}" + sql.Optional("x.sku_id", q.SkuId)
            + sql.Optional("x.warehouse_id", q.WarehouseId) + sql.Optional("x.document_id", q.DocumentId) + sql.Optional("x.document_version_id", q.VersionId)
            + sql.Dates("x.business_at", q) + sql.Search("CONCAT(x.document_no,N' ',k.name,N' ',k.code)", q.Keyword)
            + " AND (x.sku_id IS NULL OR k.id IS NOT NULL) AND (x.warehouse_id IS NULL OR w.id IS NOT NULL) AND (x.document_id IS NULL OR d.id IS NOT NULL)"
            + $" AND (x.document_version_id IS NULL OR EXISTS(SELECT 1 FROM dbo.qsm_document_version mv WHERE mv.id=x.document_version_id AND mv.source_id=x.source_id AND mv.batch_id=x.batch_id AND {DocumentVisible("mv")}))";
        string from = $@"dbo.qsm_stock_movement x LEFT JOIN dbo.qsm_product_sku k ON k.id=x.sku_id AND k.source_id=x.source_id AND {MasterVisible("k")}
            LEFT JOIN dbo.qsm_warehouse w ON w.id=x.warehouse_id AND w.source_id=x.source_id AND {MasterVisible("w")}
            LEFT JOIN dbo.qsm_document d ON d.id=x.document_id AND d.source_id=x.source_id AND d.valid=1";
        return sql.Page("x.*,k.name product_name,k.code product_code,k.size_label,w.name warehouse_name,d.document_type", from, where, "x.business_at DESC,x.id DESC", q);
    }
    private static Task<Dictionary<string, object?>> SalesLines(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        string from = "dbo.qsm_sales_report_line x LEFT JOIN dbo.qsm_document d ON d.id=x.document_id AND d.source_id=x.source_id AND d.valid=1";
        string where = ScopeWhere(sql, s, "x") + $" AND {RawVisible("x.raw_record_id", "x.source_id", "x.batch_id")}" + sql.Optional("x.sku_id", q.SkuId)
            + sql.Optional("x.document_id", q.DocumentId) + sql.Dates("x.business_at", q)
            + sql.Search("CONCAT(x.document_no,N' ',x.product_name,N' ',x.product_code)", q.Keyword) + " AND (x.document_id IS NULL OR d.id IS NOT NULL)"
            + $" AND (x.sku_id IS NULL OR EXISTS(SELECT 1 FROM dbo.qsm_product_sku sk WHERE sk.id=x.sku_id AND sk.source_id=x.source_id AND {MasterVisible("sk")}))"
            + $" AND (x.document_version_id IS NULL OR EXISTS(SELECT 1 FROM dbo.qsm_document_version sv WHERE sv.id=x.document_version_id AND sv.source_id=x.source_id AND sv.batch_id=x.batch_id AND {DocumentVisible("sv")}))";
        return sql.Page("x.*,d.document_type", from, where, "x.business_at DESC,x.id DESC", q);
    }
}

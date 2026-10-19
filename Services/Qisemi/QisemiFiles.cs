#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Services.Qisemi;

public sealed record QisemiFileHandle(Stream Content, string Name, string ContentType, bool Inline, IDisposable? Owner = null);
public interface IQisemiFileReader
{
    Task<QisemiFileHandle> OpenAsync(string provider, string objectKey, string name, string? mimeType, CancellationToken ct);
}

public sealed class QisemiS3FileReader : IQisemiFileReader, IDisposable
{
    private readonly Lazy<IAmazonS3> s3;
    private readonly FileStorageOptions options;
    public QisemiS3FileReader(FileStorageOptions options)
    {
        this.options = options;
        // SQL-only requests must work on development PCs without an AWS identity.
        s3 = new(() => new AmazonS3Client(RegionEndpoint.GetBySystemName(options.Region)));
    }
    public QisemiS3FileReader(FileStorageOptions options, IAmazonS3 s3) { this.s3 = new(() => s3); this.options = options; }
    public async Task<QisemiFileHandle> OpenAsync(string provider, string objectKey, string name, string? mimeType, CancellationToken ct)
    {
        if (provider != "s3" || !objectKey.StartsWith("private/qisemi/datasets/", StringComparison.Ordinal)
            || objectKey.Split('/').Any(p => p is ".." or "." || p.Contains('\\')))
            throw new QisemiReadException(409, "文件存储状态不可用");
        try
        {
            var response = await s3.Value.GetObjectAsync(new GetObjectRequest { BucketName = options.Bucket, Key = objectKey }, ct);
            string type = mimeType is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp" or "image/heic"
                ? mimeType : UploadPaths.ContentType(name);
            if (type == "application/octet-stream") type = UploadPaths.ContentType(objectKey);
            bool inline = type is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp" or "image/heic";
            return new(response.ResponseStream, QisemiRules.SafeName(name), type, inline, response);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        { throw new QisemiReadException(404, "文件未找到"); }
    }
    public void Dispose() { if (s3.IsValueCreated) s3.Value.Dispose(); }
}

public sealed partial class QisemiReadService
{
    private static string FileRefVisible(string x = "x") => $@"{x}.valid=1 AND {RawVisible(x + ".raw_record_id", x + ".source_id", x + ".batch_id")}
        AND ({x}.sku_id IS NULL OR EXISTS(SELECT 1 FROM dbo.qsm_product_sku fk WHERE fk.id={x}.sku_id AND fk.source_id={x}.source_id AND {MasterVisible("fk")}))
        AND ({x}.partner_id IS NULL OR EXISTS(SELECT 1 FROM dbo.qsm_partner fp WHERE fp.id={x}.partner_id AND fp.source_id={x}.source_id AND {MasterVisible("fp")}))
        AND ({x}.document_version_id IS NULL OR EXISTS(SELECT 1 FROM dbo.qsm_document_version fv WHERE fv.id={x}.document_version_id AND fv.source_id={x}.source_id AND fv.batch_id={x}.batch_id AND {DocumentVisible("fv")}))";

    private static async Task<object> FileList(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        string from = $@"dbo.qsm_file_ref x LEFT JOIN dbo.qsm_file f ON f.id=x.file_id AND f.source_id=x.source_id AND f.valid=1
            LEFT JOIN dbo.qsm_product_sku k ON k.id=x.sku_id AND k.source_id=x.source_id AND {MasterVisible("k")}
            LEFT JOIN dbo.qsm_document_version v ON v.id=x.document_version_id AND v.source_id=x.source_id AND {DocumentVisible("v")}";
        string where = ScopeWhere(sql, s, "x") + $" AND {FileRefVisible()} AND (x.file_id IS NULL OR f.id IS NOT NULL)"
            + sql.Optional("x.sku_id", q.SkuId) + sql.Optional("x.document_version_id", q.VersionId)
            + sql.Optional("v.document_id", q.DocumentId) + sql.Optional("x.download_state", q.State)
            + sql.Search("CONCAT(x.original_name,N' ',k.name,N' ',k.code,N' ',v.document_no)", q.Keyword);
        if (!string.IsNullOrWhiteSpace(q.FileType))
        {
            if (q.FileType == "image") where += " AND f.mime_type LIKE N'image/%'";
            else if (q.FileType == "attachment") where += " AND x.reference_kind IN ('document_file','attachment','other')";
            else if (q.FileType == "export") where += sql.Optional("x.reference_kind", q.FileType);
            else where += sql.Optional("f.extension", "." + q.FileType.TrimStart('.').ToLowerInvariant());
        }
        if(q.StyleId!=null)where+=$" AND EXISTS(SELECT 1 FROM dbo.qsm_style_sku fs JOIN dbo.qsm_product_style fp ON fp.id=fs.style_id AND fp.source_id=fs.source_id AND fp.valid=1 WHERE fs.valid=1 AND fs.source_id=x.source_id AND fs.sku_id=x.sku_id AND fs.style_id={sql.Add(q.StyleId)})";
        var result = await sql.Page("x.id,x.file_id,x.sku_id,x.document_version_id,x.partner_id,x.reference_kind,x.original_name,x.is_primary,x.sort,x.download_state,x.error_summary,f.byte_length,f.mime_type,f.extension,f.availability_state,k.name product_name,v.document_no",
            from, where, "x.is_primary DESC,x.sort,x.id", q);
        foreach (var row in (List<Dictionary<string, object?>>)result["items"]!)
            row["contentPath"] = row["fileId"] == null ? null : $"/api/Qisemi/FileReferences/{row["id"]}/Content?sourceId={s.SourceId}&batchId={s.BatchId}";
        return result;
    }

    public async Task<QisemiFileHandle> FileAsync(long referenceId, QisemiQuery query, CancellationToken ct)
    {
        query.Validate();
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var sql = new QisemiSql(connection, tx, ct); var scope = await Scope(sql, query, false);
        var row = await sql.One($@"SELECT f.storage_provider,f.object_key storage_key,f.mime_type,x.original_name,x.download_state,f.availability_state
            FROM dbo.qsm_file_ref x JOIN dbo.qsm_file f ON f.id=x.file_id AND f.source_id=x.source_id AND f.valid=1
            JOIN dbo.qsm_import_batch fb ON fb.id=f.first_seen_batch_id AND fb.source_id=f.source_id AND fb.valid=1 AND fb.state='published' AND fb.verification_passed=1
            WHERE {ScopeWhere(sql, scope, "x")} AND x.id={sql.Add(referenceId)} AND {FileRefVisible()}");
        if ((string?)row["downloadState"] != "complete" || (string?)row["availabilityState"] != "ready")
            throw new QisemiReadException(409, "文件尚未准备完成");
        // No URL, access key, or private object key is sent to the browser.
        var file = await files.OpenAsync((string)row["storageProvider"]!, (string)row["storageKey"]!,
            QisemiRules.SafeName((string?)row["originalName"]), (string?)row["mimeType"], ct);
        try { await tx.CommitAsync(ct); return file; }
        catch { file.Content.Dispose(); file.Owner?.Dispose(); throw; }
    }

    private static async Task<object> Sheets(QisemiSql sql, QisemiScope s, QisemiQuery q)
    {
        string from = "dbo.qsm_export_sheet x JOIN dbo.qsm_import_module m ON m.id=x.module_id AND m.source_id=x.source_id AND m.batch_id=x.batch_id AND m.valid=1 JOIN dbo.qsm_file f ON f.id=x.file_id AND f.source_id=x.source_id AND f.valid=1";
        return await sql.Page("x.*,m.state module_state", from, ScopeWhere(sql, s, "x") + sql.Search("CONCAT(x.export_key,N' ',x.sheet_name)", q.Keyword), "x.export_key,x.sheet_index,x.id", q);
    }
    private static async Task<object> SheetRows(QisemiSql sql, QisemiScope s, QisemiQuery q, long id)
    {
        var sheet = await sql.One($@"SELECT x.* FROM dbo.qsm_export_sheet x
            JOIN dbo.qsm_import_module m ON m.id=x.module_id AND m.source_id=x.source_id AND m.batch_id=x.batch_id AND m.valid=1 AND m.state IN ('complete','empty')
            JOIN dbo.qsm_file f ON f.id=x.file_id AND f.source_id=x.source_id AND f.valid=1 WHERE {ScopeWhere(sql, s, "x")} AND x.id={sql.Add(id)}");
        var rows = await sql.Page("r.id,r.row_ordinal,r.payload_json", "dbo.qsm_raw_record r", ScopeWhere(sql, s, "r") + sql.Optional("r.sheet_id", id)
            + $" AND {RawVisible("r.id", "r.source_id", "r.batch_id")}" + await RawOwnerWhere(sql, s) + sql.Search("r.payload_json", q.Keyword), "r.row_ordinal,r.id", q);
        return new { sheet, rows };
    }
}

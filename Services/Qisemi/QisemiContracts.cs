#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Qisemi;

public sealed class QisemiQuery
{
    [Range(1, long.MaxValue)] public long? SourceId { get; set; }
    [Range(1, long.MaxValue)] public long? BatchId { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 30;
    [StringLength(160)] public string? Keyword { get; set; }
    [Range(1, long.MaxValue)] public long? WarehouseId { get; set; }
    [Range(1, long.MaxValue)] public long? SkuId { get; set; }
    [Range(1, long.MaxValue)] public long? StyleId { get; set; }
    [Range(1, long.MaxValue)] public long? DocumentId { get; set; }
    [Range(1, long.MaxValue)] public long? VersionId { get; set; }
    [Range(1, long.MaxValue)] public long? CategoryId { get; set; }
    [Range(1, long.MaxValue)] public long? TopCategoryId { get; set; }
    [Range(0, long.MaxValue)] public long? ResultCategoryId { get; set; }
    [Range(1, long.MaxValue)] public long? ResultWarehouseId { get; set; }
    [RegularExpression("^(name|topCategory)$")] public string SortBy { get; set; } = "name";
    [RegularExpression("^(asc|desc)$")] public string SortDirection { get; set; } = "asc";
    [StringLength(160)] public string? WarehouseKeyword { get; set; }
    [StringLength(64)] public string? Module { get; set; }
    [StringLength(40)] public string? DocumentType { get; set; }
    [StringLength(100)] public string? State { get; set; }
    [StringLength(32)] public string? FileType { get; set; }
    [StringLength(24)] public string? Carrier { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public bool IncludeZero { get; set; }
    public bool NegativeOnly { get; set; }
    public bool InStockOnly { get; set; }
    public bool? WriteBack { get; set; }
    public bool? SourceEnabled { get; set; }
    public bool? SourceDeleted { get; set; }

    public void Validate()
    {
        Validator.ValidateObject(this, new ValidationContext(this), true);
        if ((long)(Page - 1) * PageSize > 10000000) throw new ArgumentException("分页偏移过大");
        if (DateFrom > DateTo) throw new ArgumentException("开始日期不能晚于结束日期");
        if (DateTo == DateOnly.MaxValue) throw new ArgumentException("结束日期超出范围");
    }
}

// Only this context needs to change when employee authorization is enabled later.
// The present release deliberately accepts absent or arbitrary session headers.
public sealed class QisemiSessionContext
{
    public string? SessionKey { get; internal set; }
    public string? SessionType { get; internal set; }
}

public sealed class QisemiReadException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IQisemiConnectionFactory { DbConnection Create(); }
public sealed class QisemiConnectionFactory(ApplicationDBContext db) : IQisemiConnectionFactory
{
    public DbConnection Create() => new SqlConnection(db.Database.GetConnectionString());
}
public interface IQisemiReadService
{
    Task<object> ReadAsync(string operation, QisemiQuery query, long? id, string? key, CancellationToken cancellationToken);
    Task<QisemiFileHandle> FileAsync(long referenceId, QisemiQuery query, CancellationToken cancellationToken);
}

public static class QisemiRules
{
    public static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Like(string value) => "%" + value.Replace("~", "~~").Replace("%", "~%")
        .Replace("_", "~_").Replace("[", "~[") + "%";
    public static string[] Tokens(string? keyword) => Regex.Split((keyword ?? "").Trim(), @"\s+")
        .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
    public static string Camel(string name) => Regex.Replace(name, "_([a-z])", m => m.Groups[1].Value.ToUpperInvariant());
    private static readonly HashSet<string> PrivateFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "access_token", "accessToken", "refresh_token", "refreshToken", "authorization", "cookie", "set-cookie",
        "sessionKey", "sessionToken", "loginToken", "object_key", "objectKey", "storage_key", "storageKey", "local_path", "localPath"
    };
    public static JsonElement Json(string value)
    {
        var root = JsonNode.Parse(value);
        void Scrub(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var key in obj.Select(p => p.Key).ToArray())
                    if (PrivateFields.Contains(key)) obj.Remove(key); else Scrub(obj[key]);
            else if (node is JsonArray arr) foreach (var child in arr) Scrub(child);
        }
        Scrub(root); return JsonSerializer.SerializeToElement(root);
    }
    public static string SafeName(string? value)
    {
        string name = (value ?? "download").Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(c => !char.IsControl(c) && c != '"').Take(200).ToArray());
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }
}

internal sealed record QisemiScope(long SourceId, long BatchId, Dictionary<string, object?> Snapshot);

public sealed record QisemiModule(string Key, string Label, string Tab, string Section, string Kind,
    string[] Sources, string[]? Native, string? DocumentType);

public static class QisemiCatalog
{
    public static IReadOnlyList<QisemiModule> Modules { get; } = Load();
    private static QisemiModule[] Load()
    {
        using var stream = typeof(QisemiCatalog).Assembly.GetManifestResourceStream("SnowmeetApi.QisemiModules.json")!;
        return JsonSerializer.Deserialize<QisemiModule[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    public static QisemiModule Get(string? key) => Modules.SingleOrDefault(x => x.Key == key)
        ?? throw new QisemiReadException(404, "模块不存在");
}

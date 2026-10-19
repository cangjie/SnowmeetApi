#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SnowmeetApi.Services.Qisemi;

// All identifiers come from code-owned queries/catalog entries; user values are always parameters.
internal sealed class QisemiSql(DbConnection connection, DbTransaction transaction, CancellationToken ct)
{
    public Dictionary<string, object?> Args { get; } = new();
    public string? HiddenRawIds { get; set; }
    public string Add(object? value)
    {
        string key = "@p" + Args.Count; Args.Add(key, value); return key;
    }
    public async Task<List<Dictionary<string, object?>>> Rows(string sql)
    {
        using var cmd = connection.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = sql; cmd.CommandTimeout = 30;
        foreach (var pair in Args)
        {
            var p = cmd.CreateParameter(); p.ParameterName = pair.Key; p.Value = pair.Value ?? DBNull.Value;
            if (pair.Value is string) p.DbType = DbType.String;
            cmd.Parameters.Add(p);
        }
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                if (name is "payload_gzip" or "row_version" or "object_key" or "source_url_sanitized") continue;
                object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                value = value switch
                {
                    long n => QisemiRules.Id(n),
                    byte[] b => Convert.ToHexString(b).ToLowerInvariant(),
                    DateTime d when name == "business_at" => DateTime.SpecifyKind(d, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "+08:00",
                    DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    Guid g => g.ToString(),
                    string s when name.EndsWith("_json", StringComparison.Ordinal) => QisemiRules.Json(s),
                    _ => value
                };
                row[QisemiRules.Camel(name)] = value;
            }
            result.Add(row);
        }
        return result;
    }
    public async Task<Dictionary<string, object?>> One(string sql) => (await Rows(sql)).SingleOrDefault()
        ?? throw new QisemiReadException(404, "记录不存在或已删除");
    public async Task<Dictionary<string, object?>> Page(string fields, string from, string where, string order, QisemiQuery q)
    {
        var count = await One($"SELECT COUNT_BIG(*) AS total FROM {from} WHERE {where}");
        string skip = Add((q.Page - 1) * q.PageSize), take = Add(q.PageSize);
        var rows = await Rows($"SELECT {fields} FROM {from} WHERE {where} ORDER BY {order} OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY");
        return new() { ["items"] = rows, ["total"] = count["total"], ["page"] = q.Page, ["pageSize"] = q.PageSize };
    }
    public string Search(string expression, string? keyword)
        => string.Concat(QisemiRules.Tokens(keyword).Select(t => $" AND {expression} LIKE {Add(QisemiRules.Like(t))} ESCAPE N'~'"));
    public string Dates(string column, QisemiQuery q)
    {
        string result = "";
        if (q.DateFrom is { } f) result += $" AND {column}>={Add(f.ToDateTime(TimeOnly.MinValue))}";
        if (q.DateTo is { } t) result += $" AND {column}<{Add(t.AddDays(1).ToDateTime(TimeOnly.MinValue))}";
        return result;
    }
    public string Optional(string column, object? value) => value == null ? "" : $" AND {column}={Add(value)}";
}

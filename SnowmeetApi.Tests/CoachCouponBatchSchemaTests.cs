using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 优惠券批次两张表（coach_coupon_batch / coach_coupon_claim）：建表脚本和 EF 模型必须一一对应（列、长度、唯一索引）。
/// 测试用 SQLite 按模型建表，抓不到「脚本列名拼错」或「库里有 NOT NULL 列而模型没有」这类漂移，所以单独对照。
/// </summary>
public sealed class CoachCouponBatchSchemaTests
{
    private static string ReadScript(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "snowmeet_ai_doc", "sql", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        Assert.Fail("需要相邻 snowmeet_ai_doc 仓库里的 sql/" + fileName);
        return "";
    }

    private static ApplicationDBContext ModelOnly() => new(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite("Data Source=:memory:").Options);

    private static Dictionary<string, string> ScriptColumns(string sql, string table)
    {
        var block = Regex.Match(sql, @"CREATE TABLE dbo\." + table + @" \((?<body>.*?)\n\s+\);", RegexOptions.Singleline);
        Assert.True(block.Success, "脚本里没有 " + table + " 的建表语句");
        return Regex.Matches(block.Groups["body"].Value, @"^\s+(?<col>[a-z_]+)\s+(?<type>VARCHAR\(\d+\)|(?:INT|DATETIME|DATE)\b)", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups["col"].Value, m => m.Groups["type"].Value);
    }

    [Theory]
    [InlineData(typeof(CoachCouponBatch), "coach_coupon_batch")]
    [InlineData(typeof(CoachCouponClaim), "coach_coupon_claim")]
    public void The_script_creates_exactly_the_columns_the_model_maps(Type clr, string table)
    {
        string sql = ReadScript("2026-11-18_coach_coupon_batch.sql");
        using var db = ModelOnly();
        var entity = db.Model.FindEntityType(clr)!;
        Assert.Equal(table, entity.GetTableName());
        var script = ScriptColumns(sql, table);
        Assert.Equal(entity.GetProperties().Select(p => p.GetColumnName()).OrderBy(c => c), script.Keys.OrderBy(c => c));
        // 长度对上：脚本里的 VARCHAR(n) = 模型里的 MaxLength
        foreach (var p in entity.GetProperties().Where(p => p.GetMaxLength() != null))
            Assert.Equal("VARCHAR(" + p.GetMaxLength() + ")", script[p.GetColumnName()]);
        Assert.Equal("DATE", ScriptColumns(sql, "coach_coupon_claim")["claim_date"]);
    }

    [Fact]
    public void The_unique_indexes_in_the_script_are_the_ones_the_model_declares_and_the_script_is_rerunnable()
    {
        string sql = ReadScript("2026-11-18_coach_coupon_batch.sql");
        using var db = ModelOnly();
        foreach (var clr in new[] { typeof(CoachCouponBatch), typeof(CoachCouponClaim) })
            foreach (var index in db.Model.FindEntityType(clr)!.GetIndexes())
            {
                string name = index.GetDatabaseName()!;
                Assert.Contains(name, sql);
                Assert.Contains((index.IsUnique ? "CREATE UNIQUE INDEX " : "CREATE INDEX ") + name, sql);
            }
        Assert.Contains("IF OBJECT_ID(N'dbo.coach_coupon_batch', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'dbo.coach_coupon_claim', N'U') IS NULL", sql);
        Assert.Contains("UNIQUE INDEX UX_coach_coupon_claim_member_day ON dbo.coach_coupon_claim(member_id, claim_date)", sql);   // 每个会员每天只能领一次
        Assert.Contains("UNIQUE INDEX UX_coach_coupon_claim_batch_member ON dbo.coach_coupon_claim(batch_id, member_id)", sql);   // 同一批次只能领一次
        Assert.Contains("claimed_count <= total_count", sql);                                                                      // 库层面也不会领超
    }

    [Fact]
    public void The_rollback_script_drops_both_tables_and_can_run_twice()
    {
        string sql = ReadScript("2026-11-18_coach_coupon_batch_rollback.sql");
        Assert.Contains("IF OBJECT_ID(N'dbo.coach_coupon_claim', N'U') IS NOT NULL DROP TABLE dbo.coach_coupon_claim;", sql);
        Assert.Contains("IF OBJECT_ID(N'dbo.coach_coupon_batch', N'U') IS NOT NULL DROP TABLE dbo.coach_coupon_batch;", sql);
    }
}

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// member 表的三个可空列（昵称、头像、更新时间）：加列脚本和 EF 小实体必须一一对应。
/// 测试用 SQLite 按模型建表，抓不到「库里有 NOT NULL 列而模型没有」或「脚本列名拼错」这类漂移，所以单独对照。
/// </summary>
public sealed class MemberProfileSchemaTests
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

    private static ApplicationDBContext ModelOnly() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
        .UseSqlite("Data Source=:memory:").Options);

    [Fact]
    public void SchemaScript_AddsExactlyTheNullableColumnsTheModelMaps()
    {
        string sql = ReadScript("2026-11-14_member_nick_avatar.sql");
        using var db = ModelOnly();
        var entity = db.Model.FindEntityType(typeof(MemberProfile))!;
        Assert.Equal("member", entity.GetTableName());

        var memberColumns = db.Model.FindEntityType(typeof(Member))!.GetProperties().Select(p => p.GetColumnName()).ToHashSet();
        var modelColumns = entity.GetProperties().Where(p => p.GetColumnName() != "id").ToList();
        Assert.Equal(new[] { "avatar", "nick_avatar_update_date", "nick_name" }, modelColumns.Select(p => p.GetColumnName()).OrderBy(c => c).ToArray());
        Assert.All(modelColumns, p => Assert.DoesNotContain(p.GetColumnName(), memberColumns));   // Member 实体本身不映射它们
        Assert.All(modelColumns, p => Assert.True(p.IsNullable));

        var scriptColumns = Regex.Matches(sql, @"ALTER\s+TABLE\s+dbo\.member\s+ADD\s+(?<col>\w+)\s+(?<type>\w+(?:\(\d+\))?)\s+NULL", RegexOptions.IgnoreCase)
            .ToDictionary(m => m.Groups["col"].Value, m => m.Groups["type"].Value.ToUpperInvariant());
        Assert.Equal(modelColumns.Select(p => p.GetColumnName()).OrderBy(c => c), scriptColumns.Keys.OrderBy(c => c));

        // 长度也要对上：脚本里的 NVARCHAR(n) = 模型里的 MaxLength
        Assert.Equal("NVARCHAR(64)", scriptColumns["nick_name"]);
        Assert.Equal(64, entity.FindProperty(nameof(MemberProfile.nick_name))!.GetMaxLength());
        Assert.Equal("NVARCHAR(255)", scriptColumns["avatar"]);
        Assert.Equal(255, entity.FindProperty(nameof(MemberProfile.avatar))!.GetMaxLength());
        Assert.Equal("DATETIME", scriptColumns["nick_avatar_update_date"]);

        foreach (string col in scriptColumns.Keys)
            Assert.Contains($"COL_LENGTH('dbo.member', '{col}')", sql);                              // 可重复执行
    }

    [Fact]
    public void RollbackScript_DropsTheSameThreeColumnsAndCanRunTwice()
    {
        string sql = ReadScript("2026-11-14_member_nick_avatar_rollback.sql");
        var dropped = Regex.Matches(sql, @"DROP\s+COLUMN\s+(?<col>\w+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups["col"].Value).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "avatar", "nick_avatar_update_date", "nick_name" }, dropped);
        foreach (string col in dropped)
            Assert.Contains($"COL_LENGTH('dbo.member', '{col}') IS NOT NULL", sql);
    }
}

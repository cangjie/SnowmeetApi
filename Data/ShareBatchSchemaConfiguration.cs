using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Data;

// 手动分账批次表的索引声明。与 snowmeet_ai_doc/sql/2026-11-04_share_batch.sql 保持一致；
// 生产库的表由那份脚本建，这里声明是为了让 SQLite 测试（EnsureCreated）也能校验唯一性。
internal static class ShareBatchSchemaConfiguration
{
    internal static void Configure(ModelBuilder m, bool isSqlite)
    {
        var batch = m.Entity<ShareBatch>();
        var no = batch.Property(x => x.batch_no).HasMaxLength(40);
        // SQL Server 默认排序规则本来就不分大小写；SQLite 默认分，测试里要显式声明才和生产一致
        if (isSqlite) no.UseCollation("NOCASE");
        batch.HasIndex(x => x.batch_no).IsUnique().HasDatabaseName("UX_share_batch_no");
    }
}

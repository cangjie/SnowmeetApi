using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Data;

// 介绍人表的索引声明。与 snowmeet_ai_doc/sql/2026-11-02_introducer.sql 保持一致；
// 生产库的表由那份脚本建，这里声明是为了让 SQLite 测试（EnsureCreated）也能校验唯一性。
internal static class IntroducerSchemaConfiguration
{
    internal static void Configure(ModelBuilder m)
    {
        var introducer = m.Entity<Introducer>();
        introducer.Property(x => x.type).HasMaxLength(10);
        introducer.Property(x => x.name).HasMaxLength(100);
        introducer.Property(x => x.real_name).HasMaxLength(50);
        // 一个会员同一时间只能是一位有效的介绍人
        introducer.HasIndex(x => x.member_id).IsUnique().HasFilter("[valid] = 1").HasDatabaseName("UX_introducer_member");
    }
}

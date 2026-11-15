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

        // 顾客归属：一位顾客同一时间只归属一位分销商（先到先得）；订单归属：同一单同一位分销商只记一次
        var customer = m.Entity<DistributorCustomer>();
        customer.Property(x => x.source).HasMaxLength(10);
        customer.Property(x => x.source_ref).HasMaxLength(100);
        customer.HasIndex(x => x.member_id).IsUnique().HasFilter("[valid] = 1").HasDatabaseName("UX_distributor_customer_member");
        customer.HasIndex(x => x.introducer_id).HasDatabaseName("IX_distributor_customer_introducer");
        var order = m.Entity<DistributorOrder>();
        order.Property(x => x.source).HasMaxLength(10);
        order.HasIndex(x => new { x.order_id, x.introducer_id }).IsUnique().HasDatabaseName("UX_distributor_order");
        order.HasIndex(x => x.introducer_id).HasDatabaseName("IX_distributor_order_introducer");
    }
}

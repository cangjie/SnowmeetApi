using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Data;

// 教练 / 学员卡表的索引声明。与 snowmeet_ai_doc/sql/2026-10-30_coach_platform.sql 保持一致；
// 生产库的表由那份脚本建，这里声明索引是为了让 SQLite 测试（EnsureCreated）也能校验唯一性。
internal static class CoachSchemaConfiguration
{
    internal static void Configure(ModelBuilder m)
    {
        var coach = m.Entity<Coach>();
        coach.Property(x => x.display_name).HasMaxLength(100);
        // 一个会员同一时间只能是一位启用中的教练
        coach.HasIndex(x => x.member_id).IsUnique().HasFilter("[valid] = 1").HasDatabaseName("UX_coach_member");

        var price = m.Entity<CoachCarePrice>();
        price.Property(x => x.shop).HasMaxLength(50);
        price.Property(x => x.tier).HasMaxLength(10);
        price.Property(x => x.mode).HasMaxLength(10);
        price.HasIndex(x => new { x.coach_id, x.shop, x.tier }).IsUnique().HasDatabaseName("UX_coach_care_price");

        var card = m.Entity<CoachCard>();
        card.Property(x => x.token).HasMaxLength(32);
        card.Property(x => x.status).HasMaxLength(12);
        card.Property(x => x.remark).HasMaxLength(200);
        card.HasIndex(x => x.token).IsUnique().HasDatabaseName("UX_coach_card_token");
        card.HasIndex(x => new { x.coach_id, x.status, x.create_date }).HasDatabaseName("IX_coach_card_coach");
        card.HasIndex(x => new { x.member_id, x.status }).HasDatabaseName("IX_coach_card_member");
        // 同一学员本雪季对同一位教练只能有一张已领取的卡（并发领取的兜底）
        card.HasIndex(x => new { x.coach_id, x.member_id, x.season_end_year }).IsUnique()
            .HasFilter("[member_id] IS NOT NULL AND [status] = 'claimed'").HasDatabaseName("UX_coach_card_holder");

        var use = m.Entity<CoachCardUse>();
        use.Property(x => x.biz_type).HasMaxLength(10);
        use.Property(x => x.mode).HasMaxLength(10);
        use.Property(x => x.status).HasMaxLength(12);
        use.Property(x => x.reverse_reason).HasMaxLength(200);
        // 每笔养护 / 每个租赁日只记一次，重复核销、支付回调重放都安全
        use.HasIndex(x => new { x.biz_type, x.biz_id, x.sub_biz_id }).IsUnique().HasDatabaseName("UX_coach_card_use_biz");
        use.HasIndex(x => new { x.coach_id, x.create_date }).HasDatabaseName("IX_coach_card_use_coach");
        use.HasIndex(x => x.card_id).HasDatabaseName("IX_coach_card_use_card");
        use.HasIndex(x => x.order_id).HasDatabaseName("IX_coach_card_use_order");

        var grant = m.Entity<CoachCouponGrant>();
        grant.Property(x => x.ticket_code).HasMaxLength(50);
        grant.Property(x => x.grant_key).HasMaxLength(100);
        grant.HasIndex(x => x.ticket_code).IsUnique().HasDatabaseName("UX_coach_coupon_grant_ticket");
        grant.HasIndex(x => x.grant_key).IsUnique().HasDatabaseName("UX_coach_coupon_grant_key");
        grant.HasIndex(x => x.coach_id).HasDatabaseName("IX_coach_coupon_grant_coach");
    }
}

using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Data;

internal static class TrialReservationSchemaConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var reservation = model.Entity<TrialReservation>();
        reservation.Property(x => x.reservation_date).HasColumnType("date");
        reservation.Property(x => x.status).HasMaxLength(16);
        reservation.Property(x => x.qr_token).HasMaxLength(24);
        reservation.HasIndex(x => new { x.member_id, x.reservation_date }).IsUnique()
            .HasFilter("[counts_for_day] = 1").HasDatabaseName("UX_trial_member_day");
        reservation.HasIndex(x => new { x.rent_product_id, x.reservation_date }).IsUnique()
            .HasFilter("[blocks_day] = 1").HasDatabaseName("UX_trial_board_day");
        reservation.HasIndex(x => new { x.rent_product_id, x.status }).HasDatabaseName("IX_trial_board_status");
        reservation.HasIndex(x => x.qr_token).IsUnique().HasFilter("[qr_token] IS NOT NULL")
            .HasDatabaseName("UX_trial_qr_token");
    }
}

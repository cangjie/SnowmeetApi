using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

// Absence means a legacy order whose benefit must not be automatically changed.
[Table("rental_benefit_preference")]
public sealed class RentalBenefitPreference
{
    [Key] public int order_id { get; set; }
    [MaxLength(16)] public string mode { get; set; } = "none"; // auto | none
    public int staff_id { get; set; }
    public DateTime update_date { get; set; } = DateTime.Now;
}

// An explicit per-rental choice wins over automatic matching.
[Table("rental_benefit_rental_preference")]
public sealed class RentalBenefitRentalPreference
{
    [Key] public int rental_id { get; set; }
    [MaxLength(16)] public string mode { get; set; } = "none"; // manual | none
    public int staff_id { get; set; }
    public DateTime update_date { get; set; } = DateTime.Now;
}

[Table("rental_benefit_detail_preference")]
public sealed class RentalBenefitDetailPreference
{
    [Key] public int rental_detail_id { get; set; }
    public int staff_id { get; set; }
    public DateTime update_date { get; set; } = DateTime.Now;
}

// A committed benefit is immutable; retries must not consume assets again.
[Table("rental_benefit_settlement")]
public sealed class RentalBenefitSettlement
{
    [Key] public int order_id { get; set; }
    [MaxLength(128)] public string? coupon_code { get; set; }
    public double rent_payable { get; set; }
    public int staff_id { get; set; }
    public DateTime create_date { get; set; } = DateTime.Now;
}

[Table("rental_season_card_use")]
public sealed class RentalSeasonCardUse
{
    [Key] public int id { get; set; }
    public int card_id { get; set; }
    public int order_id { get; set; }
    public int rental_detail_id { get; set; }
    [Column(TypeName = "date")] public DateTime rental_date { get; set; }
    public bool valid { get; set; } = true;
    public DateTime create_date { get; set; } = DateTime.Now;
}

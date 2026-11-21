using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

[Table("trial_board")]
public sealed class TrialBoard
{
    [Key] public int rent_product_id { get; set; }
    public int introducer_id { get; set; }
    public int valid { get; set; } = 1;
}

[Table("trial_reservation")]
public sealed class TrialReservation
{
    public const string Pending = "pending";
    public const string InUse = "in_use";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";

    [Key] public int id { get; set; }
    public int member_id { get; set; }
    public int rent_product_id { get; set; }
    public int introducer_id { get; set; }
    public DateTime reservation_date { get; set; }
    public string status { get; set; } = Pending;
    public int counts_for_day { get; set; } = 1;
    public int blocks_day { get; set; } = 1;
    public string? qr_token { get; set; }
    public DateTime? qr_expires_at { get; set; }
    public int? qr_staff_id { get; set; }
    public DateTime create_date { get; set; }
    public DateTime? checkin_date { get; set; }
    public DateTime? return_date { get; set; }
    public int? return_staff_id { get; set; }
    public DateTime? cancel_date { get; set; }
    public int? cancel_member_id { get; set; }
    public int? cancel_staff_id { get; set; }
}

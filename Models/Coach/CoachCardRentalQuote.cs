using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

/// <summary>租赁订单开单时选用的会籍卡折扣，后续按日计费沿用。</summary>
[Table("coach_card_rental_quote")]
public sealed class CoachCardRentalQuote
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public int rental_id { get; set; }
    public int card_id { get; set; }
    public double? rental_rate { get; set; }
    public DateTime create_date { get; set; }
}

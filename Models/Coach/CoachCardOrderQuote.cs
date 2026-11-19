using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

/// <summary>养护开单时确定的卡优惠，付款生效仍使用这一笔的报价。</summary>
[Table("coach_card_order_quote")]
public sealed class CoachCardOrderQuote
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public int care_id { get; set; }
    public int card_id { get; set; }
    [MaxLength(10)] public string mode { get; set; } = "none";
    public double mode_value { get; set; }
    public double base_amount { get; set; }
    public double discount_amount { get; set; }
    public DateTime create_date { get; set; }
}

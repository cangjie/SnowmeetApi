using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

[Table("card_order_refund_batch")]
public class CardOrderRefundBatch
{
    [Key] public int id { get; set; }
    public int order_id { get; set; }
    public string status { get; set; } = "processing";
    public decimal amount { get; set; }
    public DateTime create_date { get; set; } = DateTime.Now;
    public DateTime update_date { get; set; } = DateTime.Now;
}

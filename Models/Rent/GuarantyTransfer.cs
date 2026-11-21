using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

// 已解除占用的押金转用于同订单的新租赁商品；取消待支付追加时作废并退回来源。
[Table("guaranty_transfer")]
public class GuarantyTransfer
{
    [Key] public int id { get; set; }
    public int order_id { get; set; }
    public int source_guaranty_id { get; set; }
    public int target_guaranty_id { get; set; }
    public double amount { get; set; }
    public int valid { get; set; } = 1;
    public int? staff_id { get; set; }
    public DateTime create_date { get; set; } = DateTime.Now;
    public DateTime? update_date { get; set; }
}

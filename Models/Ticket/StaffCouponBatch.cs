using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

// 独立配置表避免更改旧分享批次模型，旧分享链接维持原规则。
[Table("staff_coupon_batch")]
public class StaffCouponBatch
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public int batch_id { get; set; }
    public int staff_id { get; set; }
    public int per_person_limit { get; set; }
    [MaxLength(10)] public string mode { get; set; } = "multiple";
    [MaxLength(32)] public string request_id { get; set; } = "";
}

[Table("staff_coupon_batch_ticket")]
public class StaffCouponBatchTicket
{
    [Key, MaxLength(20)] public string ticket_code { get; set; } = "";
    public int batch_id { get; set; }
    public int member_id { get; set; }
    public DateTime create_date { get; set; }
}

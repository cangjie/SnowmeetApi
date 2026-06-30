using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("punch_card_used")]
    public class PunchCardUsed
    {
        [Key]
        public int id { get; set; }
        public int card_id { get; set; }       // → punch_card.id
        public int order_id { get; set; }      // → [order].id
        public string biz_type { get; set; }   // '租赁'
        public int biz_id { get; set; }        // → rental.id（每条 rental 一条记录）
        public int? payment_id { get; set; }   // 次卡按次、不产生 OrderPayment，留空
        public int punch_count { get; set; }   // 本次扣的次数（该 rental 被免天数）
        public bool valid { get; set; } = true;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

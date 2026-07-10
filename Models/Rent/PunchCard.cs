using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("punch_card")]
    public class PunchCard
    {
        [Key]
        public int id { get; set; }
        public string biz_type { get; set; }      // '租赁' / '养护'
        public string card_name { get; set; }
        public int member_id { get; set; }
        public string? mi7_code { get; set; }
        public int? total { get; set; }            // 总次数；NULL = 季卡（不限次数，2026-07-09 起）
        public int? punches { get; set; }          // 已用次数累计（可空，空视为 0）
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;

        [NotMapped]
        public int? remaining => total == null ? null : total.Value - (punches ?? 0);   // 剩余次数；季卡 NULL
    }
}

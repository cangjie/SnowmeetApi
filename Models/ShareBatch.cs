using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 手动分账批次：店长在分账列表里把若干「未分 + 手动」的分账一次记成已分，这一次就是一个批次。
    /// 只记账，不转钱。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-03-share-list-manual-batch-design.md
    /// </summary>
    [Table("share_batch")]
    public class ShareBatch
    {
        [Key]
        public int id { get; set; }
        /// <summary>批次号，全局唯一（不分大小写），最长 40。</summary>
        public string batch_no { get; set; } = "";
        public int share_count { get; set; }
        public double total_amount { get; set; }
        public int created_by_staff_id { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

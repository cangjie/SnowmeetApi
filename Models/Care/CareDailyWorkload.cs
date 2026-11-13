using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 日养护工作列表的每日快照（一个门店一天一行）。统计规则见 Helpers/CareWorkloadRules.cs。
    /// 每天 22:10 之后由应用内的 CareWorkloadSnapshotJob 生成（asOf = 当天 22:00，数据已最终），
    /// 之后订单作废、改测试单等都不会改变已存的历史。建表脚本：snowmeet_ai_doc/sql/2026-11-13_care_daily_workload.sql。
    /// </summary>
    [Table("care_daily_workload")]
    [Index(nameof(stat_date), nameof(shop), IsUnique = true, Name = "ux_care_daily_workload_date_shop")]
    public class CareDailyWorkload
    {
        [Key]
        public int id { get; set; }
        [Column(TypeName = "date")]
        public DateTime stat_date { get; set; }
        [Required, MaxLength(100)]
        public string shop { get; set; } = "";
        [Column(TypeName = "datetime")]
        public DateTime as_of { get; set; }
        public int initial_pending { get; set; }
        public int added { get; set; }
        public int completed { get; set; }
        public int completed_cancel { get; set; }
        public int pending { get; set; }
        public int idle_pending { get; set; }
        public int worked { get; set; }
        public int edge_count { get; set; }
        public int wax_count { get; set; }
        public int scrape_count { get; set; }
        public int patch_bottom_count { get; set; }
        public int repair_count { get; set; }
        /// <summary>按完成人拆开的各工序数（CareWorkloadStaff 列表的 JSON）</summary>
        public string? staff_json { get; set; }
        /// <summary>auto 每天定时生成 / catchup 启动时补写</summary>
        [Required, MaxLength(20)]
        public string source { get; set; } = "auto";
        [Column(TypeName = "datetime")]
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

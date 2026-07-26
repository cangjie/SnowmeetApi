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
        // 季卡绑定装备（2026-07-09 起）：type/brand/scale 全非空即「限装备」季卡；serial 暂不参与限制
        public string? equip_type { get; set; }    // 双板/单板
        public string? equip_brand { get; set; }
        public string? equip_scale { get; set; }
        public string? equip_serial { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        public int? source_retail_id { get; set; } = null;   // 该卡由哪笔 retail 销售生成；GrantPunchCard 白送/旧数据为 null
        // 养护次卡专用：一次核销覆盖几个养护项目。1=单项（修刃 或 热蜡）、2=双项（修刃 + 热打蜡）。
        // 发卡/售卡时从 product.care_project_count 复制而来；历史卡为 NULL，核销时回退按卡名含「双项」判断。
        public int? care_project_count { get; set; } = null;
        // 已退款（2026-07-25 起）：顾客在「我的次卡」详情页对一次未核销过的卡自助退款成功后置 true。
        // 置位后该卡在所有核销入口一律不可用（租赁/养护定价、选卡列表、资产聚合都要排除它）。
        public bool is_refund { get; set; } = false;

        [ForeignKey("source_retail_id")]
        public Retail? sourceRetail { get; set; } = null;

        [NotMapped]
        public int? remaining => total == null ? null : total.Value - (punches ?? 0);   // 剩余次数；季卡 NULL
    }
}

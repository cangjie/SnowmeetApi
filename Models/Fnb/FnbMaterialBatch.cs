using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.Fnb
{
    /// <summary>
    /// 餐饮食材批次台账（食材过期提醒）。单店，不设 shop 字段。
    /// 状态（已过期/今日/临期/正常/已处理）不落库，按 expire_date/warn_days/dispose_status 实时派生；
    /// expire_date 是效期唯一真理之源（生产日期/保质期仅录入辅助，原样保存供追溯）。
    /// </summary>
    [Table("fnb_material_batch")]
    public class FnbMaterialBatch
    {
        [Key]
        public int id { get; set; }
        public string name { get; set; }                        // 食材名称
        public string batch_no { get; set; }                    // 批次号（自动发号 B{yyMMdd}-{序号} 或手输，不唯一）
        public DateTime? produce_date { get; set; } = null;     // 生产日期（选填）
        public int? shelf_life_value { get; set; } = null;      // 保质期数值（选填）
        public string? shelf_life_unit { get; set; } = null;    // 保质期单位：天 / 月
        public DateTime expire_date { get; set; }               // 到期日期
        public int warn_days { get; set; } = 3;                 // 预警提前天数（临期：今天 < 到期 <= 今天+warn_days）
        public string? image_ids { get; set; } = null;          // 现场照片 upload_file.id 逗号分隔
        public string? dispose_status { get; set; } = null;     // NULL=在库；用完 / 报废（都归「已处理」）
        public string? dispose_userid { get; set; } = null;     // 处置人（企微 UserId）
        public DateTime? dispose_date { get; set; } = null;
        public string? create_userid { get; set; } = null;      // 录入人（企微 UserId）
        public int valid { get; set; } = 1;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; } = null;
    }
}

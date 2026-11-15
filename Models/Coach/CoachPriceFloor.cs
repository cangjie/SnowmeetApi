using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 机构自己制作会员卡时的底价（管理员设定的下限）：养护每家店 × 单项/双项一行，租赁一行（shop、tier 为空串）。
    /// min_rate = 折扣率不能低于它（0.7 = 不能比 7 折更低）；min_fixed = 一口价不能低于它。两个都为空 = 这一项没有开放，机构不能设优惠。
    /// 机构自助保存价格时逐格校验；管理员自己改价不受它限制。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-15-distributor-backend-design.md
    /// </summary>
    [Table("coach_price_floor")]
    public class CoachPriceFloor
    {
        [Key]
        public int id { get; set; }
        public int coach_id { get; set; }
        /// <summary>养护 | 租赁</summary>
        public string biz_type { get; set; } = "";
        /// <summary>养护：万龙服务中心 | 崇礼旗舰店；租赁为空串</summary>
        public string shop { get; set; } = "";
        /// <summary>养护：单项 | 双项；租赁为空串</summary>
        public string tier { get; set; } = "";
        public double? min_rate { get; set; } = null;
        public double? min_fixed { get; set; } = null;
        public int? updated_by_staff_id { get; set; } = null;
        public DateTime update_date { get; set; } = DateTime.Now;
    }
}

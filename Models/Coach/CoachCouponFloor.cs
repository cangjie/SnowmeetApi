using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 优惠券底价（管理员设）：限制这位分销商（教练 / KOL / 机构）自己生成的单张优惠券不能低到什么程度。
    /// 每家店 × 单项/双项一行，三个值各自可空，空 = 这种方式没有开放，分销商界面不显示：
    /// min_fixed = 一口价不能低于它（元）；min_rate = 折扣率不能低于它（0.7 = 不能比 7 折更低）；max_off = 立减金额不能超过它（元）。
    /// 三个都空 = 这一项整格不开放。
    /// 2026-11-17 起，原来教练详情里的「价格设置」就是它（旧数据由 sql/2026-11-17_coach_coupon_floor.sql 迁入）。
    /// 会籍卡的底价是另一张表 coach_price_floor（只给机构），两者互不影响。
    /// </summary>
    [Table("coach_coupon_floor")]
    public class CoachCouponFloor
    {
        [Key]
        public int id { get; set; }
        public int coach_id { get; set; }
        /// <summary>万龙服务中心 | 崇礼旗舰店</summary>
        public string shop { get; set; } = "";
        /// <summary>单项 | 双项</summary>
        public string tier { get; set; } = "";
        public double? min_fixed { get; set; } = null;
        public double? min_rate { get; set; } = null;
        public double? max_off { get; set; } = null;
        public int? updated_by_staff_id { get; set; } = null;
        public DateTime update_date { get; set; } = DateTime.Now;
    }
}

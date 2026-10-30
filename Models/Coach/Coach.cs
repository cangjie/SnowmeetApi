using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 教练：一个会员 + 一条教练记录，由系统管理员在「教练列表」里添加。
    /// 不复用 staff（店员）、也不复用滑雪学校的 school_staff。
    /// 租赁折扣率全店、全项目通用；养护价格按（门店 × 单项/双项）存在 coach_care_price。
    /// </summary>
    [Table("coach")]
    public class Coach
    {
        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        /// <summary>学员领卡时看到的名字。</summary>
        public string display_name { get; set; } = "";
        /// <summary>租赁折扣率：0.9 = 9 折；NULL = 租赁不优惠。</summary>
        public double? rental_rate { get; set; } = null;
        /// <summary>0 = 已停用：已领的卡继续有效，但不能再发新卡、新卡不能再领。</summary>
        public int valid { get; set; } = 1;
        public int? created_by_staff_id { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; } = null;

        [NotMapped]
        public List<CoachCarePrice>? care_prices { get; set; }
    }

    /// <summary>教练的养护价格：每位教练最多 4 行（2 家店 × 单项/双项）。</summary>
    [Table("coach_care_price")]
    public class CoachCarePrice
    {
        [Key]
        public int id { get; set; }
        public int coach_id { get; set; }
        /// <summary>门店名，取 CarePricingRules.ResolveCareShop 的归属（万龙体验中心归万龙服务中心）。</summary>
        public string shop { get; set; } = "";
        /// <summary>单项 | 双项</summary>
        public string tier { get; set; } = "";
        /// <summary>none | rate | fixed</summary>
        public string mode { get; set; } = "none";
        /// <summary>rate：0.8 = 8 折；fixed：一口价金额。</summary>
        public double value { get; set; } = 0;
    }

    /// <summary>
    /// 学员卡：某某教练的学员卡。一张卡只能被一个学员领取。
    /// 领取时把教练当时的价格写进快照，之后教练改价只影响新领的卡。
    /// </summary>
    [Table("coach_card")]
    public class CoachCard
    {
        [Key]
        public int id { get; set; }
        public int coach_id { get; set; }
        /// <summary>24 位随机串，分享路径里用它，不可枚举。</summary>
        public string token { get; set; } = "";
        /// <summary>issued 待领取 | claimed 已领取 | revoked 已撤销 | merged 会员合并时因重复而作废</summary>
        public string status { get; set; } = StatusIssued;
        /// <summary>待领取卡的失效时间；NULL = 不限。</summary>
        public DateTime? claim_deadline { get; set; } = null;
        public int? member_id { get; set; } = null;
        public DateTime? claimed_at { get; set; } = null;
        public DateTime? start_date { get; set; } = null;
        public DateTime? end_date { get; set; } = null;
        /// <summary>有效期截止年份（本雪季 = 2027）。同一教练同一雪季每个学员只能有一张。</summary>
        public int? season_end_year { get; set; } = null;
        /// <summary>领取时的养护价格快照（JSON：[{shop,tier,mode,value}]）。</summary>
        public string? care_price_snapshot { get; set; } = null;
        /// <summary>领取时的租赁折扣率快照；NULL = 租赁不优惠。</summary>
        public double? rental_rate_snapshot { get; set; } = null;
        public string? remark { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; } = null;
        public DateTime? revoked_at { get; set; } = null;

        public const string StatusIssued = "issued";
        public const string StatusClaimed = "claimed";
        public const string StatusRevoked = "revoked";
        public const string StatusMerged = "merged";
    }

    /// <summary>
    /// 学员卡的一次使用（养护或租赁的某一笔）。UNIQUE(biz_type, biz_id, sub_biz_id) 保证重复核销只记一次。
    /// 养护：biz_id = care.id，sub_biz_id = 0；租赁：biz_id = rental.id，sub_biz_id = rental_detail.id（按天）。
    /// </summary>
    [Table("coach_card_use")]
    public class CoachCardUse
    {
        [Key]
        public int id { get; set; }
        public int card_id { get; set; }
        public int coach_id { get; set; }
        public int member_id { get; set; }
        public int? order_id { get; set; } = null;
        /// <summary>养护 | 租赁</summary>
        public string biz_type { get; set; } = "";
        public int biz_id { get; set; }
        public int sub_biz_id { get; set; } = 0;
        public string mode { get; set; } = "";
        public double mode_value { get; set; } = 0;
        public double base_amount { get; set; } = 0;
        public double discount_amount { get; set; } = 0;
        /// <summary>effective | reversed</summary>
        public string status { get; set; } = StatusEffective;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? reverse_date { get; set; } = null;
        public string? reverse_reason { get; set; } = null;

        public const string StatusEffective = "effective";
        public const string StatusReversed = "reversed";
    }

    /// <summary>管理员批量发给教练的优惠券。grant_key 让批量请求重试时不会重复发放。</summary>
    [Table("coach_coupon_grant")]
    public class CoachCouponGrant
    {
        [Key]
        public int id { get; set; }
        public int coach_id { get; set; }
        public string ticket_code { get; set; } = "";
        public int template_id { get; set; }
        /// <summary>requestId-coachId-序号，唯一。</summary>
        public string grant_key { get; set; } = "";
        public int staff_id { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

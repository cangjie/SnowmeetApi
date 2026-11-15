using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 顾客归属：一位顾客同一时间只归属一位分销商（先到先得）。建立时机：顾客接受了分销商转发的优惠券、
    /// 领了分销商的学员卡、或通过分销商订票二维码订的雪票取票成功。已有归属时不变（用户 2026-11-15 定）。
    /// 设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-15-distributor-backend-design.md
    /// </summary>
    [Table("distributor_customer")]
    public class DistributorCustomer
    {
        public const string SourceQr = "雪票码";
        public const string SourceCoupon = "接券";
        public const string SourceCard = "领卡";

        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        public int introducer_id { get; set; }
        public string source { get; set; } = "";
        /// <summary>来源单据：雪票码 = 订单 id，接券 = 券码，领卡 = 卡 id。</summary>
        public string? source_ref { get; set; } = null;
        /// <summary>0 = 作废（保留历史，可让该顾客重新归属别人）。</summary>
        public int valid { get; set; } = 1;
        public DateTime create_date { get; set; } = DateTime.Now;
    }

    /// <summary>订单归属：这一笔订单是通过哪位分销商的入口产生的。顾客自己的归属不变，但这一单记在这位分销商名下。</summary>
    [Table("distributor_order")]
    public class DistributorOrder
    {
        public const string SourceQr = "雪票码";

        [Key]
        public int id { get; set; }
        public int order_id { get; set; }
        public int introducer_id { get; set; }
        public int member_id { get; set; }
        public string source { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

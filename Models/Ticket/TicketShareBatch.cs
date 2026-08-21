using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 店员分享发券的一次「分享动作」（员工发券三条途径之二）。
    ///
    /// 两种模式共用这张表：
    ///   personal 分享给好友 —— MaxClaims = 1，一张卡片只有第一个点开的人能领
    ///   group    分享到群   —— MaxClaims 为空（不限人数），但每人只能领一张
    ///
    /// 不复用顾客转赠链路的原因：转赠的关注校验场景值是 ticket_gift_{券码}_{分享时间}，
    /// 绑死在单张已存在的券上，「多人各领一张」套不进去。走批次后顾客转赠完全不受影响。
    /// </summary>
    [Table("ticket_share_batch")]
    public class TicketShareBatch
    {
        [Key]
        public int id { get; set; }
        public int template_id { get; set; }
        /// <summary>发起分享的店员。领到的券 staff_id 记的也是他，后台按发券人能查到。</summary>
        public int staff_id { get; set; }
        /// <summary>personal | group | qrcode</summary>
        public string share_type { get; set; } = SharePersonal;
        /// <summary>
        /// 投放场景（与 ticket.channel 同义）。只有 qrcode 批次用：
        /// 一个 (staff_id, template_id, channel) 组合 = 一张固定二维码。
        /// </summary>
        public string? channel { get; set; } = null;
        /// <summary>领取上限。personal 恒为 1；group 为空表示不限人数。</summary>
        public int? max_claims { get; set; } = null;
        public int claim_count { get; set; } = 0;
        /// <summary>0 = 已撤回，链接失效。</summary>
        public int valid { get; set; } = 1;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; } = null;

        public const string SharePersonal = "personal";
        public const string ShareGroup = "group";
        /// <summary>固定二维码：长期有效、任意陌生人可扫，一人一天一张（按模板算）。</summary>
        public const string ShareQrCode = "qrcode";

        /// <summary>
        /// 关注公众号校验用的场景值。与顾客转赠的 ticket_gift_* 分属两套，互不干扰。
        /// 公众号侧把关注/扫码事件原样落进 oa_receive，这边查这个 scene 有没有命中。
        /// </summary>
        [NotMapped]
        public string share_scene
        {
            get
            {
                // 二维码批次单独一个前缀：公众号侧按前缀分派事件，
                // 而且老的 getticket_* 二维码要停用，不能混在一起
                return share_type == ShareQrCode
                    ? "ticketqr_" + id.ToString()
                    : "ticket_share_" + id.ToString();
            }
        }
    }

    /// <summary>
    /// 一次领取。既用于「同一批次每人只能领一张」的去重（库层面有唯一索引兜底），
    /// 也是行为分析的原始数据。
    /// </summary>
    [Table("ticket_share_claim")]
    public class TicketShareClaim
    {
        [Key]
        public int id { get; set; }
        public int batch_id { get; set; }
        public int member_id { get; set; }
        public string ticket_code { get; set; } = "";
        /// <summary>
        /// 领取日期。二维码批次「一人一天一张」的去重维度——
        /// 唯一索引是 (batch_id, member_id, claim_date)。
        /// </summary>
        public DateTime claim_date { get; set; } = DateTime.Now.Date;
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

/// <summary>
/// 分销商「制作优惠券」的一个分享批次（用户 2026-11-18）：每做一次 = 一个批次，规定张数 total_count，
/// 配一个分享二维码 / 小程序卡片（token），转发给个人或放进群里让大家领。
/// 创建批次时**不生成券**，顾客扫码 / 点开后领取时才生成一张券（券码、券记录、发放记录、顾客归属）；
/// 领到 total_count 张就不能再领（原子占位，见 CoachCouponBatchService.ClaimAsync）。
/// 领取限制：每个 openid（会员）每天只能领一次（不分批次）、同一批次只能领一次。
/// 删除 = valid=0（软删除），只有没人领取过的批次能删。
/// 表由 snowmeet_ai_doc/sql/2026-11-18_coach_coupon_batch.sql 建，列名、长度必须和脚本一一对应。
/// </summary>
[Table("coach_coupon_batch")]
public class CoachCouponBatch
{
    [Key]
    public int id { get; set; }
    public int coach_id { get; set; }
    /// <summary>这批券用的隐藏券模板（价格规则在模板上；同一分销商同档位同价格同雪季共用一个模板）</summary>
    public int template_id { get; set; }
    /// <summary>24 位随机串，分享路径 / 二维码里用，不可枚举</summary>
    public string token { get; set; } = "";
    public int total_count { get; set; }
    public int claimed_count { get; set; } = 0;
    /// <summary>创建批次的请求编号：同一份表单重试不会重复建批次（同一分销商内唯一）</summary>
    public string request_id { get; set; } = "";
    /// <summary>1 正常 / 0 已删除</summary>
    public int valid { get; set; } = 1;
    public DateTime create_date { get; set; } = DateTime.Now;
    public DateTime? update_date { get; set; } = null;
    public DateTime? deleted_at { get; set; } = null;
}

/// <summary>批次的一次领取：谁（会员 + openid）在哪天领了哪个批次的哪张券。两条唯一索引兜底并发：(批次, 会员)、(会员, 日期)。</summary>
[Table("coach_coupon_claim")]
public class CoachCouponClaim
{
    [Key]
    public int id { get; set; }
    public int batch_id { get; set; }
    public int coach_id { get; set; }
    public int member_id { get; set; }
    public string open_id { get; set; } = "";
    public string ticket_code { get; set; } = "";
    /// <summary>领取日期（只有日期）：「每天只能领一次」的维度</summary>
    public DateTime claim_date { get; set; } = DateTime.Now.Date;
    public DateTime create_date { get; set; } = DateTime.Now;
}

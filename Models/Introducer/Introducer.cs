using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 介绍人：一个会员 + 一条介绍人记录 + 一条收款账号（order_share_relation）。由系统管理员添加，方式像教练管理。
    /// 类型只有 教练 / KOL / 机构。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-02-introducer-commission-design.md
    /// </summary>
    [Table("introducer")]
    public class Introducer
    {
        [Key]
        public int id { get; set; }
        /// <summary>必须是有效会员；机构填联系人的会员。</summary>
        public int member_id { get; set; }
        /// <summary>教练 / KOL / 机构</summary>
        public string type { get; set; } = "";
        public string name { get; set; } = "";
        /// <summary>真实姓名；支付宝按登录号绑定分账关系时必填。</summary>
        public string? real_name { get; set; } = null;
        /// <summary>收款账号，order_share_relation.id。</summary>
        public int relation_id { get; set; }
        public int? coach_id { get; set; } = null;
        /// <summary>0 = 已停用：订单上不能再选，已经设置的分账不受影响。</summary>
        public int valid { get; set; } = 1;
        public int? created_by_staff_id { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; } = null;
    }
}

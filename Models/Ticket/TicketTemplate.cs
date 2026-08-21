using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("ticket_template")]
    public class TicketTemplate
    {
        [Key]
        public int id { get; set; }

        /// <summary>
        /// 业务线（养护 / 租赁 / null）。控制开单流程里能不能选到本模板发出的券：
        /// null = 不参与开单。2026-08-18 之前这一列全库都是 NULL 且零读取方，
        /// 开单选券过滤的是 ticket.biz_type（券自己的列）+ 一个 template_id==12 的硬编码补丁。
        /// </summary>
        public string? biz_type { get; set; } = null;

        /// <summary>
        /// 可用天数：券**启用日**起算 N 天后失效。与 expire_date 互斥（见 TicketTemplateRules.ValidateTemplate）。
        /// DB 原本是 int NOT NULL（0 当"未设置"用），2026-08-18 随本次改造 ALTER 成可空。
        /// </summary>
        public int? available_days { get; set; } = null;

        public string type { get; set; }
        public string name { get; set; }
        public string memo { get; set; }
        public int hide {get; set;} = 0;

        /// <summary>
        /// 是否允许店员通过小程序卡片分享发券（员工发券三条途径之二）。
        /// 0 = 不可分享（默认）/ 1 = 可分享。
        /// 默认 0：像「非雪季赠双项」这类随单自动发的券，误分享就是白送一张。
        /// 2026-08-20 新增，DDL 见 sql/2026-08-20_ticket_template_sharable.sql。
        /// </summary>
        public int sharable { get; set; } = 0;
        public string miniapp_recept_path { get; set; }

        /// <summary>
        /// 固定截止日（总过期日）。与 available_days 互斥；两者都为空 = 永久有效。
        /// 默认值曾经是 DateTime.MaxValue，会让 new TicketTemplate() 存成 9999-12-31 而不是 NULL。
        /// </summary>
        public DateTime? expire_date { get; set; } = null;

        // 以下四列 DB 都是 NOT NULL 且无默认值。原来模型不映射它们，导致 EF 根本插不了新模板。
        public int valid { get; set; } = 1;
        public int experience { get; set; } = 0;
        public int need_points { get; set; } = 0;
        public double currency_value { get; set; } = 0;

        public List<ProductTicketTemplate> productTicketTemplates {get; set;} = new List<ProductTicketTemplate>();
    }
    [Table("product_ticket_template")]
    public class ProductTicketTemplate
    {
        [Key]
        public int id { get; set; }
        public int product_id { get; set; }
        public int ticket_template_id { get; set; }
        /// <summary>一口价：直接把服务费定成这个数。优先级最高。</summary>
        public double? fixed_price { get; set; } = null;
        /// <summary>折扣比例，"打几折"的小数（0.8 = 8 折）。次优先。</summary>
        public double? discount_rate { get; set; } = null;
        /// <summary>立减金额。优先级最低。</summary>
        public double? discount_amount {get; set;} = null;
        public bool valid {get; set; }
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("ticket_template_id")]
        public TicketTemplate ticketTemplate {get; set;}
        [ForeignKey("product_id")]
        public Product? product {get; set;}
    }

    /*
    [Table("ticket_template_rule")]
    public class TicketTemplateRule
    {
        [Key]
        public int id {get; set;}
        public string? shop {get; set;} = null;
        public int template_id {get; set;}
        public string? biz_name {get; set;} = null;
        public string? sub_biz_name {get; set;} = null;
        public string? discount_type {get; set;} = null;
        public double? fixed_price {get; set;} = null;
        public double? discount_price {get; set;} = null;
        public string? memo {get; set;} = null;
        public bool valid {get; set;}
        public DateTime create_date {get; set;} = DateTime.Now;

    }
    */
}

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
        public string type { get; set; }
        public string name { get; set; }
        public string memo { get; set; }
        // 券面额。DB 里一直有这列、模型此前没映射；当前全库都是 0（体验券），
        // 券列表左侧票根显示的就是它
        public double currency_value { get; set; } = 0;
        public int hide {get; set;} = 0;
        public string miniapp_recept_path { get; set; }
        public DateTime? expire_date { get; set; } = DateTime.MaxValue;
        public List<ProductTicketTemplate> productTicketTemplates {get; set;} = new List<ProductTicketTemplate>();
    }
    [Table("product_ticket_template")]
    public class ProductTicketTemplate
    {
        [Key]
        public int id { get; set; }
        public int product_id { get; set; }
        public int ticket_template_id { get; set; }
        public double? fixed_price { get; set; } = null;
        public double? discount_rate { get; set; } = null;
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

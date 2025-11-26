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

        public string miniapp_recept_path { get; set; }

        public DateTime? expire_date { get; set; } = DateTime.MaxValue;

        public int hide { get; set; }
        [ForeignKey(nameof(TicketTemplateRule.template_id))]
        public List<TicketTemplateRule>? rules;
    }
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
}

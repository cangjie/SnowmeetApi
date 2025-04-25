using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("order")]
    public class Order
    {
        [Key]
        public int id { get; set; }
        public string code {get; set;}
        public string shop {get; set;}
        public string type {get; set;}
        public string sub_type {get; set;} = "";
        public int is_package {get; set;} = 0;
        public string pay_option {get; set;} = "普通";
        public int? member_id {get; set;}
        public string? name {get; set;}
        public string? gender {get; set;}
        public string? cell {get; set;}
        public double total_amount {get; set;}
        public string? ticket_code {get; set;} = null;
        public double ticket_discount {get; set;} = 0;
        public double discount {get; set;} = 0;
        public double final_charge {get; set;} = 0;
        public string memo {get; set;} = "";
        public DateTime biz_date {get; set;} = DateTime.Now;
        public int? staff_id {get; set;}
        public int closed {get; set;} = 0;
        public DateTime? close_date {get; set;} = null;
        public int? waiting_for_pay {get; set;} = null;
        public int supplement {get; set;} = 0;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
        [ForeignKey("staff_id")]
        public Staff staff {get; set;} = null;
        [ForeignKey(nameof(Retail.order_id))]
        public List<Retail> retails {get; set;} = new List<Retail>();
        
    }
}
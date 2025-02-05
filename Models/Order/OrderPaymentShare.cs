using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("order_payment_share")]
    public class PaymentShare
    {
        [Key]
        public int id { get; set; } 
        public int payment_id { get; set; }
        public int order_id { get; set; }
        public int kol_id   {get;set;}
        public double amount { get; set; }
        public string memo { get; set; }
        public int state { get; set; }  
        public string ret_msg { get; set; } 

        public DateTime? submit_date {get; set; }

        public string out_trade_no {get; set;}
        [ForeignKey("kol_id")]
        public Kol kol {get; set;}
    }
}
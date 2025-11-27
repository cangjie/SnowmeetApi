using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("order_share")]
    public class OrderShare
    {
        [Key]
        public int id {get; set;}
        public int order_id {get; set;}
        public int relation_id {get; set;}
        public double amount {get; set;}
        public bool valid {get; set; } = true;
        public DateTime create_date {get; set;}
    }
    [Table("payment_share")]
    public class PaymentShare
    {
        [Key]
        public int id {get; set;}
        public int payment_id {get; set;}
        public int share_id {get; set;}
        public string out_trade_no {get; set;}
        public double amount {get; set;}
        public bool? success {get; set;} = null;
        public bool valid {get; set;} = true;
        public DateTime? submit_time {get; set;} = null;
        public DateTime? response_time {get; set;} = null;
        public string? response_content {get; set;} = null;
        public string? memo {get; set;} = null;
        public bool can_not_share {get; set;} = false;
        public DateTime create_date {get; set;} = DateTime.Now;


    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

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
        public bool dealed {get; set;} = false;
        public DateTime create_date {get; set;}
        [ForeignKey("relation_id")]
        public OrderShareRelation relation {get; set;}
        public List<PaymentShare> paymentShares {get; set;}
        [NotMapped]
        public double successSharedAmount
        {
            get
            {
                try
                {
                    return paymentShares.Where(s => s.valid && (s.success == true)).Sum(s=>s.amount);
                }
                catch
                {
                    return 0;
                }
            }
        }
        public Models.Order order {get; set;} = null;
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
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
        public OrderPayment payment {get; set;}
        [ForeignKey("share_id")]
        public OrderShare orderShare {get; set;}

    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace SnowmeetApi.Models.Order
{
    [Table("order_payment")]
    public class OrderPayment
    {
        public enum PaymentStatus
        {
            待支付,
            支付成功,
            取消
        }

        public string staffRealName = "";

        [Key]
        public int id { get; set; }

        
        public int order_id { get; set; }
        public string pay_method { get; set; }
        public double amount { get; set; }
        public string status { get; set; } = "待支付";
        public string? out_trade_no { get; set; }
        public int? mch_id { get; set; }
        public string open_id { get; set; } = "";
        public string? app_id { get; set; }
        public string? notify { get; set; }
        public string? nonce { get; set; }
        public string? sign { get; set; }
        public string? timestamp { get; set; }
        public string? prepay_id { get; set; }
        public string? ssyn { get; set; }
        public string staff_open_id { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;

        public string? ali_qr_code { get; set; }

        public string? ali_trade_no {get; set;}

        public string? wepay_trans_id {get; set;}
        [ForeignKey(nameof(OrderPaymentRefund.payment_id))]
        public List<Models.Order.OrderPaymentRefund> refunds {get; set;}
        [ForeignKey(nameof(Models.Order.PaymentShare.payment_id))]
        public List<Models.Order.PaymentShare> shares {get;set;}
        public string? deposit_type { get; set; } = null;
        public string? deposit_sub_type {get; set; } = null;


        [NotMapped]
        public string staffName
        {
            get
            {
                return staffRealName.Trim();
            }
        }
 
    }
}


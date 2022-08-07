using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("order_payment")]
    public class OrderPayment
    {
        [Key]
        public int id { get; set; }

        public int order_id { get; set; }
        public string pay_method { get; set; }
        public double amount { get; set; }
        public string status { get; set; }
        public string? out_trade_no { get; set; }
        public int? mch_id { get; set; }
        public string? open_id { get; set; }
        public string? app_id { get; set; }
        public string? notify { get; set; }
        public string? nonce { get; set; }
        public string? sign { get; set; }
        public string? timestamp { get; set; }
        public string? prepay_id { get; set; }
        public string? ssyn { get; set; }
        public string staff_open_id { get; set; } = "";
    }
}


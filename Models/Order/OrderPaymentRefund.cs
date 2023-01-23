using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("order_online_refund")]
    public class OrderPaymentRefund
    {
        public int id { get; set; }
        public int order_id { get; set; }
        public int payment_id { get; set; }
        public double amount { get; set; }
        public int state { get; set; } = 0;
        public string oper { get; set; }
        public string memo { get; set; } = "";
        public string notify_url { get; set; } = "";
        public string refund_id { get; set; } = "";
        public DateTime create_date { get; set; }
    }
}


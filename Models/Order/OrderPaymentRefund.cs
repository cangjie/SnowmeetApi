using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("order_online_refund")]
    public class OrderPaymentRefund
    {
        public int id { get; set; }
        [ForeignKey(nameof(Rent.RentOrder))]
        public int order_id { get; set; }
        public int payment_id { get; set; }
        public double amount { get; set; }
        public int state { get; set; } = 0;
        public string oper { get; set; }
        public string memo { get; set; } = "";
        public string notify_url { get; set; } = "";
        public string refund_id { get; set; } = "";
        public double RefundFee { get; set; } = 0;
        public string TransactionId { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
        public string reason {get; set;} = "";

        public string out_refund_no {get; set;} = "";

        
        public bool refundSuccess
        {
            get
            {
                bool suc = false;
                if (refund_id != null && !refund_id.Trim().Equals("") || state == 1)
                {
                    suc = true;
                }
                return suc;
            }
        }

        public bool isManual
        {
            get
            {
                bool manual = false;
                if (refundSuccess)
                {
                    if (refund_id.Trim().Equals("") && state == 1)
                    {
                        manual = true;   
                    }
                }
                return manual; 
            }
        }
    }
}


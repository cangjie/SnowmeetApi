using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models
{
    [Table("payment_refund")]
    public class OrderPaymentRefund
    {
        public int id { get; set; }
        [ForeignKey(nameof(Rent.RentOrder))]
        public int order_id { get; set; }
        public int payment_id { get; set; }
        public double amount { get; set; }
        public int state { get; set; } = 0;
        public string oper { get; set; } = "";
        public int? oper_member_id {get; set;}
        public string memo { get; set; } = "";
        public string notify_url { get; set; } = "";
        public string refund_id { get; set; } = "";
        public double RefundFee { get; set; } = 0;
        public string TransactionId { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
        public string reason {get; set;} = "";

        public string out_refund_no {get; set;} = "";
        // 雪票购物车订单一单多张票：这笔退款退的是哪一张票（ski_pass.id）；其他业务和旧数据为空
        public int? skipass_id {get; set;} = null;
        public DateTime? update_date {get; set;} = null;
        [ForeignKey("payment_id")]
        public OrderPayment? payment { get; set; } = null;
        [ForeignKey("order_id")]
        public Order? order { get; set; } = null;
        [ForeignKey("oper_member_id")]
        public Member? member { get; set; } = null;
        public int? staff_id { get; set; }
        [ForeignKey("staff_id")]
        public Staff? staff { get; set; }
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


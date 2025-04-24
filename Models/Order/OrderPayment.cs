using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Models
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
        public List<Models.OrderPaymentRefund> refunds {get; set;} = new List<OrderPaymentRefund>();
        [ForeignKey(nameof(Models.PaymentShare.payment_id))]
        public List<Models.PaymentShare> shares {get;set;}
        public string? deposit_type { get; set; } = null;
        public string? deposit_sub_type {get; set; } = null;
        public string shop
        {
            get
            {
                string shop = "";
                if (out_trade_no!=null)
                {
                    if (out_trade_no.StartsWith("WT"))
                    {
                        shop = "万龙体验中心";
                    }
                    else if (out_trade_no.StartsWith("WF"))
                    {
                        shop = "万龙服务中心";
                    }
                    else if (out_trade_no.StartsWith("NS"))
                    {
                        shop = "南山";
                    }
                    else if (out_trade_no.StartsWith("YY"))
                    {
                        shop = "渔阳";
                    }
                    else if (out_trade_no.StartsWith("HB"))
                    {
                        shop = "怀北";
                    }
                }
                return shop;
            }
        }


        [NotMapped]
        public double refundedAmount
        {
            get
            {
                double amount = 0;
                for (int i = 0; i < refunds.Count; i++)
                {
                    if (refunds[i].state == 1 || !refunds[i].refund_id.Trim().Equals(""))
                    {
                        amount += refunds[i].amount;
                    }
                }
                return amount;
            }

        }
        [NotMapped]
        public double unRefundedAmount
        {
            get
            {
                return amount - refundedAmount;
            }
        }

        [NotMapped]
        public OrderOnline order {get; set;}
        [NotMapped]
        public MemberSocialAccount? msa {get; set;} = null;

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


using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.ComponentModel;

using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Ticket;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;


using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models.Rent;

namespace SnowmeetApi.Models
{
    [Table("order_online")]
    public class OrderOnline
    {
        /*
        public class EnterainInfo
        {
            public string cell {get; set;}
            public string name {get; set;}
            public string gender {get; set;}
            public int? memberId {get; set;} = null;
        }
        */
        public string staffRealName = "";

        //public Ticket.Ticket[] ticketArray = new Ticket.Ticket[0];
   
        [Key]
        public int id { get; set; }

        public string type { get; set; }
        public string open_id { get; set; } = "";
        public string cell_number { get; set; } = "";
        public string name { get; set; } = "";
        public string pay_method { get; set; } = "微信支付";
        public double order_price { get; set; } = 0;
        public double order_real_pay_price { get; set; } = 0;
        public int pay_state { get; set; } = 0;
        public DateTime? pay_time { get; set; }
        public string code { get; set; } = "";
        public string syssn { get; set; } = "";
        public string memo { get; set; } = "";
        public string shop { get; set; } = "";
        public string? mchid { get; set; }
        public double ticket_amount { get; set; } = 0;
        public double score_rate { get; set; } = 0;
        public double generate_score { get; set; } = 0;
        public string ticket_code { get; set; } = "";
        public string out_trade_no { get; set; } = "";
        public string pay_memo { get; set; } = "全额支付";
        public double other_discount { get; set; } = 0;
        public double final_price { get; set; } = 0;
        public string? staff_open_id { get; set; }

       
        public int have_score { get; set; } = 0;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? crt { get; set; } = DateTime.Now;

        public int referee_member_id { get; set; } = 0;
        public DateTime biz_date {get; set;} = DateTime.Now;
        public string? payer { get; set; } = null;
        /*
        public bool isEnterain
        {
            get
            {
                bool enterain = false;
                for(int i = 0; mi7Orders != null && i < mi7Orders.Count; i++)
                {
                    if (mi7Orders[i].order_type.Trim().Equals("招待"))
                    {
                        enterain = true;
                        break;
                    }
                }
                return enterain;
            }
        }
        */
        /*
        public EnterainInfo enterainInfo
        {
            get
            {
                if (!isEnterain)
                {
                    return null;
                }
                else
                {
                    string name = "";
                    string cell = "";
                    string gender = "";
                    int? memberId = null;
                    for(int i = 0; i < mi7Orders.Count; i++)
                    {
                        Mi7Order mi7Order = mi7Orders[i];
                        if (mi7Order.order_type.Trim().Equals("招待"))
                        {
                            name = mi7Order.enterain_real_name == null? "" : mi7Order.enterain_real_name.Trim();
                            cell = mi7Order.enterain_cell == null? "" : mi7Order.enterain_cell.Trim();
                            gender = mi7Order.enterain_gender == null ? "" : mi7Order.enterain_gender.Trim();
                            memberId = mi7Order.enterain_member_id == null? 0 :  mi7Order.enterain_member_id;
                            break;
                        }
                    }
                    return new EnterainInfo()
                    {
                        name = name,
                        cell = cell,
                        gender = gender,
                        memberId = memberId
                    };
                }
            }
        }
        */
        [NotMapped]
        public OrderPayment[]? _payments; 

        [NotMapped]
        public OrderPayment[]? payments 
        { 
            get
            {
                if (paymentList == null || paymentList.Count == 0)
                {
                    return new OrderPayment[0];
                }
                else
                {
                    return paymentList.ToArray();
                }
            }
            set
            {
                _payments = value;
            } 
        }
        /// <summary>
        /// temp propertyies before merge
        /// </summary>order_id
        /// 
        [ForeignKey(nameof(OrderPayment.order_id))]
        public List<Models.OrderPayment> paymentList {get; set;}
        [NotMapped]
        public List<Models.OrderPaymentRefund> refundList
        {
            get
            {
                List<OrderPaymentRefund> rL = new List<OrderPaymentRefund>();
                
                for(int i = 0; paymentList != null && i < paymentList.Count; i++)
                {
                    OrderPayment payment = paymentList[i];
                    for(int j = 0; payment != null && payment.refunds != null && j <  payment.refunds.Count; j++)
                    {
                        rL.Add(payment.refunds[j]);
                    }
                }
                
                return rL;
            }
        }
        
        [NotMapped]
        public List<OrderPaymentRefund> refunds { get; set; }
        [NotMapped]
        public MiniAppUser? user { get; set; }
        
        public List<Mi7Order>? mi7Orders { get; set; }
        [NotMapped]
        public string status
        {
            get
            {
                string str = "待支付";
               
                if (paidAmount == 0)
                {
                    switch (pay_memo.Trim())
                    {
                        case "无需支付":
                            str = "支付完成";
                            break;
                        case "暂缓支付":
                            str = "待支付";
                            break;
                        default:
                            if (create_date.AddDays(1) < DateTime.Now)
                            {
                                str = "订单关闭";
                            }
                            break;
                    }
                }
                else if (paidAmount >= final_price)
                {
                    str = "支付完成";
                }
                else if (paidAmount > 0 && paidAmount < final_price)
                {
                    str = "部分支付";
                }
                if (pay_state == -1)
                {
                    str = "订单作废";
                }
                return str.Trim();
            }
        }
        [NotMapped]
        public double paidAmount
        {
            get
            {
                double amount = 0;
                if (payments != null)
                {
                    for (int i = 0; paymentList != null && i < paymentList.Count; i++)
                    {
                        if (payments[i].status.Trim().Equals("支付成功"))
                        {
                            amount = amount + payments[i].amount;
                        }
                    }
                }
                return amount;
            }
        }
        [NotMapped]
        public double refundAmount
        {
            get
            {
                double refund = 0;
                for(int i = 0; paymentList != null && i < paymentList.Count; i++)
                {
                    if (paymentList[i].status.Trim().Equals("支付成功"))
                    {
                        for(int j = 0;paymentList[i].refunds != null && j < paymentList[i].refunds.Count; j++)
                        {
                            if (paymentList[i].refunds[j].state == 1 || !paymentList[i].refunds[j].refund_id.Trim().Equals(""))
                            {
                                refund += paymentList[i].refunds[j].amount;
                            }
                        }
                    }
                }
                return refund;
            }
        }
        [NotMapped]
        public string staffName
        {
            get
            {
                return staffRealName.Trim();
            }
        }
        [NotMapped]
        public Ticket.Ticket[] tickets { get; set; }
        [NotMapped]
        public OrderOnlineDetail[]? details { get; set; }
        //[NotMapped]
        //public Member? member {get; set;}
        //[NotMapped]
        //public MemberSocialAccount? msa {get; set;}
        public List<RentOrder> rentOrderList {get; set;}
        [ForeignKey(nameof(MaintainLive.order_id))]
        public List<MaintainLive> maintainList {get; set;}
        
        public List<Models.SkiPass.SkiPass> skiPasses {get; set;} = new List<Models.SkiPass.SkiPass>();
       
    }
}

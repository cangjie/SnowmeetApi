using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.ComponentModel;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Ticket;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;


using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models
{
    [Table("order_online")]
    public class OrderOnline
    {
        public string staffRealName = "";

        //public Ticket.Ticket[] ticketArray = new Ticket.Ticket[0];
   
        [Key]
        public int id { get; set; }

        public string type { get; set; }
        public string open_id { get; set; }
        public string cell_number { get; set; }
        public string name { get; set; }
        public string pay_method { get; set; }
        public double order_price { get; set; }
        public double order_real_pay_price { get; set; }
        public int pay_state { get; set; }
        public DateTime? pay_time { get; set; }
        public string code { get; set; } = "";
        public string syssn { get; set; }
        public string memo { get; set; } = "";
        public string shop { get; set; }
        public string? mchid { get; set; }
        public double ticket_amount { get; set; }
        public double score_rate { get; set; }
        public double generate_score { get; set; }
        public string ticket_code { get; set; } = "";
        public string out_trade_no { get; set; } = "";
        public string pay_memo { get; set; } = "全额支付";
        public double other_discount { get; set; }
        public double final_price { get; set; }
        public string? staff_open_id { get; set; }
        public int have_score { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;

        
        [NotMapped]
        public OrderPayment[]? payments { get; set; }
        [NotMapped]
        public OrderPaymentRefund[]? refunds { get; set; }
        [NotMapped]
        public MiniAppUser? user { get; set; }
        [NotMapped]
        public Mi7Order[]? mi7Orders { get; set; }
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
                    for (int i = 0; i < payments.Length; i++)
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
        /*
        [NotMapped]
        public Ticket.Ticket[] tickets
        {
            get
            {
                return ticketArray;
            }
        }
        */
        //public SnowmeetApi.Data.ApplicationDBContext db;

        
        
        /*
        
        [Key]
        public int id { get; set; }
        public string type { get; set; }
        public int pay_state { get; set; }
        public DateTime? pay_time { get; set; }
        public string out_trade_no { get; set; }
        public string pay_method { get; set; }
        public string open_id { get; set; }
        public double order_real_pay_price { get; set; }
        public double order_price { get; set; }
        public string cell_number { get; set; }
        public string shop { get; set; }
        public string name { get; set; }
        public string code { get; set; }
        public string ticket_code { get; set; }

        [DefaultValue("")]
        public string memo { get; set; } = "";
        */
        //public List<OrderOnlineDetail> details { get; set; }
    }
}

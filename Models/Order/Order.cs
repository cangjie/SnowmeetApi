using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace SnowmeetApi.Models
{
    [Table("order")]
    public class Order
    {
        [Key]
        public int id { get; set; }
        public string code { get; set; }
        public string shop { get; set; }
        public string type { get; set; }
        public string sub_type { get; set; } = "";
        public int is_package { get; set; } = 0;
        public string pay_option { get; set; } = "普通";
        public int? member_id { get; set; }
        public string? name { get; set; }
        public string? gender { get; set; }
        public string? cell { get; set; }
        public double total_amount { get; set; }
        public string? ticket_code { get; set; } = null;
        public double ticket_discount { get; set; } = 0;
        public double discount { get; set; } = 0;
        public string memo { get; set; } = "";
        public DateTime biz_date { get; set; } = DateTime.Now;
        public int? staff_id { get; set; }
        public int closed { get; set; } = 0;
        public int valid { get; set; } = 1;
        public DateTime? close_date { get; set; } = null;
        public int? waiting_for_pay { get; set; } = null;
        public int supplement { get; set; } = 0;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("staff_id")]
        public Staff staff { get; set; } = null;
        [ForeignKey("member_id")]
        public Member member { get; set; } = null;
        public List<Retail> retails { get; set; } = new List<Retail>();
        public List<OrderPayment>? payments { get; set; }
        public List<OrderPaymentRefund>? refunds { get; set; }
        public List<PaymentShare>? shares { get; set; }
        public double totalCharge
        {
            get
            {
                return total_amount - ticket_discount - discount;
            }
        }
        [NotMapped]
        public List<OrderPayment>? availablePayments
        {
            get
            {
                if (payments == null)
                {
                    return null;
                }
                else
                {
                    return payments.Where(p => p.status.Equals("支付成功")).ToList();
                }
            }
        }
        [NotMapped]
        public List<OrderPaymentRefund>? availableRefunds
        {
            get
            {
                if (availablePayments == null)
                {
                    return null;
                }
                else
                {
                    List<OrderPaymentRefund> availableRefunds = new List<OrderPaymentRefund>();
                    foreach (OrderPayment payment in availablePayments)
                    {
                        foreach (OrderPaymentRefund refund in payment.refunds)
                        {
                            if (refund.state == 1 || !refund.refund_id.Trim().Equals(""))
                            {
                                availableRefunds.Add(refund);
                            }

                        }
                    }
                    return availableRefunds;
                }
            }
        }
        [NotMapped]
        public double paidAmount
        {
            get
            {
                double paid = 0;
                if (availablePayments == null)
                {
                    return 0;
                }
                foreach (OrderPayment payment in availablePayments)
                {
                    if (payment.status.Equals("支付成功"))
                    {
                        paid += payment.amount;
                    }
                }
                return paid;
            }
        }
        [NotMapped]
        public double refundAmount
        {
            get
            {
                double refundAmount = 0;
                foreach (OrderPaymentRefund refund in availableRefunds)
                {
                    refundAmount += refund.amount;
                }
                return refundAmount;
            }
        }
        [NotMapped]
        public string paymentStatus
        {
            get
            {
                string s = "";
                if (valid == 0)
                {
                    if (closed == 1)
                    {
                        return "订单作废";
                    }
                    else
                    {
                        if (paidAmount < totalCharge)
                        {
                            if (paidAmount == 0)
                            {
                                s = "待支付";
                            }
                            else
                            {
                                s = "部分支付";
                            }
                        }
                        else
                        {
                            s = "支付完成";
                        }
                        return s;
                    }

                }
                else
                {
                    if (pay_option.Trim().Equals("招待"))
                    {
                        return "无需支付";
                    }
                    else if (pay_option.Trim().Equals("挂账"))
                    {
                        return "挂账";
                    }
                    else
                    {
                        if (paidAmount < totalCharge)
                        {
                            if (paidAmount == 0)
                            {
                                s = "待支付";
                            }
                            else
                            {
                                s = "部分支付";
                            }
                        }
                        else
                        {
                            s = "支付完成";
                        }
                        return s;
                    }
                }
                
            }
        }

    }
}
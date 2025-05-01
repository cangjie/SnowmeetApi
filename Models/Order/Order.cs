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
        public static void RendOrder(SnowmeetApi.Models.Order order)
        {
            string txtColor = "";
            string backColor = "";
            if (order.paidAmount < order.totalCharge && order.closed == 0)
            {
                txtColor = "red";
            }
            else if (order.retails == null || (order.retails != null
                && order.retails.Any(r => (r.mi7_code == null || !r.mi7_code.StartsWith("XSD") || r.mi7_code.Length != 15 || (!r.mi7_code.ToUpper().EndsWith("A") && !r.mi7_code.ToUpper().EndsWith("I"))))))
            {
                txtColor = "orange";
            }
            if (order.paidAmount == 0 && order.pay_option.Trim().Equals("招待"))
            {
                backColor = "yellow";
            }
            if (order.valid == 0)
            {
                txtColor = "grey";
            }
            order.textColor = txtColor;
            order.backgroundColor = backColor;
        }
        public static void RendOrderList(List<SnowmeetApi.Models.Order> orderList)
        {
            for (int i = 0; i < orderList.Count; i++)
            {
                RendOrder(orderList[i]);
            }
        }
        public static List<CoreDataModLog> GetUpdateDifferenceLog(Order oriOrder, Order order, int? memberId, int? staffId, string scene)
        {
            if (oriOrder.id != order.id)
            {
                return null;
            }
            List<CoreDataModLog> logs = new List<CoreDataModLog>();
            TimeSpan ts = DateTime.Now - DateTime.Parse("1970-1-1");
            if (oriOrder.code != order.code)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "code", order.id.ToString(), oriOrder.code, order.code, memberId, staffId, scene, ts.Ticks));
                oriOrder.code = order.code;
            }
            if (oriOrder.shop != order.shop)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "shop", order.id.ToString(), oriOrder.shop, order.shop, memberId, staffId, scene, ts.Ticks));
                oriOrder.shop = order.shop;
            }
            if (oriOrder.type != order.type)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "type", order.id.ToString(), oriOrder.type, order.type, memberId, staffId, scene, ts.Ticks));
                oriOrder.type = order.type;
            }
            if (oriOrder.sub_type != order.sub_type)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "sub_type", order.id.ToString(), oriOrder.sub_type, order.sub_type, memberId, staffId, scene, ts.Ticks));
                oriOrder.sub_type = order.sub_type;
            }
            if (oriOrder.is_package != order.is_package)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "is_package", order.id.ToString(), oriOrder.is_package, order.is_package, memberId, staffId, scene, ts.Ticks));
                oriOrder.is_package = order.is_package;
            }
            if (oriOrder.pay_option != order.pay_option)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "pay_option", order.id.ToString(), oriOrder.pay_option, order.pay_option, memberId, staffId, scene, ts.Ticks));
                oriOrder.pay_option = order.pay_option;
            }
            if (oriOrder.member_id != order.member_id)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "member_id", order.id.ToString(), oriOrder.member_id, order.member_id, memberId, staffId, scene, ts.Ticks));
                oriOrder.member_id = order.member_id;
            }
            if (oriOrder.name != order.name)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "name", order.id.ToString(), oriOrder.name, order.name, memberId, staffId, scene, ts.Ticks));
                oriOrder.name = order.name;
            }
            if (oriOrder.gender != order.gender)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "gender", order.id.ToString(), oriOrder.gender, order.gender, memberId, staffId, scene, ts.Ticks));
                oriOrder.gender = order.gender;
            }
            if (oriOrder.cell != order.cell)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "cell", order.id.ToString(), oriOrder.cell, order.cell, memberId, staffId, scene, ts.Ticks));
                oriOrder.cell = order.cell;
            }
            if (oriOrder.total_amount != order.total_amount)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "total_amount", order.id.ToString(), oriOrder.total_amount, order.total_amount, memberId, staffId, scene, ts.Ticks));
                oriOrder.total_amount = order.total_amount;
            }
            if (oriOrder.ticket_code != order.ticket_code)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "ticket_code", order.id.ToString(), oriOrder.ticket_code, order.ticket_code, memberId, staffId, scene, ts.Ticks));
                oriOrder.ticket_code = order.ticket_code;
            }
            if (oriOrder.ticket_discount != order.ticket_discount)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "ticket_discount", order.id.ToString(), oriOrder.ticket_discount, order.ticket_discount, memberId, staffId, scene, ts.Ticks));
                oriOrder.ticket_discount = order.ticket_discount;
            }
            if (oriOrder.discount != order.discount)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "discount", order.id.ToString(), oriOrder.discount, order.discount, memberId, staffId, scene, ts.Ticks));
                oriOrder.discount = order.discount;
            }
            if (oriOrder.memo != order.memo)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "memo", order.id.ToString(), oriOrder.memo, order.memo, memberId, staffId, scene, ts.Ticks));
                oriOrder.memo = order.memo;
            }
            if (oriOrder.biz_date != order.biz_date)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "biz_date", order.id.ToString(), oriOrder.biz_date, order.biz_date, memberId, staffId, scene, ts.Ticks));
                oriOrder.biz_date = order.biz_date;
            }
            if (oriOrder.staff_id != order.staff_id)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "staff_id", order.id.ToString(), oriOrder.staff_id, order.biz_date, memberId, staffId, scene, ts.Ticks));
                oriOrder.staff_id = order.staff_id;
            }
            if (oriOrder.closed != order.closed)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "closed", order.id.ToString(), oriOrder.closed, order.closed, memberId, staffId, scene, ts.Ticks));
                oriOrder.closed = order.closed;
            }
            if (oriOrder.valid != order.valid)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "valid", order.id.ToString(), oriOrder.valid, order.valid, memberId, staffId, scene, ts.Ticks));
                oriOrder.valid = order.valid;
            }
            if (oriOrder.close_date != order.close_date)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "close_date", order.id.ToString(), oriOrder.close_date, order.close_date, memberId, staffId, scene, ts.Ticks));
                oriOrder.close_date = order.close_date;
            }
            if (oriOrder.waiting_for_pay != order.waiting_for_pay)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "waiting_for_pay", order.id.ToString(), oriOrder.waiting_for_pay, order.waiting_for_pay, memberId, staffId, scene, ts.Ticks));
                oriOrder.waiting_for_pay = order.waiting_for_pay;
            }
            if (oriOrder.supplement != order.supplement)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "supplement", order.id.ToString(), oriOrder.supplement, order.supplement, memberId, staffId, scene, ts.Ticks));
                oriOrder.supplement = order.supplement;
            }
            return logs;
        }
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
        [NotMapped]
        public string textColor { get; set; } = "";
        [NotMapped]
        public string backgroundColor { get; set; } = "";
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
        public bool canDelete
        {
            get
            {
                bool can = true;
                for(int i = 0; availablePayments != null && i < availablePayments.Count; i++)
                {
                    if (availablePayments[i].pay_method.Trim().Equals("微信支付"))
                    {
                        can = false;
                    }
                }
                return can;
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
                if (availableRefunds == null)
                {
                    return 0;
                }
                foreach (OrderPaymentRefund refund in availableRefunds)
                {
                    refundAmount += refund.amount;
                }
                return refundAmount;
            }
        }
        [NotMapped]
        public double surplusAmount
        {
            get
            {
                return paidAmount - refundAmount;
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
                        return "暂缓支付";
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
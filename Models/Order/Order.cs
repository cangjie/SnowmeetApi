using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NPOI.SS.Formula.PTG;

namespace SnowmeetApi.Models
{
    [Table("order")]
    public class Order
    {
        public enum OrderTag
        {
            无效订单,
            已关闭,
            部分支付,
            支付完成,
            挂账,
            已平账,
            部分平账,
            招待,
            减免,
            支付中
        }
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
                logs.Add(Util.CreateCoreDataModLog("order", "code", order.id, oriOrder.code, order.code, memberId, staffId, scene, ts.Ticks));
                oriOrder.code = order.code;
            }
            if (oriOrder.shop != order.shop)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "shop", order.id, oriOrder.shop, order.shop, memberId, staffId, scene, ts.Ticks));
                oriOrder.shop = order.shop;
            }
            if (oriOrder.type != order.type)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "type", order.id, oriOrder.type, order.type, memberId, staffId, scene, ts.Ticks));
                oriOrder.type = order.type;
            }
            if (oriOrder.sub_type != order.sub_type)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "sub_type", order.id, oriOrder.sub_type, order.sub_type, memberId, staffId, scene, ts.Ticks));
                oriOrder.sub_type = order.sub_type;
            }
            if (oriOrder.is_package != order.is_package)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "is_package", order.id, oriOrder.is_package, order.is_package, memberId, staffId, scene, ts.Ticks));
                oriOrder.is_package = order.is_package;
            }
            if (oriOrder.pay_option != order.pay_option)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "pay_option", order.id, oriOrder.pay_option, order.pay_option, memberId, staffId, scene, ts.Ticks));
                oriOrder.pay_option = order.pay_option;
            }
            if (oriOrder.member_id != order.member_id)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "member_id", order.id, oriOrder.member_id, order.member_id, memberId, staffId, scene, ts.Ticks));
                oriOrder.member_id = order.member_id;
            }
            if (oriOrder.name != order.name)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "name", order.id, oriOrder.name, order.name, memberId, staffId, scene, ts.Ticks));
                oriOrder.name = order.name;
            }
            if (oriOrder.gender != order.gender)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "gender", order.id, oriOrder.gender, order.gender, memberId, staffId, scene, ts.Ticks));
                oriOrder.gender = order.gender;
            }
            if (oriOrder.cell != order.cell)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "cell", order.id, oriOrder.cell, order.cell, memberId, staffId, scene, ts.Ticks));
                oriOrder.cell = order.cell;
            }
            if (oriOrder.total_amount != order.total_amount)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "total_amount", order.id, oriOrder.total_amount, order.total_amount, memberId, staffId, scene, ts.Ticks));
                oriOrder.total_amount = order.total_amount;
            }
            /*
            if (oriOrder.ticket_code != order.ticket_code)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "ticket_code", order.id, oriOrder.ticket_code, order.ticket_code, memberId, staffId, scene, ts.Ticks));
                oriOrder.ticket_code = order.ticket_code;
            }
            if (oriOrder.ticket_discount != order.ticket_discount)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "ticket_discount", order.id, oriOrder.ticket_discount, order.ticket_discount, memberId, staffId, scene, ts.Ticks));
                oriOrder.ticket_discount = order.ticket_discount;
            }
            if (oriOrder.discount != order.discount)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "discount", order.id, oriOrder.discount, order.discount, memberId, staffId, scene, ts.Ticks));
                oriOrder.discount = order.discount;
            }
            */
            if (oriOrder.memo != order.memo)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "memo", order.id, oriOrder.memo, order.memo, memberId, staffId, scene, ts.Ticks));
                oriOrder.memo = order.memo;
            }
            if (oriOrder.biz_date != order.biz_date)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "biz_date", order.id, oriOrder.biz_date, order.biz_date, memberId, staffId, scene, ts.Ticks));
                oriOrder.biz_date = order.biz_date;
            }
            if (oriOrder.staff_id != order.staff_id)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "staff_id", order.id, oriOrder.staff_id, order.biz_date, memberId, staffId, scene, ts.Ticks));
                oriOrder.staff_id = order.staff_id;
            }
            if (oriOrder.closed != order.closed)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "closed", order.id, oriOrder.closed, order.closed, memberId, staffId, scene, ts.Ticks));
                oriOrder.closed = order.closed;
            }
            if (oriOrder.valid != order.valid)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "valid", order.id, oriOrder.valid, order.valid, memberId, staffId, scene, ts.Ticks));
                oriOrder.valid = order.valid;
            }
            if (oriOrder.close_date != order.close_date)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "close_date", order.id, oriOrder.close_date, order.close_date, memberId, staffId, scene, ts.Ticks));
                oriOrder.close_date = order.close_date;
            }
            if (oriOrder.waiting_for_pay != order.waiting_for_pay)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "waiting_for_pay", order.id, oriOrder.waiting_for_pay, order.waiting_for_pay, memberId, staffId, scene, ts.Ticks));
                oriOrder.waiting_for_pay = order.waiting_for_pay;
            }
            if (oriOrder.supplement != order.supplement)
            {
                logs.Add(Util.CreateCoreDataModLog("order", "supplement", order.id, oriOrder.supplement, order.supplement, memberId, staffId, scene, ts.Ticks));
                oriOrder.supplement = order.supplement;
            }
            return logs;
        }
        [Key]
        public int id { get; set; }
        public string code { get; set; }
        public string shop { get; set; }
        public string type { get; set; }
        public string? contact_num { get; set; } = null;
        public string? contact_name { get; set; } = null;
        public string? contact_gender { get; set; } = null;
        public string sub_type { get; set; } = "";
        public int is_package { get; set; } = 0;
        public string pay_option { get; set; } = "普通";
        public int? member_id { get; set; }
        public string? name { get; set; }
        public string? gender { get; set; }
        public string? cell { get; set; }
        public double total_amount { get; set; }
        public string memo { get; set; } = "";
        public DateTime biz_date { get; set; } = DateTime.Now;
        public int? staff_id { get; set; }
        public int closed { get; set; } = 0;
        public int valid { get; set; } = 1;
        public DateTime? close_date { get; set; } = null;
        public int waiting_for_pay { get; set; } = 1;
        public int supplement { get; set; } = 0;
        public int single_payment { get; set; } = 1;
        public int dealed { get; set; } = 0;
        public int is_test { get; set; } = 0;
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
        public List<Care> cares { get; set; } = new List<Care>();
        public List<Rental> rentals { get; set; } = new List<Rental>();
        public List<FdOrder> fdOrders { get; set; } = new List<FdOrder>();
        public List<OrderPayment>? payments { get; set; }
        public List<OrderPaymentRefund>? refunds { get; set; }
        public List<PaymentShare>? shares { get; set; }
        public List<Discount> discounts { get; set; } = new List<Discount>();
        public List<Guaranty> guarantys { get; set; } = new List<Guaranty>();
        [NotMapped]
        public List<Guaranty> paidGuarantys
        {
            get
            {

                List<Guaranty> paidGuarantys = new List<Guaranty>();
                for (int i = 0; i < guarantys.Count; i++)
                {
                    Guaranty g = guarantys[i];
                    if (g.payStatus.Trim().Equals("支付完成"))
                    {
                        paidGuarantys.Add(g);
                    }
                }
                return paidGuarantys;
            }
        }
        [NotMapped]
        public double discountAmount
        {
            get
            {
                if (discounts == null)
                {
                    return 0;
                }
                double amount = 0;
                foreach (Discount discount in discounts)
                {
                    if (discount.valid == 1)
                    {
                        amount += discount.amount;
                    }
                }
                return amount;
            }
        }
        [NotMapped]
        public double totalCharge
        {
            get
            {
                return total_amount - discountAmount;
            }
        }
        [NotMapped]
        public bool canDelete
        {
            get
            {
                bool can = true;
                for (int i = 0; availablePayments != null && i < availablePayments.Count; i++)
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
        public List<OrderPayment> availablePayments
        {
            get
            {
                if (payments == null)
                {
                    return new List<OrderPayment>();
                }
                else
                {
                    return payments.Where(p => p.status.Equals("支付成功") && p.valid == 1).ToList();
                }
            }
        }
        [NotMapped]
        public List<OrderPayment> debts
        {
            get
            {
                if (payments == null)
                {
                    return new List<OrderPayment>();
                }
                else
                {
                    return payments.Where(p => p.is_debt == 1 && p.valid == 1).ToList();
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
        [NotMapped]
        public string? rentalStatus
        {
            get
            {
                //已付押金 未支付 已关闭 全部归还 已退款  已完成 免押金
                string? s = null;
                if (rentals == null && rentals.Count <= 0)
                {
                    return null;
                }
                bool allSettled = true;
                for (int i = 0; i < rentals.Count; i++)
                {
                    if (rentals[i].settled == 0)
                    {
                        allSettled = false;
                        break;
                    }
                }
                if (allSettled)
                {
                    return "已完成";
                }
                if (closed == 1)
                {
                    return "已关闭";
                }

                if (pay_option.Trim().Equals("招待"))
                {
                    s = "免押金";
                }
                bool unPaid = true;
                foreach (Guaranty g in guarantys)
                {
                    if (!g.payStatus.Equals("未支付"))
                    {
                        unPaid = false;
                        break;
                    }
                }
                if (unPaid)
                {
                    s = "未支付";
                }
                else
                {
                    s = "已付押金";
                }
                bool allReturned = true;
                foreach (Rental rental in rentals)
                {
                    foreach (RentItem item in rental.rentItems)
                    {
                        if (item.return_time == null)
                        {
                            allReturned = false;
                            break;
                        }
                    }
                    if (!allReturned)
                    {
                        break;
                    }
                }
                if (allReturned)
                {
                    s = "全部归还";
                }


                return s;
            }
        }
        [NotMapped]
        public DateTime? rentalLastRefundDate
        {
            get
            {
                if (rentals == null || rentals.Count == 0)
                {
                    return null;
                }

                DateTime rDate = DateTime.MinValue;
                foreach (OrderPayment payment in payments)
                {
                    foreach (OrderPaymentRefund refund in payment.refunds)
                    {
                        if (refund.state == 1 || !refund.refund_id.Trim().Equals(""))
                        {
                            rDate = rDate > refund.create_date ? rDate : refund.create_date;
                        }
                    }
                }

                if (rDate == DateTime.MinValue)
                {
                    return null;
                }
                else
                {
                    return rDate;
                }
            }
        }
        [NotMapped]
        public double guarantyAmount
        {
            get
            {
                double amount = 0;
                foreach (Guaranty g in paidGuarantys)
                {
                    amount += (double)g.amount;
                }
                return amount;
            }
        }
        [NotMapped]
        public string subject
        {
            get
            {
                return type.Trim() + "订单";
            }
        }
        [NotMapped]
        public string description
        {
            get
            {
                string ret = "";
                switch (type)
                {
                    case "餐饮":
                        foreach (FdOrder fd in fdOrders)
                        {
                            ret += (fd.product_name + " x " + fd.count.ToString() + ";");
                        }
                        break;
                    default:
                        break;
                }
                return ret;
            }
        }
        [NotMapped]
        public List<string> tags
        {
            get
            {
                List<string> tags = new List<string>();
                if (valid == 0)
                {
                    tags.Add(OrderTag.无效订单.ToString());
                    return tags;
                }
                else
                {
                    if (closed == 1)
                    {
                        tags.Add(OrderTag.已关闭.ToString());
                    }
                    if (discounts.Where(d => d.valid == 1).ToList().Count > 0)
                    {
                        tags.Add(OrderTag.减免.ToString());
                    }
                    //List<OrderPayment> debtList = availablePayments.Where(p => p.is_debt == 1).ToList();
                    if (debts.Count > 0)
                    {
                        tags.Add(OrderTag.挂账.ToString());
                        bool allPaid = true;
                        bool havePaid = false;
                        List<OrderPayment> pL = availablePayments
                            .Where(p => p.status.Equals(OrderPayment.PaymentStatus.支付成功.ToString())).ToList();
                        for (int i = 0; i < debts.Count; i++)
                        {
                            if (pL.Where(p => p.reference_debt_id == debts[i].id).ToList().Count > 0)
                            {
                                havePaid = true;
                            }
                            else
                            {
                                allPaid = false;
                            }
                        }
                        if (havePaid)
                        {
                            if (allPaid)
                            {
                                tags.Add(OrderTag.已平账.ToString());
                            }
                            else
                            {
                                tags.Add(OrderTag.部分平账.ToString());
                            }
                        }
                    }
                    else
                    {
                        if (waiting_for_pay == 1)
                        {
                            tags.Add(OrderTag.支付中.ToString());
                        }
                        if (waiting_for_pay == 0 && paidAmount == totalCharge)
                        {
                            tags.Add(OrderTag.支付完成.ToString());
                        }

                    }
                }
                return tags;
            }
        }
    }
}
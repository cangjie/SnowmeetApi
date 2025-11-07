using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using NPOI.SS.Formula.PTG;

namespace SnowmeetApi.Models
{
    [Table("order")]
    public class Order
    {
        public enum OrderStatus { 待生成, 待支付, 部分支付, 支付成功, 挂账, 全额退款, 部分退款, 退款失败, 订单关闭, 已下单, 已完成 }
        public enum PayFlowStatus { 待生成, 已生成, 待支付, 支付中, 已支付, 已关闭, 部分退款, 全额退款 }
        public enum PayType { 整单支付, 分付, 无需支付, 未支付, 招待 }
        public enum RentStatus { 未开始, 租赁中, 部分归还, 全部归还, 部分退押金, 全额退押金, 了结关闭 };
        public class RentPropertySet
        {
            public string? rentStatus { get; set; } = null;
            public DateTime? startDate { get; set; } = null;
            public DateTime? endDate { get; set; } = null;
            public int totalPaidGuarantyCount { get; set; } = 0;
            public int relieveGuarantyCount { get; set; } = 0;
            public double? totalGuarantyAmount { get; set; } = null;
            public double? currentRentalAmount { get; set; } = null;
            public int totalRentalsCount { get; set; } = 0;
            public int packageCount { get; set; } = 0;
            public int categoryCount { get; set; } = 0;
            public double? totalChargeSummaryAmount { get; set; } = null;
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
        [Key]
        public int id { get; set; }
        public string? code { get; set; } = null;
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
        public int queryed { get; set; } = 0;
        public int valid { get; set; } = 1;
        public DateTime? close_date { get; set; } = null;
        public int supplement { get; set; } = 0;
        //public int single_payment { get; set; } = 1;
        public int dealed { get; set; } = 0;
        public int entertain { get; set; } = 0;
        public string? pay_flow_status { get; set; } = null;
        public int is_test { get; set; } = 0;
        public string? current_pay_method { get; set; } = null;
        public string? customer_type { get; set; } = null;
        public int recepting { get; set; } = 0;
        public double? paying_amount { get; set; } = null;
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
        public List<OrderPayment>? payments { get; set; } = new List<OrderPayment>();
        public List<OrderPaymentRefund>? refunds { get; set; } = new List<OrderPaymentRefund>();
        public List<PaymentShare>? shares { get; set; } = new List<PaymentShare>();
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
                    return payments.Where(p => ((p.pay_method == "微信支付" || p.pay_method == "支付宝") && p.status.Equals("支付成功"))
                     || (p.valid == 1 && p.pay_method != "微信支付" && p.pay_method != "支付宝" && p.status.Equals("支付成功"))).ToList();
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
                            if (refund.refund_id != null && (refund.state == 1 || !refund.refund_id.Trim().Equals("")))
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
        public bool haveEntertain
        {
            get
            {
                if (entertain == 1)
                {
                    return true;
                }
                bool haveEntertain = false;
                for (int i = 0; fdOrders != null && i < fdOrders.Count; i++)
                {
                    if (fdOrders[i].order_type.Trim().Equals("招待"))
                    {
                        haveEntertain = true;
                    }
                }
                for (int i = 0; retails != null && i < retails.Count; i++)
                {
                    if (retails[i].order_type.Trim().Equals("招待"))
                    {
                        haveEntertain = true;
                    }

                }
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (rentals[i].entertain)
                    {
                        haveEntertain = true;
                    }
                }
                return haveEntertain;
            }
        }
        [NotMapped]
        public double entrtainAmount
        {
            get
            {
                double amount = 0;
                for (int i = 0; fdOrders != null && i < fdOrders.Count; i++)
                {
                    FdOrder fd = fdOrders[i];
                    if (fd.valid == 1 && fd.order_type.Trim().Equals("招待"))
                    {
                        amount = amount + fd.summary;
                    }
                }
                for (int i = 0; retails != null && i < retails.Count; i++)
                {
                    if (retails[i].order_type.Trim().Equals("招待"))
                    {
                        amount = amount + retails[i].deal_price;
                    }
                }
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (rentals[i].entertain)
                    {
                        amount += rentals[i].totalSummary;
                    }
                }
                return amount;
            }
        }
        [NotMapped]
        public bool allEntrtain
        {
            get
            {
                if (entertain == 1)
                {
                    return true;
                }
                bool allEntrtain = true;
                for (int i = 0; fdOrders != null && i < fdOrders.Count; i++)
                {
                    if (!fdOrders[i].order_type.Trim().Equals("招待"))
                    {
                        allEntrtain = false;
                    }
                }
                for (int i = 0; retails != null && i < retails.Count; i++)
                {
                    if (!retails[i].order_type.Trim().Equals("招待"))
                    {
                        allEntrtain = false;
                    }
                }
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (!rentals[i].entertain)
                    {
                        allEntrtain = false;
                    }
                }
                return haveEntertain && allEntrtain;
            }
        }
        [NotMapped]
        public double itemDiscountAmount
        {
            get
            {
                double amount = 0;
                for (int i = 0; i < discounts.Count; i++)
                {
                    Discount discount = discounts[i];
                    if (discount.valid == 1 && discount.biz_id != null)
                    {
                        amount += discount.amount;
                    }
                }
                return amount;
            }
        }
        [NotMapped]
        public double orderDiscountAmount
        {
            get
            {
                double amount = 0;
                for (int i = 0; i < discounts.Count; i++)
                {
                    Discount discount = discounts[i];
                    if (discount.valid == 1 && discount.biz_id == null)
                    {
                        amount += discount.amount;
                    }
                }
                return amount;
            }
        }
        [NotMapped]
        public double creditAmount
        {
            get
            {
                return payments == null ? 0 : payments.Where(p => p.valid == 1 && p.is_debt == 1).Sum(p => p.amount);
            }
        }
        [NotMapped]
        public bool haveOnCredit
        {
            get
            {
                return creditAmount > 0;
            }
        }
        [NotMapped]
        public bool allOnCredit
        {
            get
            {
                return creditAmount >= totalCharge;
            }
        }
        [NotMapped]
        public bool haveDiscount
        {
            get
            {
                return discounts.Count > 0;
            }
        }
        [NotMapped]
        public string orderStatus
        {
            get
            {
                string status = "未定义";
                if (totalCharge == 0 && type != "租赁")
                {
                    status = OrderStatus.已下单.ToString();
                }
                else
                {
                    if (paidAmount == 0)
                    {
                        if (dealed == 1)
                        {
                            if (type == "餐饮" || type == "养护")
                            {
                                status = OrderStatus.已下单.ToString();
                            }
                        }
                        else
                        {
                            List<OrderPayment> unpaidPayments = payments
                                .Where(p => p.valid == 1 && p.status.ToString().Equals(OrderPayment.PaymentStatus.待支付.ToString())).ToList();
                            if (payments.Count == 0 || unpaidPayments.Count == 0)
                            {
                                status = OrderStatus.待生成.ToString();
                            }
                            else if (unpaidPayments.Count > 0)
                            {
                                status = OrderStatus.待支付.ToString();
                            }

                        }
                        if (closed == 1)
                        {
                            status = OrderStatus.订单关闭.ToString();
                        }
                    }
                    else
                    {
                        if (paidAmount < totalCharge)
                        {
                            status = OrderStatus.部分支付.ToString();
                        }
                        else
                        {
                            status = OrderStatus.支付成功.ToString();
                        }
                        if (refundAmount > 0)
                        {
                            if (refundAmount < paidAmount)
                            {
                                status = OrderStatus.部分退款.ToString();
                            }
                            else
                            {
                                status = OrderStatus.全额退款.ToString();
                            }
                        }
                        if (debts.Count > 0)
                        {
                            status = OrderStatus.挂账.ToString();
                        }
                    }
                }
                return status;
            }
        }

        [NotMapped]
        public string payType
        {
            get
            {
                if (allEntrtain)
                {
                    return Order.PayType.无需支付.ToString();
                }
                string type = Order.PayType.未支付.ToString();
                if (paying_amount != null && paidAmount == 0)
                {
                    return type;
                }
                if (totalCharge == 0)
                {
                    if (haveEntertain)
                    {
                        type = Order.PayType.招待.ToString();
                    }
                    else
                    {
                        type = Order.PayType.无需支付.ToString();
                    }

                }
                else
                {
                    if (availablePayments.Count == 1)
                    {
                        type = Order.PayType.整单支付.ToString();
                    }
                    else if (availablePayments.Count > 1)
                    {
                        type = Order.PayType.分付.ToString();
                    }
                    else
                    {
                        type = Order.PayType.未支付.ToString();
                    }
                }
                return type;
            }
        }
        [NotMapped]
        public string? payMethod
        {
            get
            {
                string? payMethod = null;
                if (paidAmount == 0 || payType.Trim().Equals("整单支付"))
                {
                    OrderPayment currentPayment = payments
                        .Where(p => p.valid == 1 && p.status.Trim().Equals(OrderPayment.PaymentStatus.待支付.ToString()))
                        .OrderByDescending(p => p.id).FirstOrDefault();
                    if (currentPayment != null)
                    {
                        payMethod = currentPayment.pay_method.Trim();
                    }
                }
                return payMethod;
            }
        }
        [NotMapped]
        public string customerCalledName
        {
            get
            {
                string calledName = "";
                if (contact_name != null)
                {
                    calledName = contact_name;
                }
                if (contact_gender != null)
                {
                    calledName += (" " + ((contact_gender == "男") ? "先生" : (contact_gender == "女" ? "女士" : "")));
                }
                if (calledName == "" && member != null)
                {
                    if (member.real_name != null)
                    {
                        calledName = member.real_name;
                    }
                    if (member.gender != null)
                    {
                        calledName += (" " + ((member.gender == "男") ? "先生" : (member.gender == "女" ? "女士" : "")));
                    }
                }
                return calledName;
            }
        }
        [NotMapped]
        public string customerCell
        {
            get
            {
                if (contact_num != null)
                {
                    return contact_num;
                }
                else if (member != null && member.cell != null && member.cell != "")
                {
                    return member.cell;
                }
                else
                {
                    if (member != null)
                    {
                        return member.contactNum.Trim();
                    }
                    else
                    {
                        return "";
                    }
                }
            }
        }

        [NotMapped]
        public double? totalGuarantyAmount
        {
            get
            {
                double? amount = null;
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (amount == null)
                    {
                        amount = 0;
                    }
                    Rental rental = rentals[i];
                    amount += rental.totalGuarantyAmount;
                }
                return amount;
            }
        }
        [NotMapped]
        public double? totalRentSummaryAmount
        {
            get
            {
                double? amount = null;
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (amount == null)
                    {
                        amount = 0;
                    }
                    Rental rental = rentals[i];
                    amount += rental.totalSummary;
                }
                return amount;
            }
        }
        [NotMapped]
        public double? totalRentNeedToRefundAmount
        {
            get
            {
                if (rentals != null && rentals.Count >= 0)
                {
                    return totalGuarantyAmount - totalRentSummaryAmount;
                }
                return null;
            }
        }
        [NotMapped]
        public double? totalRentUnRefund
        {
            get
            {
                return totalRentNeedToRefundAmount - refundAmount;
            }
        }
        [NotMapped]
        public double? totalRentOverTimeAmount
        {
            get
            {
                double? amount = null;
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (amount == null)
                    {
                        amount = 0;
                    }
                    Rental rental = rentals[i];
                    amount += rental.totalOvertimeAmount;
                }
                return amount;
            }
        }
        [NotMapped]
        public double? totalRentRepairAmount
        {
            get
            {
                double? amount = null;
                for (int i = 0; rentals != null && i < rentals.Count; i++)
                {
                    if (amount == null)
                    {
                        amount = 0;
                    }
                    Rental rental = rentals[i];
                    amount += rental.totalRepairationAmount;
                }
                return amount;
            }
        }
        [NotMapped]
        public RentPropertySet? rentProperties
        {
            get
            {
                if (rentals == null || rentals.Count == 0)
                {
                    return null;
                }
                DateTime? startDate = null;
                DateTime? endDate = DateTime.MinValue;
                double paidGuarantyAmount = 0;
                int paidGuarantyCount = 0;
                int relieveGuarantyCount = 0;
                int settledCount = 0;
                int packageCount = 0;
                int categoryCount = 0;
                double currentRentalAmount = 0;
                double summary = 0;
                for (int i = 0; i < rentals.Count; i++)
                {
                    Rental rental = rentals[i];
                    if (rental.valid != 1)
                    {
                        continue;
                    }
                    if ((startDate == null || (rental.start_date != null && ((DateTime)rental.start_date).Date < ((DateTime)startDate).Date))
                         && rental.start_date != null)
                    {
                        startDate = rental.start_date;
                    }
                    if (rental.end_date == null)
                    {
                        endDate = null;
                    }
                    else if (endDate != null)
                    {
                        if (((DateTime)endDate).Date < ((DateTime)rental.end_date).Date)
                        {
                            endDate = rental.end_date;
                        }
                    }
                    for (int j = 0; j < rental.guaranties.Count; j++)
                    {
                        Guaranty g = rental.guaranties[j];
                        if (g.payStatus == "支付完成")
                        {
                            paidGuarantyCount++;
                            paidGuarantyAmount += (double)g.amount;
                            if (g.relieve == 1)
                            {
                                relieveGuarantyCount++;
                            }
                        }
                    }
                    if (rental.settled == 1)
                    {
                        settledCount++;
                    }
                    if (rental.package_id != null)
                    {
                        packageCount++;
                    }
                    else
                    {
                        categoryCount++;
                    }
                    currentRentalAmount += rental.totalRentalAmount;
                    summary += rental.totalSummary;
                }
                string status = "";
                if (((DateTime)startDate).Date > ((DateTime)biz_date).Date)
                {
                    status = RentStatus.未开始.ToString();
                }
                else
                {
                    if (endDate == null)
                    {
                        status = RentStatus.租赁中.ToString();
                    }
                    if (settledCount < packageCount + categoryCount)
                    {
                        status = RentStatus.部分归还.ToString();
                    }
                    if (settledCount == packageCount + categoryCount)
                    {
                        status = RentStatus.全部归还.ToString();
                    }
                    if (refundAmount > 0)
                    {
                        if (paidAmount - summary <= refundAmount)
                        {
                            if (closed == 1)
                            {
                                status = RentStatus.了结关闭.ToString();
                            }
                            else
                            {
                                status = RentStatus.全额退押金.ToString();
                            }
                        }
                        else
                        {
                            status = RentStatus.部分退押金.ToString();
                        }
                    }
                    //rental.totalRentNeedToRefundAmount

                }
                RentPropertySet property = new RentPropertySet()
                {
                    rentStatus = status,
                    startDate = startDate,
                    endDate = endDate

                };
                return property;
            }
        }

    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections;
using System.Collections.Generic;
using SnowmeetApi.Models.Order;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.IdentityModel.Tokens.Jwt;

namespace SnowmeetApi.Models.Rent
{

    

    [Table("rent_list")]
    public class RentOrder
    {

        

        [Key]
        public int id { get; set; }

        public string open_id { get; set; } = "";

        public string cell_number { get; set; } = "";

        public string real_name { get; set; } = "";

        public string shop { get; set; } = "";

        public int? order_id { get; set; } = 0;

        public double deposit { get; set; } = 0;

        public double deposit_real { get; set; } = 0;

        public double deposit_reduce { get; set; } = 0;

        public double deposit_reduce_ticket { get; set; } = 0;

        public double deposit_final { get; set; } = 0;

        public DateTime start_date { get; set; } = DateTime.Now;

        public DateTime due_end_date { get; set; } = DateTime.Now;

        public DateTime? end_date { get; set; } = null;

        public double rental { get; set; } = 0;

        public double rental_real { get; set; } = 0;

        public double rental_reduce { get; set; } = 0;

        public double rental_reduce_ticket { get; set; } = 0;

        public double rental_final { get; set; } = 0;

        public double refund { get; set; } = 0;

        public string ticket_code { get; set; } = "";

        public int has_guarantee_credit { get; set; } = 0;

        public string guarantee_credit_photos { get; set; } = "";

        public string memo { get; set; } = "";

        public string pay_option { get; set; } = "";

        public string staff_open_id { get; set; } = "";

        public string staff_name { get; set; } = "";

        public int closed { get; set; } = 0;
        public DateTime? finish_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;

        /*
        [NotMapped]
        public OrderOnline _order;
        */

        /*
        [NotMapped]
        public RentOrderDetail[]? _details;
        */
        [NotMapped]
        public string textColor { get; set; } = "black";

        [NotMapped]
        public string backColor { get; set; } = "white";

        [NotMapped]
        public double discount
        {
            get
            {
                
                double discount = 0;
                for (int i = 0; details != null && i < details.Count; i++)
                {
                    discount += details[i].rental_discount;
                }
                return discount;
            }
        }

        [NotMapped]
        public double ticketDiscount
        {
            get
            {
                double discount = 0;
                for (int i = 0; details != null && i < details.Count; i++)
                {
                    discount += details[i].rental_ticket_discount;
                }
                return discount;
            }
        }

        [NotMapped]
        public bool noDeposit
        {
            get
            {
                double rental = 0;
                for (int i = 0; details != null &&  i < details.Count; i++)
                {
                    rental += details[i].real_rental;
                }

                if (Math.Round(rental, 2) >= Math.Round(deposit_final, 2))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }


        /*
        [NotMapped]
        public RentOrderDetail[] details
        {
            get
            {
                return _details;
            }
            set
            {
                _details = value;
            }
        }
        */
        [ForeignKey("rent_list_id")]
        public List<RentOrderDetail> details {get; set;} = new List<RentOrderDetail>();
        
        /*
        [NotMapped]
        public string payMethod { get; set; } = "微信支付";
        */

        public string payMethod{
            get
            {
                if (order != null && order.payments != null)
                {

                    string ret = "";
                    OrderOnline order = this.order;

                    OrderPayment[] payments = order.payments.ToArray();
                    foreach(OrderPayment payment in payments)
                    {
                        if (payment.status.Trim().Equals("支付成功"))
                        {
                            ret = ret +  (ret.Trim().Equals("")? payment.pay_method.Trim() : "," + payment.pay_method.Trim());
                        }
                    }
                    return ret;
                }
                else
                {
                    return "";
                }

            }
        }

        
        /*
        {
            get
            {
                return _order;
            }
            set
            {
                _order = value;
            }
        }
        */
        [NotMapped]
        public string status
        {
            get
            {
                if (finish_date != null)
                {
                    return "已完成";
                }
                if (closed == 1)
                {
                    return "已关闭";
                }
                if (_forceTerminate)
                {
                    return "强行终止";
                }
                string s = "未支付";
                if (order_id == 0)
                {
                    s = "免押金";
                }
                else if (order != null && order.pay_state == 1)
                {
                    s = "已付押金";
                }
                else
                {
                    s = "未支付";
                    if (closed == 1 || create_date <= DateTime.Now.AddHours(-4))
                    {
                        s = "已关闭";
                    }
                    return s;
                }
                bool finish = true;
                double totalRental = 0;
                for (int i = 0; details != null && i < details.Count; i++)
                {
                    string status = details[i].status.Trim();
                    //if (details[i].real_end_date == null && details[i].start_date != null)
                    if (status.Equals("已发放") || status.Equals("已暂存")
                        || (status.Equals("未领取") && !details[i].deposit_type.Trim().Equals("立即租赁")))
                    {
                        finish = false;
                        break;
                    }
                    else
                    {
                        totalRental = totalRental + details[i].real_rental;
                    }
                }
                if (finish)
                {
                    if (order != null && !order.pay_method.Trim().Equals("微信支付") && (refunds == null || refunds.Count == 0))
                    {
                        s = "全部归还";
                    }
                    else if (order != null && ((order.refunds != null && order.refunds.Count > 0) || (Math.Round(totalRental, 2) >= Math.Round(deposit_final, 2))))
                    {
                        s = "已退款";
                    }
                    else
                    {
                        s = "全部归还";
                    }

                }
                return s;
            }
        }

        [NotMapped]
        public bool _forceTerminate = false;

        [NotMapped]
        public List<RentalDetail> rentalDetails
        {
            get
            {
                DateTime startDate = start_date;
                if (startDate.Hour >= 16)
                {
                    startDate = startDate.Date.AddDays(1);
                }
                else
                {
                    startDate = startDate.Date;
                }
                //DateTime endDate = DateTime.Now;

                List<RentalDetail> detailList = new List<RentalDetail>();


                for (int i = 0; details != null && i < details.Count; i++)
                {
                    RentOrderDetail rentOrderDetail = details[i];
                    DateTime endDate = rentOrderDetail.real_end_date == null ? DateTime.Now : (DateTime)rentOrderDetail.real_end_date;
                    if (details[i].start_date != null)
                    {
                        start_date = (DateTime)details[i].start_date;
                        startDate = start_date;
                    }

                    //夜场
                    if (start_date.Date == endDate.Date && start_date.Hour >= 16)
                    {
                        RentalDetail rentalDetail = new RentalDetail();
                        rentalDetail.date = start_date.Date;
                        rentalDetail.item = rentOrderDetail;
                        rentalDetail.rental = rentOrderDetail.real_rental;
                        rentalDetail.type = "夜场";
                        detailList.Add(rentalDetail);
                    }
                    //非夜场
                    else
                    {
                        /*
                        if (endDate.Hour >= 18)
                        {
                            endDate = endDate.Date.AddDays(1);

                        }
                        else
                        {
                            endDate = endDate.Date;
                        }
                        */
                        double totalRental = 0;
                        int count = 0;
                        for (DateTime d = startDate; startDate.Year > 2000 && d.Date <= endDate.Date ; d = d.AddDays(1))
                        {
                            count++;
                            RentalDetail rentalDetail = new RentalDetail();
                            rentalDetail.date = d.Date;
                            rentalDetail.item = rentOrderDetail;

                            //最后一天
                            
                            if (rentOrderDetail.real_end_date != null && d.Date == endDate.Date)
                            {
                                rentalDetail.type = "结算日";
                                rentalDetail.rental = rentOrderDetail.unit_rental 
                                    - rentOrderDetail.rental_discount - rentOrderDetail.rental_ticket_discount;
                                detailList.Add(rentalDetail);
                            }
                            else
                            {
                                if (totalRental + rentOrderDetail.unit_rental > deposit_final)
                                {
                                    _forceTerminate = true;

                                }
                                else
                                {

                                    rentalDetail.type = "";
                                    rentalDetail.rental = rentOrderDetail.unit_rental;
                                    totalRental = rentOrderDetail.unit_rental + totalRental;
                                    detailList.Add(rentalDetail);
                                }
                            }
                            
                        }
                        rentOrderDetail.rental_count = count;
                    }
                    
                }


                return detailList;
            }
        }
        [ForeignKey("order_id")]
        public OrderOnline? order {get; set;}
        [ForeignKey("rent_list_id")]
        public List<RentAdditionalPayment> additionalPayments {get; set;} = new List<RentAdditionalPayment>();
        [NotMapped]
        public double additionalPaidAmount
        {
            get
            {
                double amount = 0;
                for(int i = 0; i < additionalPayments.Count; i++)
                {
                    if (additionalPayments[i].is_paid == 1)
                    {
                        amount += additionalPayments[i].amount;
                    }
                }
                return amount;
            }
        }

        [NotMapped]
        public List<OrderPayment> payments 
        {
            get
            {
                List<OrderPayment> pL = new List<OrderPayment>();
                for(int i = 0; order != null && order.payments != null && i < order.payments.Count; i++)
                {
                    OrderPayment p = order.payments[i];
                    if (p.status.Trim().Equals("支付成功"))
                    {
                        pL.Add(p);
                    }
                }
                for(int i = 0; i < additionalPayments.Count; i++)
                {
                    for(int j = 0; additionalPayments[i].order != null && j < additionalPayments[i].order.payments.Count; j++)
                    {
                        if (additionalPayments[i].order.payments[j].status.Equals("支付成功"))
                        {
                            pL.Add(additionalPayments[i].order.payments[j]);
                        }
                    }
                }
                return pL;
            }
        }
        [NotMapped]
        public double totalCharge
        {
            get
            {
                double charge = 0;
                for(int i = 0; i < payments.Count; i++)
                {
                    if (payments[i].status.Trim().Equals("支付成功"))
                    {
                        charge += payments[i].amount;
                    }
                }
                return charge;
            }
        }
        [NotMapped]
        public List<Models.Order.OrderPaymentRefund> refunds
        {
            get
            {
                List<OrderPaymentRefund> refunds = new List<OrderPaymentRefund>();
                for(int i = 0; i < payments.Count; i++)
                {
                    for(int j = 0; j < payments[i].refunds.Count; j++)
                    {
                        if (payments[i].refunds[j].state == 1)
                        {
                            refunds.Add(payments[i].refunds[j]);
                        }
                    }
                }
                return refunds;
            }
        }
        [NotMapped]
        public double totalRefund
        {
            get
            {
                double refund = 0;
                for(int i = 0; i < refunds.Count; i++)
                {
                    if (refunds[i].state==1)
                    {
                        refund += refunds[i].amount;
                    }
                }
                return refund;
            }
        }
        public string GetPastStatus(DateTime date)
        {
            if (date.Date < create_date.Date)
            {
                return "";
            }
            else
            {
                string ret = "";
                if (this.order != null && this.order.pay_state == 1
                    && this.order.payments != null && this.order.payments.Count > 0)
                {
                    if (this.order.payments[0].create_date.Date >= date.Date)
                    {
                        ret = "已付押金";
                        if (this.order.refunds != null && this.order.refunds.Count > 0)
                        {
                            if (this.order.refunds[0].create_date.Date >= date.Date)
                            {
                                ret = "已退款";
                            }
                        }
                    }
                }
                return ret;
            }
        }

        

    }
}


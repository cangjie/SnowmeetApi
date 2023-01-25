using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections;
using System.Collections.Generic;

namespace SnowmeetApi.Models.Rent
{

    

    [Table("rent_list")]
    public class RentOrder
    {

        

        [Key]
        public int id { get; set; }

        public string open_id { get; set; }

        public string cell_number { get; set; }

        public string real_name { get; set; }

        public string shop { get; set; }

        public int order_id { get; set; }

        public double deposit { get; set; }

        public double deposit_real { get; set; }

        public double deposit_reduce { get; set; }

        public double deposit_reduce_ticket { get; set; } = 0;

        public double deposit_final { get; set; }

        public DateTime start_date { get; set; }

        public DateTime due_end_date { get; set; }

        public DateTime? end_date { get; set; }

        public double rental { get; set; }

        public double rental_real { get; set; }

        public double rental_reduce { get; set; }

        public double rental_reduce_ticket { get; set; }

        public double rental_final { get; set; }

        public double refund { get; set; }

        public string ticket_code { get; set; }

        public int has_guarantee_credit { get; set; }

        public string guarantee_credit_photos { get; set; }

        public string memo { get; set; }

        public string pay_option { get; set; }

        public string staff_open_id { get; set; } = "";

        public string staff_name { get; set; } = "";

        public DateTime create_date { get; set; } = DateTime.Now;

        

        [NotMapped]
        public RentOrderDetail[] details {get; set;}

        [NotMapped]
        public string payMethod { get; set; } = "微信支付";

        [NotMapped]
        public OrderOnline order { get; set; }

        [NotMapped]
        public string status
        {
            get
            {
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
                }
                bool finish = true;
                double totalRental = 0;
                for (int i = 0; details != null && i < details.Length; i++)
                {
                    if (details[i].real_end_date == null)
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
                    s = "全部归还";
                    if ( order != null && ((order.refunds != null && order.refunds.Length > 0) || (totalRental >= deposit_final)))
                    {
                        s = "已退款";
                    }
                }
                return s;
            }
        }

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


                for (int i = 0; details != null && i < details.Length; i++)
                {
                    RentOrderDetail rentOrderDetail = details[i];
                    DateTime endDate = rentOrderDetail.real_end_date == null ? DateTime.Now : (DateTime)rentOrderDetail.real_end_date;
                    

                    //夜场
                    if (startDate.Date == endDate.Date && start_date.Hour >= 16)
                    {
                        RentalDetail rentalDetail = new RentalDetail();
                        rentalDetail.date = startDate.Date;
                        rentalDetail.item = rentOrderDetail;
                        rentalDetail.rental = rentOrderDetail.real_rental;
                        rentalDetail.type = "夜场";
                        detailList.Add(rentalDetail);
                    }
                    //非夜场
                    else
                    {
                        if (endDate.Hour >= 18)
                        {
                            endDate = endDate.Date.AddDays(1);

                        }
                        else
                        {
                            endDate = endDate.Date;
                        }
                        double totalRental = 0;
                        for (DateTime d = startDate; d.Date <= endDate.Date; d = d.AddDays(1))
                        {
                            RentalDetail rentalDetail = new RentalDetail();
                            rentalDetail.date = d.Date;
                            rentalDetail.item = rentOrderDetail;

                            //最后一天
                            if (rentOrderDetail.real_end_date != null && d.Date == endDate.Date)
                            {
                                rentalDetail.type = "结算日";
                                rentalDetail.rental = rentOrderDetail.real_rental - totalRental;
                            }
                            else
                            {
                                rentalDetail.type = "";
                                rentalDetail.rental = rentOrderDetail.unit_rental;
                                totalRental = rentOrderDetail.unit_rental + totalRental;
                            }
                            detailList.Add(rentalDetail);
                        }
                    }
                    
                }


                return detailList;
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
                    && this.order.payments != null && this.order.payments.Length > 0)
                {
                    if (this.order.payments[0].create_date.Date >= date.Date)
                    {
                        ret = "已付押金";
                        if (this.order.refunds != null && this.order.refunds.Length > 0)
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


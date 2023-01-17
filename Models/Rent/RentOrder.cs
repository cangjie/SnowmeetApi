using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
                for (int i = 0; i < details.Length; i++)
                {
                    if (details[i].real_end_date == null)
                    {
                        finish = false;
                        break;
                    }
                }
                if (finish)
                {
                    s = "全部归还";
                    if (order.refunds != null && order.refunds.Length > 0)
                    {
                        s = "已退款";
                    }
                }
                return s;
            }
        }

    }
}


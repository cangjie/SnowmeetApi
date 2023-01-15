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

    }
}


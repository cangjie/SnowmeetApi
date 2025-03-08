using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Models.Order;

namespace SnowmeetApi.Models.Rent
{
    [Table("rent_reward_refund")]
    public class RentRewardRefund
    {
        [Key]
        public int id { get; set; }
        public int rent_reward_id { get; set; }
        public int payment_id {get; set;}
        public int? refund_id { get; set; }
        public double amount { get; set; }
        public int valid { get; set; } = 1;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("payment_id")]
        public OrderPayment? payment {get; set;} = null;
        [ForeignKey("refund_id")]
        public OrderPaymentRefund? refund {get; set;} = null;
    }
}

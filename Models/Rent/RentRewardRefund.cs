using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.Rent
{
    [Table("rent_reward_refund")]
    public class RentRewardRefund
    {
        [Key]
        public int id { get; set; }
        public int rent_reward_id { get; set; }
        public int refund_id { get; set; }
        public double amount { get; set; }
        public int valid { get; set; }
        public DateTime create_date { get; set; }
    }
}

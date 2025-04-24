using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace SnowmeetApi.Models.Rent
{
    [Table("rent_reward")]
    public class RentReward
    {
        [Key]
        public int id { get; set; }
        public int rent_list_id { get; set; }
        public string? mi7_order_id { get; set; }
        public string? memo { get; set; }
        public double amount { get; set; }
        public int valid { get; set; } = 1;
        public int refund_finish { get; set; } = 0;
        public int need_correct { get; set; } = 1;
        public int? correct_rent_list_id { get; set; }
        public int? oper_member_id {get; set;}
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey(nameof(RentRewardRefund.rent_reward_id))]
        public List<RentRewardRefund> rentRewardRefunds { get; set; } = new List<RentRewardRefund>();
        public double totalRefundAmount
        {
            get
            {
                double amount = 0;
                for(int i = 0; i < rentRewardRefunds.Count; i++)
                {
                    amount += rentRewardRefunds[i].refundAmount;
                }
                return amount;
            }
        }
        [NotMapped]
        public Mi7Order? mi7Order {get; set;} = null;
    }
}

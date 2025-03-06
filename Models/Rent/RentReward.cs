using System;
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
        public int valid { get; set; }
        public int refund_finish { get; set; }
        public int need_correct { get; set; }
        public int? correct_rent_list_id { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;

    }
}

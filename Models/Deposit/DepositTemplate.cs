using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Deposit
{
    public class DepositTemplate
    {
        [Key]
        public int id { get; set; }
        public double amount { get; set; }
        public DepositType type { get; set; }
        public string sub_type { get; set; }
        public string name { get; set; }
        public string memo { get; set; }
        public int product_id { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }

    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Deposit
{
    [Table("deposit_balance")]
    public class DepositBalance
    {
        [Key]
        public int id { get; set; }
        public int deposit_id { get; set; }
        public int member_id { get; set; }
        public double amount { get; set; }
        public int? payment_id { get; set; }
        public int? order_id { get; set; }
        public DateTime? extend_expire_date { get; set; }
        public string? memo { get; set; }
        public int? biz_id { get; set; }
        public string? source { get; set; }
        public int valid {get; set;} = 1;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date { get; set; }
    }
}
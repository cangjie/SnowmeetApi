using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Deposit
{
    public enum DepositType
    {
        现金预存,服务储值
    }
    [Table("deposit_account")]
    public class DepositAccount
    {
        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        public string type { get; set; }
        public string sub_type { get; set; }
        public string deposit_no { get; set; }
        public int? order_id { get; set; }
        public int? biz_id { get; set; }
        public string? source { get; set; }
        public string memo { get; set; }
        public double income_amount { get; set; }
        public double consume_amount { get; set; }
        public DateTime? expire_date { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }

        

    }
}
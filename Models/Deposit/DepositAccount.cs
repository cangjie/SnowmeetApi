using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aop.Api.Domain;
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
        public string? biz_id { get; set; }
        public string? source { get; set; }
        public string? memo { get; set; }
        public int valid {get; set;} = 1;
        public double income_amount { get; set; }
        public double consume_amount { get; set; }
        public int? create_member_id {get; set;} = null;
        public DateTime? expire_date { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }
        [ForeignKey("deposit_id")]
        public List<DepositBalance> balances { get; set; }
        [ForeignKey("member_id")]
        public Member member {get; set;}
        public double avaliableAmount
        {
            get
            {
                return income_amount - consume_amount;
            }
        }

        

    }
}
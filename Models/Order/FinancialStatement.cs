using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("financial_statement")]
    public class FinancialStatement
    {
        [Key]
        public int id {get; set;}
        public string pay_type {get; set;}
        public string mch_id {get; set;}
        public string season {get; set;}
        public string month {get; set;}
        public string day_of_week {get; set;}
        public string order_type {get; set;}
        public DateTime trans_date {get; set;}
        public DateTime bill_date {get; set;}
        public string platform_order_no {get; set;}
        public string out_trade_no {get; set;}
        public string flow_no {get; set;}
        public string open_id {get; set;}
        public string op_type {get; set;}
        public string oper {get; set;}
        public string oper_account {get; set;}
        public double? amount {get; set;}
        public double? fee {get; set;}
        public double? settle {get; set;}
        public double? amount_sum {get; set;}
        public double? fee_sum {get; set;}
        public double? settle_sum {get; set;}
        public double? platform_remain {get; set;}
        public double? coming {get; set;}
        public double? bank_remain {get; set;}
        public double? can_withdraw {get; set;}
        public double? refund_amount {get; set;}
        public double? refund_fee {get; set;}
        public double? refund_settle {get; set;}
        public double? withdraw {get; set;}
        public double? charge {get; set;}
    }
}
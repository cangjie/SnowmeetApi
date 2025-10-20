using System;
using System.ComponentModel.DataAnnotations.Schema;
using Humanizer;
using System.Data;

namespace SnowmeetApi.Models.Order
{
	[Table("epayment_daily_biz_report")]
	public class EPaymentDailyReport
	{
        public DateTime biz_date {get; set;}
        public string pay_method {get; set;}
        public string mch_id {get; set;}
        public double last_surplus {get; set;} = 0;
        public double last_receiveable {get; set;} = 0;
        public double last_deposit {get; set;} = 0;
        public double biz_count {get; set;} = 0;
        public double order_amount {get; set;} = 0;
        public double order_fee {get; set;} = 0;
        public double order_balance {get; set;} = 0;
        public double refund_count {get; set;} = 0;
        public double refund_amount {get; set;} = 0;
        public double refund_fee {get; set;} = 0;
        public double refund_balance {get; set;} = 0;
        public double withdraw {get; set;} = 0;
        public double rental {get; set;} = 0;
        public double maintain {get; set;} = 0;
        public double sale {get; set;} = 0;
        public double current_surplus {get; set;} = 0;


        [NotMapped]
        public double computed_surplus { get; set; } = 0;
        [NotMapped]
        public bool isCorrect { get; set; } = true;


    }
}


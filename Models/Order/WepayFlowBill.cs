using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humanizer;

namespace SnowmeetApi.Models.Order
{
	[Table("wepay_downloaded_flowbill")]
	public class WepayFlowBill
	{
        [Key]
        public int id { get; set; }
        public string mch_id {get; set;}
        public DateTime bill_date_time { get; set; }
        public string biz_no {get; set;}
        public string flow_no {get; set;}
        public string biz_name {get; set;}
        public string biz_type {get; set;}
        public string bill_type {get; set;}
        public double amount { get; set; }
        public double surplus { get; set; }
        public string oper {get; set;}
        public string memo { get; set; }
        public string invoice_id {get; set;}

    }
}


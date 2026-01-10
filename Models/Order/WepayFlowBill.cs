using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humanizer;

namespace SnowmeetApi.Models
{
    //资金账单
	[Table("wepay_downloaded_flowbill")]
	public class WepayFlowBill
	{
        [Key]
        public int id { get; set; }
        public string mch_id {get; set;}
        //记账时间
        public DateTime bill_date_time { get; set; }
        //微信支付业务单号
        public string biz_no {get; set;}
        //资金流水单号
        public string flow_no {get; set;}
        //业务名称
        public string biz_name {get; set;}
        //业务类型
        public string biz_type {get; set;}
        //收支类型
        public string bill_type {get; set;}
        //收支金额(元)
        public double amount { get; set; }
        //账户结余(元)
        public double surplus { get; set; }
        //资金变更提交申请人
        public string oper {get; set;}
        //备注
        public string memo { get; set; }
        //业务凭证号
        public string invoice_id {get; set;}

        public int statement_id {get; set;}

    }
}


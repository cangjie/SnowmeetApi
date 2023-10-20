using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aop.Api.Domain;
using Humanizer;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.QueryCapitalBanksByBankAccountResponse.Types;

namespace SnowmeetApi.Models.Order
{
    [Table("wepay_downloaded_balance")]
    public class WepayBalance
	{
        [Key]
		public int  id { get; set;}
        public int summary_id { get; set; }
        public DateTime  trans_date { get; set;}
        public string  app_id { get; set;} 
        public string mch_id { get; set;}       
        public string spc_mch_id { get; set;}          
        public string device_id { get; set;}             
        public string wepay_order_num { get; set;}       
        public string out_trade_no { get; set;}          
        public string open_id { get; set;}               
        public string trans_type { get; set;}            
        public string pay_status { get; set;}            
        public string bank { get; set;}                  
        public string currency { get; set;}              
        public double settle_amount { get; set;}         
        public double coupon_amount { get; set;}         
        public string refund_no { get; set;}             
        public string out_refund_no { get; set;}         
        public double refund_amount { get; set;}         
        public double coupon_refund_amount { get; set;}  
        public string refund_type { get; set;}           
        public string refund_status { get; set;}         
        public string product_name { get; set;}          
        public string product_package { get; set;}       
        public double fee { get; set;}                   
        public string fee_rate { get; set;}              
        public double  order_amount { get; set;}          
        public double  request_refund_amount { get; set;} 
        public string  fee_rate_memo { get; set;}         
	}
}


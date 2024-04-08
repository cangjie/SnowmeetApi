using System;
using System.Collections.Generic;
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
        public int summary_id { get; set; } = 0;
        public DateTime  trans_date { get; set;}
        public string app_id { get; set; } = "";
        public string mch_id { get; set; } = "";
        public string spc_mch_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string wepay_order_num { get; set; } = "";
        public string out_trade_no { get; set; } = "";
        public string open_id { get; set; } = "";
        public string trans_type { get; set; } = "";
        public string pay_status { get; set; } = "";
        public string bank { get; set; } = "";
        public string currency { get; set; } = "";
        public double settle_amount { get; set; } = 0;
        public double coupon_amount { get; set; } = 0;
        public string refund_no { get; set; } = "";
        public string out_refund_no { get; set; } = "";
        public double refund_amount { get; set; } = 0;
        public double coupon_refund_amount { get; set; } = 0;
        public string refund_type { get; set; } = "";
        public string refund_status { get; set; } = "";
        public string product_name { get; set; } = "";
        public string product_package { get; set; } = "";
        public double fee { get; set; } = 0;
        public string fee_rate { get; set; } = "";
        public double order_amount { get; set; } = 0;
        public double request_refund_amount { get; set; } = 0;
        public string fee_rate_memo { get; set; } = "";

        [NotMapped]
        public double drawAmount { get; set; } = 0;
        [NotMapped]
        public string orderType { get; set; } = "";
        [NotMapped]
        public string orderId { get; set; } = "";
        [NotMapped]
        public string shop { get; set; } = "";
        [NotMapped]
        public string mchNo { get; set; } = "";
        [NotMapped]
        public string mchName { get; set; } = "";
        [NotMapped]
        public string cell { get; set; } = "";
        [NotMapped]
        public string real_name { get; set; } = "";
        [NotMapped]
        public string gender { get; set; } = "";
        [NotMapped]
        public double netAmount { get; set; } = 0;
        [NotMapped]
        public double totalRefundAmount { get; set; } = 0;
        [NotMapped]
        public double totalRefundAmountReal { get; set; } = 0;
        [NotMapped]
        public double totalRefundFee { get; set; } = 0;
        [NotMapped]
        public string dayOfWeek { get; set; } = "";
        [NotMapped]
        public List<WepayBalance> refunds { get; set; } = new List<WepayBalance>();
        
        public double receiveable_amount
        {
            get
            {
                return settle_amount - fee;
            }
        }

        public double real_refund_amount
        {
            get
            {
                return refund_amount + fee;
            }
        }
	}
}


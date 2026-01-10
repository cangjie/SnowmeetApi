using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aop.Api.Domain;
using Humanizer;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.QueryCapitalBanksByBankAccountResponse.Types;

namespace SnowmeetApi.Models
{
    [Table("wepay_downloaded_balance")]
    //交易账单
    public class WepayBalance
	{
        [Key]
		public int  id { get; set;}
        public int summary_id { get; set; } = 0;
        //交易时间
        public DateTime  trans_date { get; set;}
        //公众账号ID
        public string app_id { get; set; } = "";
        //商户号
        public string mch_id { get; set; } = "";
        //特约商户号
        public string spc_mch_id { get; set; } = "";
        //设备号
        public string device_id { get; set; } = "";
        //微信订单号
        public string wepay_order_num { get; set; } = "";
        //商户订单号
        public string out_trade_no { get; set; } = "";
        //用户标识
        public string open_id { get; set; } = "";
        //交易类型
        public string trans_type { get; set; } = "";
        //交易状态
        public string pay_status { get; set; } = "";
        //付款银行
        public string bank { get; set; } = "";
        //货币种类
        public string currency { get; set; } = "";
        //应结订单金额
        public double settle_amount { get; set; } = 0;
        //代金券金额
        public double coupon_amount { get; set; } = 0;
        //微信退款单号
        public string refund_no { get; set; } = "";
        //商户退款单号
        public string out_refund_no { get; set; } = "";
        //退款金额
        public double refund_amount { get; set; } = 0;
        //充值券退款金额
        public double coupon_refund_amount { get; set; } = 0;
        //退款类型
        public string refund_type { get; set; } = "";
        //退款状态
        public string refund_status { get; set; } = "";
        //商品名称
        public string product_name { get; set; } = "";
        //商户数据包
        public string product_package { get; set; } = "";
        //手续费
        public double fee { get; set; } = 0;
        //费率
        public string fee_rate { get; set; } = "";
        //订单金额
        public double order_amount { get; set; } = 0;
        //申请退款金额
        public double request_refund_amount { get; set; } = 0;
        //费率备注
        public string fee_rate_memo { get; set; } = "";

        public int statement_id {get; set;} = 0;

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


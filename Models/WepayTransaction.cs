using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace SnowmeetApi.Models
{
	[Table("wepay_transaction")]
	public class WepayTransaction
	{
		[Key]
		public int id { get; set; }
        public string trans_date { get; set; } = "";
        public string official_account_id { get; set; } = "";
        public string mch_id  { get; set; } = "";
        public string dated_mch_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string wepay_order_id { get; set; } = "";
        public string out_trade_no { get; set; } = ""; 
        public string open_id { get; set; } = ""; 
        public string trans_type { get; set; } = "";
        public string trans_status { get; set; } = ""; 
        public string trans_bank { get; set; } = "";
        public string currency { get; set; } = "";
        public string settled_amount { get; set; } = "";
        public string ticket_amount { get; set; } = "";
        public string wepay_refund_no { get; set; } = "";
        public string out_refund_no { get; set; } = "";
        public string refund_amount { get; set; } = "";
        public string ticket_refund_admount { get; set; } = "";
        public string refund_type { get; set; } = "";
        public string refund_status { get; set; } = "";
        public string good_name { get; set; } = "";
        public string good_data_package { get; set; } = "";
        public string fee { get; set; } = "";
        public string fee_rate { get; set; } = "";
        public string order_amount { get; set; } = "";
        public string request_refund_amount { get; set; } = "";
        public string fee_rate_memo { get; set; } = "";
        public string file_name { get; set; } = "";

    }
}


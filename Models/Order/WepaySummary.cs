using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Humanizer;

namespace SnowmeetApi.Models.Order
{
	[Table("wepay_downloaded_summary")]
	public class WepaySummary
	{
        [Key]
        public int id { get; set; }
        public DateTime trans_date { get; set; }
        public int mch_id { get; set; }
        public int trans_num { get; set; }
        public double total_settle_amount { get; set; }
        public double total_refund_amount { get; set; }
        public double coupon_refund_amount { get; set; }
        public double total_fee { get; set; }
        public double total_order_amount { get; set; }
        public double total_request_refund_amount { get; set; }

    }
}


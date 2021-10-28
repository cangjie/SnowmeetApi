using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("wepay_order_refund")]
    public class WepayOrderRefund
    {
        public WepayOrderRefund()
        {

        }

        public int id { get; set; }
        public string wepay_out_trade_no { get; set; }
        public int amount { get; set; }
        public string status { get; set; }
        public string oper_open_id { get; set; }
    }
}

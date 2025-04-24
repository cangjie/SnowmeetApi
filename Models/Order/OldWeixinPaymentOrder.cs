using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
	[Table("weixin_payment_orders")]
	public class OldWeixinPaymentOrder
	{
		[Key]
		public long order_out_trade_no { get; set; }

		public string order_product_id { get; set; }

    }
}


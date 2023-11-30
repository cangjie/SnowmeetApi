using System;
namespace SnowmeetApi.Models.WanLong
{
	public class WanlongOrder
	{
		public string orderId { get; set; }
		public double orderMoney { get; set; }
		public double memOrderMoney { get; set; }
		public int orderState { get; set; }
		public int payType { get; set; }
	}

	public class ZiwoyouPlaceOrderResult
	{
		public int state { get; set; }
		public string msg { get; set; }
		public WanlongOrder data { get; set; }
    }
}


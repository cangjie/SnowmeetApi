using System;
namespace SnowmeetApi.Models.WanLong
{
	public class PayResult
	{
		public int state { get; set; }
		public string msg { get; set; }
		public PayResultData data {get; set;}
	}

	public class PayResultData
	{
		public long orderId { get; set; }
		public int payType { get; set; }
	}
}


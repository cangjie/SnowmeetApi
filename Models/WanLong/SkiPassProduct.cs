using System;
namespace SnowmeetApi.Models
{
	public class ZiwoyouSkipassProduct
	{
		public string productNo { get; set; }
		public string productName { get; set; }
		public string img { get; set; }
		public double settlementPrice { get; set; }
		public double salePrice { get; set; } = 0;
		// 自我游的门市价（市场价）；列表接口里有的商品为空
		public double? marketPrice { get; set; } = null;
		public string orderDesc { get; set; }
		// 景区名（如「【崇礼】万龙滑雪场」「张家口万龙滑雪场盛龙公寓」），用来区分雪票和酒店套餐
		public string? viewName { get; set; } = null;

    }
}


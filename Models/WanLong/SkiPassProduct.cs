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

    }
}


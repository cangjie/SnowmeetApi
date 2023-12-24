using System;
namespace SnowmeetApi.Models.WanLong
{
	public class SkiPassProduct
	{
		public string productNo { get; set; }
		public string productName { get; set; }
		public string img { get; set; }
		public double settlementPrice { get; set; }
		public double salePrice { get; set; } = 0;
		public string orderDesc { get; set; }

    }
}


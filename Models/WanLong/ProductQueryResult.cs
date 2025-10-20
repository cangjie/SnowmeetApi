using System;
namespace SnowmeetApi.Models.WanLong
{
	public class ProductQueryResult
	{
		public int state { get; set; }
		public string msg { get; set; }
		public ProductQueryResultData data { get; set; }
    }

	public class ProductQueryResultData
	{
		public int startIndex { get; set; }
		public int resultNum { get; set; }
		public int size { get; set; }
		public int sizeAll { get; set; }
		public int page { get; set; }
		public int pageCount { get; set; }
		public SkiPassProduct[] results { get; set; }

    }


}


using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Product
{
	[Table("product_resort_ski_pass")]
	public class SkiPass
	{
		[Key]
		public int product_id { get; set; }

        public string resort { get; set; }
		public TimeSpan end_sale_time { get; set; }
		public string rules { get; set; }
		public string available_days { get; set; }
		public string unavailable_days { get; set; }
		public string  tags { get; set; }
		public string? source { get; set; } = null;
		public string? third_party_no { get; set; } = null;
		
		[NotMapped]
		public List<SkipassDailyPrice> dailyPrice {get; set;}
		[NotMapped]
		public Product product {get; set;}

		public bool TagMatch(string[] userTags)
		{
			bool valid = true;

			for (int i = 0; i < userTags.Length; i++)
			{
				bool exists = false;
				foreach (string t in tags.Split(','))
				{
					if (t.Trim().Equals(userTags[i].Trim()))
					{
						exists = true;
						break;
					}
				}
				if (!exists)
				{
					valid = false;
					break;
				}
			}

			return valid;
		}

		public bool DateMatch(DateTime date)
		{
			bool valid = true;

			switch (date.DayOfWeek)
			{
				case DayOfWeek.Saturday:
					if (tags.IndexOf("周六") < 0)
					{
						valid = false;
					}
					break;
				case DayOfWeek.Sunday:
                    if (tags.IndexOf("周日") < 0)
                    {
                        valid = false;
                    }
                    break;
				default:
					break;
            }

			foreach (string s in unavailable_days.Split(','))
			{
				if (!s.Trim().Equals(""))
				{
                    DateTime sDate = DateTime.Parse(s);
                    if (sDate.Date == date.Date)
                    {
                        valid = false;
                        break;
                    }
                }
				
			}


	
			if (!valid)
			{
				if (tags.IndexOf("周六") >= 0)
				{
					
				}
				else if (tags.IndexOf("周六") >= 0)
				{

				}
				else if (tags.IndexOf("节假日") >= 0)
				{
					if (date.Date >= DateTime.Parse("2025-1-28") && date.Date <= DateTime.Parse("2025-2-4").Date)
					{
						valid = true;
					}
				}
				else if (tags.IndexOf("平日") >= 0)
				{
					if (date.Date == DateTime.Parse("2025-1-26").Date)
					{
						valid = true;
					}
				}
				else
				{

				}
			}
			
			

			return valid;
		}
		[NotMapped]
		public List<SkipassDailyPrice> avaliablePriceList
		{
			get
			{
				List<SkipassDailyPrice> list = new List<SkipassDailyPrice>();
				for(int i = 0; dailyPrice != null && i < dailyPrice.Count; i++)
				{
					if (dailyPrice[i].reserve_date.Date >= DateTime.Now.Date)
					{
						list.Add(dailyPrice[i]);
					}
				}
				return list;
			}
		}
		[NotMapped]
		public double commonDayDealPrice
		{
			get
			{
				if (avaliablePriceList == null || avaliablePriceList.Count == 0)
				{
					return 0;
				}
				else
				{
					double ret = 0;
					for(int i = 0; i < avaliablePriceList.Count; i++)
					{
						if (avaliablePriceList[i].reserve_date.Date >= DateTime.Now.Date
							&& avaliablePriceList[i].day_type.Trim().Equals("平日"))
						{
							ret = avaliablePriceList[i].deal_price;
							break;
						}
					}
					return ret;
				}
			}
		}
		[NotMapped]
		public double weekendDealPrice
		{
			get
			{
				if (avaliablePriceList == null || avaliablePriceList.Count == 0)
				{
					return 0;
				}
				else
				{
					double ret = 0;
					for(int i = 0; i < avaliablePriceList.Count; i++)
					{
						if (avaliablePriceList[i].day_type.Trim().Equals("周末"))
						{
							ret = avaliablePriceList[i].deal_price;
							break;
						}
					}
					return ret;
				}
			}
		}

    }
}


using System;
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

			if ((date >= DateTime.Parse("2022-12-31") && date <= DateTime.Parse("2023-1-2"))
				|| (date >= DateTime.Parse("2023-1-21") && date <= DateTime.Parse("2023-1-27")))
			{
				if (tags.IndexOf("节假日") <= 0)
				{
					valid = false;
				}
				else
				{

					valid = true;
				}
			}
			if (date >= DateTime.Parse("2023-1-28") && date <= DateTime.Parse("2023-1-29"))
			{
                if (tags.IndexOf("平日") >= 0)
                {
                    valid = true;
                }
                else
                {

                    valid = false;
                }
            }
			

			return valid;
		}

    }
}


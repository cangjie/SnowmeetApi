using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent")]
	public class RentItem
	{
		[Key]
		public int id { get; set; }
		public string type { get; set; }
		public string name { get; set; }
		public string code { get; set; }
		public double deposit { get; set; }
        public string @class { get; set; }
        public string image { get; set; }
        public string for_age { get; set; }
        public string for_gender { get; set; }
        public string brand { get; set; }
        public string style { get; set; }
        public string scale { get; set; }
        public string grade { get; set; }
        public string bwh { get; set; }


		[NotMapped]
		public double rental { get; set; } = 0;

		public double GetRental(string shop)
		{
			double rental = 0;
			if (shop.IndexOf("南山")>=0)
			{
                if (type.IndexOf("Phenix") >= 0)
                {
                    rental = 300;
                }
                else if (type.IndexOf("Nandn") >= 0 || type.IndexOf("Trake") >= 0
                    || type.IndexOf("Tittalon") >= 0 || type.IndexOf("West Scout") >= 0
                    || type.IndexOf("Burton") >= 0 || type.IndexOf("Swagli") >= 0)
                {
                    //Nandn/Trake/Tittalon/West Scout/Burton/Swagli
                    rental = 150;
                }
                else if (type.IndexOf("双板鞋") >= 0)
                {
                    rental = 100;
                }
                else if (type.IndexOf("双板") >= 0)
                {
                    rental = 300;
                }
                else if (type.IndexOf("雪杖") >= 0)
                {
                    rental = 50;
                }
                else if (type.IndexOf("单板鞋") >= 0)
                {
                    rental = 100;
                }
                else if (type.IndexOf("单板") >= 0)
                {
                    rental = 300;
                }
                else if (type.IndexOf("头盔") >= 0)
                {
                    rental = 70;
                }
                else if (type.IndexOf("雪镜") >= 0)
                {
                    rental = 60;
                }
                else if (type.IndexOf("马甲") >= 0)
                {
                    rental = 100;
                }
                else
                {
                    rental = 0;
                }
            }
			else if (shop.IndexOf("万龙") >= 0)
			{
				if (type.IndexOf("Phenix") >= 0)
				{
					rental = 300;
				}
				else if (type.IndexOf("Nandn") >= 0 || type.IndexOf("Trake") >= 0
					|| type.IndexOf("Tittalon") >= 0 || type.IndexOf("West Scout") >= 0
					|| type.IndexOf("Burton") >= 0 || type.IndexOf("Swagli") >= 0)
				{
					//Nandn/Trake/Tittalon/West Scout/Burton/Swagli
					rental = 150;
				}
				else if (type.IndexOf("双板鞋") >= 0)
				{
					rental = 100;
				}
				else if (type.IndexOf("双板") >= 0)
				{
					rental = 300;
				}
				else if (type.IndexOf("雪杖") >= 0)
				{
					rental = 50;
				}
				else if (type.IndexOf("单板鞋") >= 0)
				{
					rental = 100;
				}
				else if (type.IndexOf("单板") >= 0)
				{
					rental = 300;
				}
                else if (type.IndexOf("头盔") >= 0)
                {
                    rental = 70;
                }
                else if (type.IndexOf("雪镜") >= 0)
                {
                    rental = 60;
                }
                else if (type.IndexOf("马甲") >= 0)
                {
                    rental = 100;
                }
                else
				{
					rental = 0;
				}
            }
			return rental;
		}
	}
}


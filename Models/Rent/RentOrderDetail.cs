using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent_list_detail")]
	public class RentOrderDetail
	{
        public int id { get; set; }

        public int rent_list_id { get; set; }

        public int rent_item_id { get; set; }

        public string rent_item_name { get; set; }

        public string rent_item_code { get; set; }

        public double deposit { get; set; }

        public double unit_rental { get; set; }

        public double real_rental { get; set; }

        public DateTime? real_end_date { get; set; }

	    public string memo { get; set; }

    }
}


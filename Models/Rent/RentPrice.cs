using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
    [Table("rent_price")]
    public class RentPrice
    {
        [Key]
        public int id { get; set; }

        //category item package
        public string type {get; set; }
        public string shop {get;set; }

        public string category_code {get; set; }    
        public int rent_item_id { get; set; }

        public int package_id { get; set; }

        public string day_type {get; set;}

        public double price {get; set;}

        public string scene {get; set;}

        public DateTime update_date {get; set;}



    }
}
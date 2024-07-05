using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
namespace SnowmeetApi.Models.Rent
{
    [Table("rent_category")]
    public class RentCategory
    {
        [Key]
        public string code { get; set; }
        public string name { get; set; }

        public double deposit {get; set;}
        public DateTime update_date { get; set; } = DateTime.Now;

        [NotMapped]
        public ICollection<RentCategory> children { get; set; }

        [NotMapped]
        public ICollection<RentPrice>? priceList {get; set;}

    }
}

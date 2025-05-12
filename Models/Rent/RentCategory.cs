using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
namespace SnowmeetApi.Models
{
    [Table("rent_category")]
    public class RentCategory
    {
        [Key]
        public int id {get; set;}
        public string code { get; set; }
        public string name { get; set; }
        public double deposit {get; set;}
        public DateTime update_date { get; set; } = DateTime.Now;
        [NotMapped]
        public ICollection<RentCategory> children { get; set; }
        public List<RentPrice>? priceList {get; set;}
        public List<RentCategoryInfoField>? infoFields { get; set; }
        public List<RentProduct>? productList { get; set; }
    }
}

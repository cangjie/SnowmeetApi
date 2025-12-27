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
        public int valid { get; set; } = 1;
        public int? staff_id { get; set; } = null;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [NotMapped]
        public ICollection<RentCategory> children { get; set; }
        [NotMapped]
        public RentCategory? father { get; set; } = null;
        public List<RentPrice>? priceList { get; set; }
        public List<RentCategoryInfoField>? infoFields { get; set; }
        public List<RentProduct>? productList { get; set; }
        public List<RentItem> rentItems { get; set; } = new List<RentItem>();
    }
}

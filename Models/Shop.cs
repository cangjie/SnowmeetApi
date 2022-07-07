using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("shop_list")]
    public class Shop
    {
        [Key]
        public int id { get; set; }
        public string name { get; set; }
        public int sort { get; set; }
        public double lat_from { get; set; }
        public double lat_to { get; set; }
        public double long_from { get; set; }
        public double long_to { get; set; }
    }
}


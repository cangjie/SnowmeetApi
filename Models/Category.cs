using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("category")]
    public class Category
    {
        [Key]
        public int id { get; set; }
        public string biz_type { get; set; }
        public string? code { get; set; } = null;
        public string name { get; set; }
        public int valid { get; set; } = 0;
        public int hide { get; set; } = 1;
        public int sort { get; set; } = 100;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        public List<Product> products { get; set; } = new List<Product>();
    }
}
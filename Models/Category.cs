using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
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
        public List<CategoryProperty> properties { get; set; } = new List<CategoryProperty>();
        [NotMapped]
        public List<CategoryProperty> availableProperties
        {
            get
            {
                return properties.Where(a => a.valid == 1).ToList();
            }
        }
    }
    [Table("category_property")]
    public class CategoryProperty
    {
        [Key]
        public int id { get; set; }
        public int category_id { get; set; }
        public string property_name { get; set; }
        public int sort { get; set; } = 0;
        public int valid { get; set; } = 0;
        public int is_option { get; set; } = 0;
        public int multi_selected { get; set; } = 0;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("category_id")]
        public Category category { get; set; }
        public List<CategoryPropertyOption> options { get; set; } = new List<CategoryPropertyOption>();
        [NotMapped]
        public List<CategoryPropertyOption> availableOptions
        {
            get
            {
                return options.Where(o => o.valid == 1).ToList();
            }
        }
    }
    [Table("category_property_option")]
    public class CategoryPropertyOption
    {
        [Key]
        public int id { get; set; }
        public int category_property_id { get; set; }
        public string option_value { get; set; }
        public int valid { get; set; }
        public int sort { get; set; } = 0;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("category_property_id")]
        public CategoryProperty categoryProperty { get; set; }
        [NotMapped]
        public bool is_checked {get; set;} = false;
    }

}
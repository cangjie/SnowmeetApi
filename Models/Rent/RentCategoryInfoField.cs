using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("rent_category_info_field")]
    public class RentCategoryInfoField
    {
        [Key]
        public int id { get; set; }
        public int category_id { get; set; } 
        public string field_name { get; set; }
        public int is_delete {get;set;}
        public int sort {get;set;}
        public DateTime update_date {get;set;}
        [ForeignKey("category_id")]
        public RentCategory? category{ get; set; }

    }
}
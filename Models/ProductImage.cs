using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("product_image")]
    public class ProductImage
    {
        public int id { get; set; }
        public int product_id { get; set; }
        public string image_url { get; set; }
        public int valid { get; set; }
        public int is_head { get; set; }
        public int sort { get; set; }
        public string? title { get; set; } = null;
        public string? content { get; set; } = null;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; }
        [ForeignKey("product_id")]
        public Product product { get; set; }
    }
}
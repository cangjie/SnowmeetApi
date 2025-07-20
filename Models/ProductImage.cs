using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("product_image")]
    public class ProductImage
    {
        public int id { get; set; }
        public int product_id { get; set; }
        public int? upload_id { get; set; } = null;
        public string? image_url { get; set; } = null;
        public int valid { get; set; }
        public int is_head { get; set; }
        public int sort { get; set; } = 0;
        public string? title { get; set; } = null;
        public string? content { get; set; } = null;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; }
        [ForeignKey("product_id")]
        public Product product { get; set; }
        [ForeignKey("upload_id")]
        public UploadFile? uploadFile { get; set; } = null;
    }
}
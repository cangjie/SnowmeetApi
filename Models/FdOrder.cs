using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace SnowmeetApi.Models
{
    [Table("fd_order")]
    public class FdOrder
    {
        [Key]
        public int id { get; set; }
        public int order_id { get; set; }
        public int product_id { get; set; }
        public string product_name { get; set; }
        public double unit_price { get; set; }
        public string order_type { get; set; }
        public int count { get; set; }
        public string memo { get; set; } = "";
        public int valid { get; set; } = 1;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("order_id")]
        public Order order { get; set; }
        [ForeignKey("product_id")]
        public Product product { get; set; }
        [ForeignKey(nameof(Discount.sub_biz_id))]
        public List<Discount> discounts { get; set; } = new List<Discount>();
    }
}
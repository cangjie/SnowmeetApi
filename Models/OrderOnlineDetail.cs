using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("order_online_detail")]
    public class OrderOnlineDetail
    {
        [Key]
        public int id { get; set; }
        [Column("order_online_id")]
        public int OrderOnlineId { get; set; }
        public int product_id { get; set; }
        public string product_name { get; set; }
        public double price { get; set; }
        public int count { get; set; }
        public double retail_price { get; set; }

        public OrderOnline order { get; set; }

        
    }
}

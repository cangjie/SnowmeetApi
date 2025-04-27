using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("retail")]
    public class Retail
    {
        [Key]
        public int id { get; set; }
        public int? order_id {get; set;} = null;
        public string? mi7_code {get; set;} = null;
        public double sale_price {get; set;} = 0;
        public double deal_price {get; set;} = 0;
        public string? order_type {get; set;} = null;
        public int valid {get; set;} = 1;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
        [NotMapped]
        public string textColor { get; set; } = "";
        [NotMapped]
        public string backgroundColor { get; set; } = "";
        [ForeignKey("order_id")]
        public Order? order {get; set;} = null;
    }
}
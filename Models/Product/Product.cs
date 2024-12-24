using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace SnowmeetApi.Models.Product
{
    [Table("product")]
    public class Product
    {
        [Key]
        public int id { get; set; }
        public string name { get; set; }
        public double sale_price { get; set; }
        public double? market_price { get; set; } = null;
        public double? cost { get; set; } = null;
        public string type { get; set; }
        public string shop { get; set; }
        public int hidden { get; set; }
        public int sort { get; set; }
        public int resort_id { get; set; }
        public int stock_num { get; set; }
        public double deposit { get; set; }
        public double prepay { get; set; }
        public DateTime start_date { get; set; }
        public DateTime end_date { get; set; }
        public string intro { get; set; }
        public int ticket_template_id { get; set; }
        public string principal { get; set; }
        public int award_score { get; set; }
    }
}

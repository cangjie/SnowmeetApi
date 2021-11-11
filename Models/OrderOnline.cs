using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
namespace SnowmeetApi.Models
{
    [Table("order_online")]
    public class OrderOnline
    {
        [Key]
        public int id { get; set; }
        public string type { get; set; }
        public int pay_state { get; set; }
        public DateTime? pay_time { get; set; }
        public string out_trade_no { get; set; }
        public string pay_method { get; set; }
        public string open_id { get; set; }
        public double order_real_pay_price { get; set; }
        public double order_price { get; set; }
        public string cell_number { get; set; }
        public string shop { get; set; }
        public string name { get; set; }
        
        public List<OrderOnlineDetail> details { get; set; }
    }
}

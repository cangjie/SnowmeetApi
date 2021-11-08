using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
namespace SnowmeetApi.Models
{
    [Table("order_online")]
    public class OrderOnline
    {
        [Key]
        public int id { get; set; }
        public int pay_state { get; set; }
        public DateTime pay_time { get; set; }
        public string out_trade_no { get; set; }
        public string pay_method { get; set; }
        public string open_id { get; set; }
        public double order_real_pay_price { get; set; }
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("expierence_list")]
    public class Experience
    {
        [Key]
        public int id { get; set; }



        public string shop { get; set; } = "";
        public double guarantee_cash { get; set; } = 0;
        public int guarantee_order_id { get; set; } = 0;
        public string cell_number { get; set; } = "";
        public string open_id { get; set; } = "";
        public string return_memo { get; set; } = "";
        public double refund_amount { get; set; } = 0;

        public string ticket_code { get; set; } = "";

        public string staff_open_id { get; set; } = "";

        public string asset_name { get; set; } = "";
        public string asset_scale { get; set; } = "";
        public string asset_photos { get; set; } = "";

        public DateTime? start_time { get; set; } = DateTime.Now;

        public DateTime? end_time { get; set; } = DateTime.Now.AddHours(2);

        public DateTime create_date { get; set; } = DateTime.Now;

        [ForeignKey("guarantee_order_id")]
        public OrderOnline order { get; set; } = null;
        
    }
}

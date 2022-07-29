using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.ComponentModel;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Models
{
    [Table("order_online")]
    public class OrderOnline
    {

        
        [Key]
        public int id { get; set; }

        public string type { get; set; }
        public string open_id { get; set; }
        public string cell_number { get; set; }
        public string name { get; set; }
        public string pay_method { get; set; }
        public double order_price { get; set; }
        public double order_real_pay_price { get; set; }
        public int pay_state { get; set; }
        public DateTime? pay_time { get; set; }
        public string code { get; set; }
        public string syssn { get; set; }
        public string memo { get; set; } = "";
        public string shop { get; set; }
        public int? mchid { get; set; }
        public double ticket_amount { get; set; }
        public double score_rate { get; set; }
        public int generate_score { get; set; }
        public string ticket_code { get; set; }
        public string out_trade_no { get; set; }
        public string pay_memo { get; set; } = "全额支付";
        public double other_discount { get; set; }
        public double final_price { get; set; }

        [NotMapped]
        public OrderPayment[]? payments { get; set; }
        [NotMapped]
        public MiniAppUser? user { get; set; }
        [NotMapped]
        public Mi7Order[]? mi7Orders { get; set; }
        
        
        /*
        
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
        public string code { get; set; }
        public string ticket_code { get; set; }

        [DefaultValue("")]
        public string memo { get; set; } = "";
        */
        //public List<OrderOnlineDetail> details { get; set; }
    }
}

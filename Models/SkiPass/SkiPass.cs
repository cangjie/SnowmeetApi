using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace SnowmeetApi.Models.SkiPass
{
    [Table("ski_pass")]
    public class SkiPass
    {
        public int id {get; set;}
        public int member_id {get; set;}
        public string? wechat_mini_openid {get; set;}
        public int product_id {get; set;}
        public string resort {get; set;}
        public string product_name {get; set;}
        public int count {get; set;}
        public int? order_id {get; set;}
        public double? deal_price {get; set;}
        public double? ticket_price {get; set;}
        public double? deposit {get; set;}
        public double? fee {get; set;} = null;
        public double? refund_amount {get; set;} = null;
        public int? valid {get; set;} = 0;
        public int? have_refund {get; set;} = null;

        public string? card_no {get; set;} = null;

        public string? card_image_url {get; set;} = null;
        public DateTime? card_member_pick_time {get; set;} = null;
        public DateTime? card_member_return_time {get; set;} = null;

        public int? card_lost {get; set;} = null;

        public string? out_order_no {get; set;} = null;
        public string? reserve_no {get; set;} = null;
        public string? qr_code_url {get; set;} = null;
        public DateTime? reserve_date {get; set;} = null;
        
        public string? contact_name {get; set;} = null;
        public string? contact_cell {get; set;} = null;
        public string? contact_id_type {get; set;} = null;
        public string? contact_id_no {get; set;} = null;
        public DateTime update_date {get; set;} = DateTime.Now;
        public DateTime create_date {get; set;} = DateTime.Now;
    }
}
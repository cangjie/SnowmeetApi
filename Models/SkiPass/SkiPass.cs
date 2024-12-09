using System;
using System.ComponentModel.DataAnnotations.Schema;

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
        public double? fee {get; set;}
        public double? refund_amount {get; set;}
        public int? valid {get; set;}
        public int? have_refund {get; set;}

        public string? card_no {get; set;}

        public string? card_image_url {get; set;}
        public DateTime? card_member_pick_time {get; set;} = null;
        public DateTime? card_member_return_time {get; set;} = null;


        public string? out_order_no {get; set;}
        public string? reserve_no {get; set;}
        public string? qr_code_url {get; set;}
        public DateTime? reserve_date {get; set;}
        
        public string? contact_name {get; set;}
        public string? contact_cell {get; set;}
        public string? contact_id_type {get; set;}
        public string? contact_id_no {get; set;}
        public DateTime update_date {get; set;}
        public DateTime create_date {get; set;}
    }
}
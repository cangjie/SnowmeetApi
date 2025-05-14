using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NuGet.Common;

namespace SnowmeetApi.Models
{
    [Table("discount")]
    public class Discount
    {
        [Key]
        public int id {get; set;}
        public string? ticket_code {get; set;}
        public double amount {get; set;}
        public int? order_id {get; set;}
        public string? biz_type {get; set;}
        public int? biz_id {get; set;}
        public string? sub_biz_type {get; set;}
        public int? sub_biz_id {get; set;}
        public int? staff_id {get; set;}
        public int? member_id {get; set;}
        public int valid {get; set;}
        public DateTime? update_date {get; set;}
        public DateTime create_date {get; set;} = DateTime.Now;
        [ForeignKey("order_id")]
        public SnowmeetApi.Models.Order? order {get; set;} = null;
    }
}
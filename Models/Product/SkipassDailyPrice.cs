using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Product
{
    [Table("product_resort_ski_pass_price")]
    public class SkipassDailyPrice
    {
        [Key]
        public int id {get; set;}
        public int product_id {get; set;}
        public int third_party_id {get; set;}
        public DateTime reserve_date {get; set;}
        public double settlementPrice {get; set;}
        public double salePrice {get; set;}
        public double marketPrice {get; set;}
        public string day_type {get; set;}
        public int valid  {get; set;}

    }
}
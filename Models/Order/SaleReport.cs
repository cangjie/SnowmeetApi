using System;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    public class SaleReport
    {
        public string mi7_order_id { get; set; } = "";
        public string barCode { get; set; } = "";
        public double sale_price { get; set; } = 0;
        public double real_charge { get; set; } = 0;
        public int order_id { get; set; } = 0;
        public string? name { get; set; } = "";
        public string? cell_number { get; set; } = "";
        public double final_price { get; set; } = 0;
        public string shop { get; set; } = "";
        public string staff { get; set; } = "";
        public DateTime? pay_time { get; set; }
        public string pay_method { get; set; } = "";

        public string memo {get; set; } = "";
    }
}


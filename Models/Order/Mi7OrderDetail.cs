using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.CreateApplyForSubMerchantApplymentRequest.Types.Business.Types.SaleScene.Types;

namespace SnowmeetApi.Models.Order
{
    [Table("mi7_order_detail")]
    public class Mi7OrderDetail
    {
        [Key]
        public int id { get; set; }
        public DateTime order_date { get; set; }
        public int mi7_order_id { get; set; }
        public string customer_mi7_order { get; set; }
        public string customer_mi7_name { get; set; }
        public string product_code { get; set; }
        public string product_name { get; set; }
        public string product_class { get; set; }
        public string product_scale { get; set; }
        public string product_properties { get; set; }
        public string unit { get; set; }
        public string barcode { get; set; }
        public string storage { get; set; }
        public int count { get; set; }
        public double product_price { get; set; }
        public double discount_rate { get; set; }
        public double sale_price { get; set; }
        public double charge_summary { get; set; }
        public double total_cost { get; set; }
        public int original_file_id { get; set; }
        public int updated_file_id { get; set; } = 0;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime update_date { get; set; } = DateTime.Now;
    }
}


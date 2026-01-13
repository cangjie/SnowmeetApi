using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("mi7_imported_order_detail")]
    public class Mi7ExportedSaleDetail
    {
        [Key]
        public int id { get; set; }
        public DateTime? biz_date { get; set; }
        public string? deliver_method { get; set; }
        public DateTime? deliver_date { get; set; }
        public string? customer_contact { get; set; }
        public string? contact_cell { get; set; }
        public string? deliver_addr { get; set; }
        public string? mi7_code { get; set; }
        public string? shop { get; set; }
        public string? associate_order_code { get; set; }
        public string? customer_code { get; set; }
        public string? customer_name { get; set; }
        public string? customer_class { get; set; }
        public string? product_code { get; set; }
        public string? product_name { get; set; }
        public string? product_category { get; set; }
        public string? product_scale { get; set; }
        public string? product_property { get; set; }
        public string? product_unit { get; set; }
        public string? product_barcode { get; set; }
        public string? store { get; set; }
        public int? count { get; set; }
        public double? unit_price { get; set; }
        public double? unit_discount { get; set; }
        public double? real_unit_price { get; set; }
        public double? total_amount { get; set; }
        public double? cost { get; set; }
        public string? weight { get; set; }
        public string? cubage { get; set; }
        public string? memo { get; set; }
        public string? recept_staff { get; set; }
        public string? creater_staff { get; set; }
        public string? inner_memo { get; set; }
        public string? express_company { get; set; }
        public string? way_bill_no { get; set; }
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
    }
}
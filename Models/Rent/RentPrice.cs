using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("rent_price")]
    public class RentPrice
    {
        public enum DayType {平日, 周末, 节假日}
        public enum RentType {日场, 下午场, 夜场, 午加夜, 日加夜, 多日}
        public enum Scene {门市, 预约, 会员}
        [Key]
        public int id { get; set; }
        public string type { get; set; }
        public int? shop_id { get; set; } = null;
        public string shop { get; set; } = "";
        public int? category_id { get; set; } = null;
        public int? rent_item_id { get; set; } = null;
        public int? package_id { get; set; } = null;
        public string? day_type { get; set; } = null;
        public double? price { get; set; } = 0;
        public string? scene { get; set; } = null;
        public string? rent_type { get; set; } = null;
        public int valid { get; set; }
        public int? staff_id { get; set; } = null;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("category_id")]
        public RentCategory? category { get; set; }
        [ForeignKey("package_id")]
        public RentPackage? rentPackage { get; set; }
        [ForeignKey("shop_id")]
        public Shop? refShop { get; set; } = null;
    }
}
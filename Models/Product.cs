using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Models
{
    [Table("product")]
    public class Product
    {
        [Key]
        public int id { get; set; }
        public int? category_id { get; set; } = null;
        public int? shop_id { get; set; } = null;
        public string name { get; set; }
        public string? content { get; set; } = null;
        public double sale_price { get; set; }
        public double? market_price { get; set; } = null;
        public double? cost { get; set; } = null;
        public string? type { get; set; } = null;
        public string? shop { get; set; } = null;
        public int hidden { get; set; } = 1;
        public int valid { get; set; } = 0;
        public int on_shelves { get; set; } = 0;
        public int no_entrain { get; set; } = 0;
        public int sort { get; set; } = 100;
        public int? resort_id { get; set; } = null;
        public int? stock_num { get; set; } = null;
        public double? deposit { get; set; } = null;
        public double? prepay { get; set; } = null;
        public DateTime? start_date { get; set; } = null;
        public DateTime? end_date { get; set; } = null;
        public string? intro { get; set; } = null;
        public int? ticket_template_id { get; set; } = null;
        public string? principal { get; set; } = null;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public int sell_out { get; set; } = 0;
        public int? award_score { get; set; } = null;
        public int? punch_total { get; set; } = null; // 仅次卡类商品有意义：该 SKU 赠送的总次数
        // 关联 category.code（稳定手工维护值，不随 category_id 自增变化）。次卡类商品的权威识别方式
        // 是 category_code 命中 category 表中 biz_type="租赁"/"养护" 且 name="次卡" 的那一行的 code，
        // 不再用 type 字符串（type 仍保留写入，兼容存量/其它用途，但查询判定一律以 category_code 为准）。
        public string? category_code { get; set; } = null;
        [ForeignKey("category_id")]
        public Category? category { get; set; } = null;
        public ProductTicketTemplate? productTicketTemplate {get; set;} = null;
        public List<ProductImage> images { get; set; } = new List<ProductImage>();
        public List<ProductProperty> properties { get; set; } = new List<ProductProperty>();
        public List<ProductStock> stocks { get; set; } = new List<ProductStock>();
        [NotMapped]
        public List<ProductImage> availableImages
        {
            get
            {
                return images.Where(i => i.valid == 1).ToList();
            }
        }
        [NotMapped]
        public List<ProductProperty> availableProperties
        {
            get
            {
                return properties.Where(p => p.valid == 1).OrderBy(p => p.sort).ToList();
            }
        }
    }
    [Table("product_property")]
    public class ProductProperty
    {
        [Key]
        public int id { get; set; }
        public int product_id { get; set; }
        public int category_property_id { get; set; }
        public int? option_id { get; set; } = null;
        public string? text_value { get; set; } = null;
        public int valid { get; set; } = 0;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("product_id")]
        public Product product { get; set; }
        [ForeignKey("category_property_id")]
        public CategoryProperty categoryProperty { get; set; }
        [NotMapped]
        public int sort
        {
            get
            {
                if (categoryProperty != null)
                {
                    return categoryProperty.sort;
                }
                return 100;
            }
        }
        [NotMapped]
        public string title
        {
            get
            {
                if (categoryProperty != null)
                {
                    return categoryProperty.property_name;
                }
                return "未知属性";
            }
        }
        [NotMapped]
        public string value
        {
            get
            {
                string ret = "";
                if (categoryProperty == null)
                {
                    return "";
                }
                foreach (CategoryPropertyOption option in categoryProperty.options)
                {
                    if (option.id == option_id)
                    {
                        ret = option.option_value;
                        break;
                    }
                }
                return ret;
            }
        }
    }
    [Table("product_stock")]
    public class ProductStock
    {
        [Key]
        public int id { get; set; }
        public int product_id { get; set; }
        public int delta { get; set; }
        public int sum { get; set; }
        public string memo { get; set; } = "";
        public int? staff_id { get; set; } = null;
        public int? order_id { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("product_id")]
        public Product product { get; set; }
    }
}

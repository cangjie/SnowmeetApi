using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("rent_product")]
    public class RentProduct
    {
        [Key]
        public int id { get; set; }
        public string owner { get; set; } = "自有";
        public string name { get; set; }
        public string? description { get; set; } 
        public string? brand { get; set; }
        public string? shop {get; set;}
        public int category_id { get; set; }
        public string? barcode { get; set; }
        public int? count {get; set;}
        public double? deposit {get; set; }
        public int is_common_price {get; set; } = 1;
        public int is_delete {get; set; } = 0;
        public int is_valid {get; set; } = 0;
        public int is_online {get;set;} = 0;

        public int is_destroyed {get; set;} = 0;

        public int creator_memberid {get; set;}

        [NotMapped]
        public ICollection<RentProductImage> images { get; set; }
        [NotMapped]
        public List<RentProductDetailInfo> detailInfo { get; set; }
       

    }

    [Table("rent_product_image")]
    public class RentProductImage
    {
        [Key]
        public int id {get; set;}
        public int product_id {get; set;}
        public string image_url {get; set;}
        public int sort {get; set;} = 0;
        public DateTime update_date {get; set;}
    }

    [Table("rent_product_detail_info")]
    public class RentProductDetailInfo
    {
        public int product_id {get; set;}
        public int field_id {get; set;}
        public string info {get; set;}
        public DateTime update_date {get; set;}
        [NotMapped]
        public string fieldName {get; set;} = "";
        [NotMapped]
        public RentCategoryInfoField field {get; set;}
    }

}
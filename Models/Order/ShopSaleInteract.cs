using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.Order
{
    [Table("shop_sale_interact")]
    public class ShopSaleInteract
    {
        [Key]
        public int id { get; set; }
        public string staff_mapp_open_id { get; set; }
        public string scaner_oa_open_id { get; set; } = "";
        public int scan { get; set; } = 0;
        public DateTime create_date { get; set; } = DateTime.Now;

        [NotMapped]
        public string scanUserMAppOpenId { get; set; } = "";
    }
}


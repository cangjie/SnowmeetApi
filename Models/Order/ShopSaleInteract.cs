using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.Order
{
    [Table("shop_sale_interact")]
    public class ShopSaleInteract
    {
        public MiniAppUser _miniAppUser;

        [Key]
        public int id { get; set; }
        public string staff_mapp_open_id { get; set; }
        public string scaner_oa_open_id { get; set; } = "";
        public int scan { get; set; } = 0;
        //public string scan_type { get; set; } = "recept";
        public DateTime create_date { get; set; } = DateTime.Now;

        [NotMapped]
        public MiniAppUser miniAppUser 
        {
            get
            {
                return _miniAppUser;
            }
            set {
                _miniAppUser = value;
            } 
        }

        [NotMapped]
        public Member member {get; set;} = null;
    }
}


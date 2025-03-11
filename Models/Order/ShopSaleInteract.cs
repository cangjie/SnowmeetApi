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
        public string scan_type { get; set; } = "";
        public int? biz_id {get; set;} = null;
        public string? scaner_mini_open_id {get; set;} = null;
        public string? cell {get; set;}
        public int? scaner_member_id {get; set;}
        public int? staff_member_id {get; set;}
        public int? auth_manager_member_id {get; set;} = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public bool needAuth
        {
            get
            {
                bool need = false;
                if (cell != null && cell.Length == 11 && scaner_mini_open_id != null)
                {
                    need = true;
                }
                return need;
            }
        }
        public bool haveAuthed
        {
            get
            {
                bool authed = false;
                if (needAuth && auth_manager_member_id != null)
                {
                    authed = true;
                }
                return authed;
            }
        }


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
        public Member? member {get; set;} = null;
        [ForeignKey("scaner_member_id")]
        public Member? scanMember {get; set;} = null;
        [ForeignKey("staff_member_id")]
        public Member? staffMember {get; set;} = null;
        
    }
}


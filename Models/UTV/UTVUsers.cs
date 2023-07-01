﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.UTV
{
    [Table("utv_users")]
    public class UTVUsers
    {
        [Key]
        public int id { get; set; }
        public int user_id { get; set; }
        public string wechat_open_id { get; set; }
        public string tiktok_open_id { get; set; }
        public string real_name { get; set; }
        public string cell { get; set; }
        public string driver_license { get; set; }  

    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models.Users
{
    [Table("users")]
    public class OfficialAccoutUser
    {
        [Key]
        public string open_id { get; set; }
        public string nick { get; set; }
        public string head_image { get; set; }
        public int vip_level { get; set; }
        public int is_admin { get; set; }
        public int is_instructor { get; set; }
        public int is_resort_staff { get; set; }
        public int qr_code_scene { get; set; }
        public string father_open_id { get; set; }
        public int is_subscribe { get; set; }
        public DateTime update_time { get; set; }
        public string cell_number { get; set; }
        public string memo { get; set; }
        public string gender { get; set; }
        public string real_name { get; set; }
    }
}

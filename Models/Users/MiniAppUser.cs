using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models.Users
{
    [Table("mini_users")]
    public class MiniAppUser
    {
        [Key]
        public string open_id { get; set; }
        public string cell_number { get; set; }
        public string real_name { get; set; }
        public string nick { get; set; }
        public string head_image { get; set; }
        public string gender { get; set; }
        public int blocked { get; set; }
        public int is_admin { get; set; }
    }
}

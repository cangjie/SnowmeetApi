using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models.Users
{
    [Table("mini_users")]
    public class MiniAppUser
    {
        [Key]
        public string open_id { get; set; }
        public string union_id { get; set; }
        public string cell_number { get; set; } = "";
        public string real_name { get; set; } = "";
        public string nick { get; set; } = "";
        public string head_image { get; set; } = "";
        public string gender { get; set; } = "";
        public int blocked { get; set; } = 0;
        public int is_admin { get; set; } = 0;
        public int member_id { get; set; } = 0;

        public string wechat_id { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;

        [NotMapped]
        public bool isMember { get; set; } = false;



        /*
        public static implicit operator MiniAppUser(ActionResult<MiniAppUser> v)
        {
            throw new NotImplementedException();
        }
        */
    }

    [NotMapped]
    public class MiniAppUserList
    {
        public int status { get; set; } = 0;
        public int count { get; set; } = 1;
        public MiniAppUser[] mini_users { get; set; }
    }
}

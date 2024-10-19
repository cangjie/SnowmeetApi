using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Users
{
    [Table("member")]
    public class Member
    {
        [Key]
        public int id { get; set; }
        public string real_name { get; set; }
        public string gender { get; set; }
        public int is_merge {get; set; } = 0;
        public int? merge_id {get; set;}
        public string source {get; set; }

        public int is_staff { get; set; }
        public int is_manager { get; set;}
        public int is_admin { get; set; }
        public int in_staff_list {get; set;}


        
        public ICollection<MemberSocialAccount> memberSocialAccounts { get; set; } = new List<MemberSocialAccount>();
        

    }
}
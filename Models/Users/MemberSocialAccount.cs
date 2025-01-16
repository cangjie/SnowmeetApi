using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Users
{
    [Table("member_social_account")]
    public class MemberSocialAccount
    {
        [Key]
        public int id { get; set; }

        //[Column("member_id")]
        [ForeignKey(nameof(Models.Users.Member))]
        public int member_id { get; set; }

        public string type {get; set;}

        public string num { get; set; }

        public int valid {get; set;} = 1;

        public string memo {get; set; } = "";
        
       
        public Models.Users.Member member { get; set; }

    }
}
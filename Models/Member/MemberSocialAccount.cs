using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("member_social_account")]
    public class MemberSocialAccount
    {
        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        public string type {get; set;}
        public string num { get; set; }
        public int valid {get; set;} = 1;
        public string memo {get; set; } = "";
        [ForeignKey("member_id")]
        public Member member { get; set; }

        //will delete
        public List<OrderOnline> orders { get; set; } = new List<OrderOnline>();

    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("kol")]
    public class Kol
    {
        [Key]
        public int id { get; set; }
        public string real_name {get; set;}
        public string wechat_open_id {get; set;}
        public string ali_login_name {get; set;}
        public int wechat_bind {get; set;}
        public int ali_bind{get;set;}
        public string memo {get; set;}
        public int member_id {get; set;}
    }
}
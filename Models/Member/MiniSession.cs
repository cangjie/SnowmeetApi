using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models
{
    [Table("mini_session")]
    public class MiniSession
    {
        public string session_key { get; set; }
        public string session_type { get; set; } = "";
        public int valid {get; set;} = 0;
        public DateTime expire_date { get; set; }
        public int? member_id {get; set;} = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("member_id")]
        public Member? member {get; set;}
    }
}

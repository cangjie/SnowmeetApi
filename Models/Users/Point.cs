using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("user_point_balance")]
    public class Point
    {
        [Key]
        public int id { get; set; }
        //public string user_open_id { get; set; }
        public int member_id { get; set; }
        public int points { get; set; }
        public string memo { get; set; }
        public DateTime transact_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("member_id")]
        public Member? member { get; set; }
    }
}

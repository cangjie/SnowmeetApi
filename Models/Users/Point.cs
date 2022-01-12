using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Users
{
    [Table("user_point_balance")]
    public class Point
    {
        [Key]
        public int id { get; set; }
        public string user_open_id { get; set; }
        public int points { get; set; }
        public string memo { get; set; }
        public DateTime transact_date { get; set; }
    }
}

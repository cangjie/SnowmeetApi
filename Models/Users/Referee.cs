using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.Users
{
    [Table("referee")]
    public class Referee
    {
        [Key]
        public int id {get; set;}
        public int member_id {get; set;}
        public string consume_type {get; set;}
        public int order_id {get; set;}
        public int biz_id {get; set;}
        public int staff_member_id {get; set;}
    }
}
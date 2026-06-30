using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    // 会员自定义标签（系统标签按参与业务派生、不入库）
    [Table("member_tag")]
    public class MemberTag
    {
        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        public string tag { get; set; }
        public int? staff_id { get; set; }         // 打标签的店员
        public bool valid { get; set; } = true;
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

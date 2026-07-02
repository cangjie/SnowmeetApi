using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    // 会员标签库（可后台维护的预设标签字典；区别于 member_tag = 某会员实际打的标签）
    [Table("member_tag_preset")]
    public class MemberTagPreset
    {
        [Key]
        public int id { get; set; }
        public string tag { get; set; }
        public string? group_name { get; set; }   // 分组（客户价值/服务关系/偏好…），可空
        public int sort { get; set; } = 0;
        public bool valid { get; set; } = true;
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 会员的昵称和头像：顾客在小程序验证手机号之后授权，记在 member 表的三个可空列上
    ///   nick_name               昵称（服务端最多存 32 个字，列宽 64）
    ///   avatar                  头像：站内相对路径 /upload/yyyyMMdd/avatar_xxx.ext（文件在 S3，显示时拼 https://img.snowmeet.top）
    ///   nick_avatar_update_date 昵称、头像最近一次更新的时间；以后「超过一定时间重新获取」就看它（规则入口 MemberProfileRules.NeedPrompt）
    ///
    /// 这三列只出现在这个实体里，Member 实体不映射它们（两个实体共用 member 表，主键都是 id）——和养护首单
    /// （MemberCareFirstOrder）同一做法。这样：
    ///   · 代码里好几处整行保存会员（Entry(member).State = Modified）只写 Member 自己的列，不会拿旧副本覆盖昵称头像；
    ///   · 会员的任何查询都不带这三列——代码先于加列脚本上线时，只是「完善资料」两个新接口报错，别的功能不受影响。
    /// 线上的列由 snowmeet_ai_doc/sql/2026-11-14_member_nick_avatar.sql 加；这里的列名、长度必须和脚本一一对应
    /// （MemberProfileSchemaTests 会核对）。写入只走 MemberProfileController（直接 UPDATE 这三列，不走 SaveChanges）。
    /// </summary>
    [Table("member")]
    public class MemberProfile
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int member_id { get; set; }
        [Column("nick_name")]
        [MaxLength(64)]
        public string? nick_name { get; set; }
        [Column("avatar")]
        [MaxLength(255)]
        public string? avatar { get; set; }
        [Column("nick_avatar_update_date")]
        public DateTime? nick_avatar_update_date { get; set; }
    }
}

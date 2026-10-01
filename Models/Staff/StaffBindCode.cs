using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    // 员工账号管理的一次性码（2026-10-01）：
    //   private   管理员生成，员工用私人手机的微信扫码，把这套手机号 + 微信关联到 staff_id
    //   job_phone 管理员生成，用工作手机上的微信扫码，给 social_account_id 这部工作手机补上微信
    //   selfreg   员工自助登记的待开通记录（不对外扫码），开通时 used_date、拒绝时 cancel_date
    [Table("staff_bind_code")]
    public class StaffBindCode
    {
        public const string PURPOSE_PRIVATE = "private";
        public const string PURPOSE_JOB_PHONE = "job_phone";
        public const string PURPOSE_SELFREG = "selfreg";

        [Key]
        public int id { get; set; }
        public string token { get; set; } = "";
        public string purpose { get; set; } = "";
        public int? staff_id { get; set; } = null;
        public int? social_account_id { get; set; } = null;
        public int created_by_staff_id { get; set; } = 0;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime expire_date { get; set; }
        public DateTime? used_date { get; set; } = null;
        public DateTime? cancel_date { get; set; } = null;
    }
}

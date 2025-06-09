using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("scan_qrcode")]
    public class ScanQrCode
    {
        [Key]
        public int id { get; set; }
        public string code { get; set; }
        public int? staff_id { get; set; }
        public string platform { get; set; }
        public string scene { get; set; }
        public string purpose { get; set; }
        public string? scaner_openid { get; set; } = null;
        public string? scaner_unionid { get; set; } = null;
        public int? scaner_member_id { get; set; } = null;
        public int scaned { get; set; } = 0;
        public int stoped { get; set; } = 0;
        public string? cell { get; set; } = null;
        public int authed { get; set; } = 0;
        public int? auth_staff_id { get; set; }
        public DateTime? expire_time { get; set; }
        public DateTime? scan_time { get; set; } = null;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [NotMapped]
        public Member? member { get; set; } = null;
        public class AuthCell
        {
            public int id { get; set; }
            public string cell { get; set; }
            public DateTime submitTime { get; set; }
            public Member? member { get; set; }
            public bool authed { get; set; }
            public Staff? submitStaff { get; set; }
        }
    }
}
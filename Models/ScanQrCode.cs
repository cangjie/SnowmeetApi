using System;
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
        public string? scaner_member_id { get; set; } = null;
        public int scaned { get; set; } = 0;
        public DateTime? scan_time { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("staff_social_account")]
    public class StaffSocialAccount
    {
        [Key]
        public int id { get; set; }
        public int staff_id {get; set;} = 0;
        public int social_account_id { get; set; } = 0;
        public DateTime start_date { get; set; } = DateTime.Now;
        public DateTime? end_date { get; set; } = null;
        public int valid { get; set; } = 0;
        public string season_memo { get; set; } = "";
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("staff_id")]
        public Staff staff { get; set; } = null;
        [ForeignKey("social_account_id")]
        public SocialAccountForJob jobMobile { get; set; } = null;
    }
}
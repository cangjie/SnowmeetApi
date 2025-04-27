using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace SnowmeetApi.Models
{
    [Table("social_account_for_job")]
    public class SocialAccountForJob
    {
        [Key]
        public int id { get; set; }
        public string cell {get; set;} = "";
        public string wechat_mini_openid { get; set; } = "";    
        public int member_id { get; set; } = 0;
        public int is_private { get; set; } = 0;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public List<StaffSocialAccount>? staffSocialAccounts { get; set; }
        [ForeignKey("member_id")]
        public Member? member {get; set;}
        public Staff GetStaff(DateTime date)
        {
            Staff staff = null;
            staffSocialAccounts = staffSocialAccounts.OrderByDescending(s => s.start_date).ToList();
            for(int i = 0; i < staffSocialAccounts.Count; i++)
            {
                if (staffSocialAccounts[i].valid == 1 
                    && staffSocialAccounts[i].start_date.Date <= date.Date
                    && (staffSocialAccounts[i].end_date == null || staffSocialAccounts[i].end_date >= date.Date))
                {
                    staff = staffSocialAccounts[i].staff;
                    break;
                }
            }
            return staff;
        }
    }
}
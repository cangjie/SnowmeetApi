using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Org.BouncyCastle.Tsp;
using SnowmeetApi.Models.Deposit;
namespace SnowmeetApi.Models
{
    [Table("member")]
    public class Member
    {
        [Key]
        public int id { get; set; }
        public string real_name { get; set; } = "";
        public string gender { get; set; } = "";
        public int is_merge { get; set; } = 0;
        public int? merge_id { get; set; }
        public string source { get; set; } = "";
        public int in_staff_list {get; set;} = 0;
        [NotMapped]
        public string title
        {
            get
            {
                string title = real_name + " ";
                title += gender.Trim().Equals("男") ? "先生" : (gender.Trim().Equals("女") ? "女士" : "");
                return title.Trim();
            }
        }  
        public List<MemberSocialAccount> memberSocialAccounts { get; set; } = new List<MemberSocialAccount>();
        public List<DepositAccount> depositAccounts { get; set; } = new List<DepositAccount>();
        public List<Point> points { get; set; } = new List<Point>();
        public List<MemberSocialAccount> GetInfo(string type)
        {
            List<MemberSocialAccount> msaList = new List<MemberSocialAccount>();
            foreach (MemberSocialAccount msa in memberSocialAccounts)
            {
                if (msa.valid == 1 && msa.type.Trim().Equals(type.Trim()))
                {
                    msaList.Add(msa);
                }
            }
            return msaList;
        }

        public string? wechatMiniOpenId
        {
            get
            {
                string? v = null;
                List<MemberSocialAccount> msaList = GetInfo("wechat_mini_openid");
                if (msaList != null && msaList.Count > 0)
                {
                    v = msaList[0].num.Trim();
                }
                return v;
            }
        }
        public string? wechatUnionId
        {
            get
            {
                string? v = null;
                List<MemberSocialAccount> msaList = GetInfo("wechat_unionid");
                if (msaList != null && msaList.Count > 0)
                {
                    v = msaList[0].num.Trim();
                }
                return v;
            }
        }
        public string? cell
        {
            get
            {
                string? v = null;
                List<MemberSocialAccount> msaList = GetInfo("cell");
                for (int i = 0; i < msaList.Count; i++)
                {
                    if (!msaList[i].num.Trim().Equals(""))
                    {
                        v = msaList[i].num.Trim();
                        break;
                    }
                }
                return v;
            }
        }
        public string? wechatId
        {
            get
            {
                string? v = null;
                List<MemberSocialAccount> msaList = GetInfo("wechat_id");
                if (msaList != null && msaList.Count > 0)
                {
                    v = msaList[0].num.Trim();
                }
                return v;
            }
        }
        public List<SocialAccountForJob>? jobAccounts {get; set;}

        //will be deleted
        public int is_staff { get; set; } = 0;
        public int is_manager { get; set; } = 0;
        public int is_admin { get; set; } = 0;
        public List<OrderOnline> orders { get; set; } = new List<OrderOnline>();
        [NotMapped]
        public SnowmeetApi.Models.Users.MiniAppUser miniAppUser {get; set;} = null;
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Org.BouncyCastle.Tsp;
using SnowmeetApi.Models.Deposit;
namespace SnowmeetApi.Models.Users
{
    [Table("member")]
    public class Member
    {
        [Key]
        public int id { get; set; }
        public string real_name { get; set; }
        public string gender { get; set; }
        public int is_merge {get; set; } = 0;
        public int? merge_id {get; set;}
        public string source {get; set; }

        public int is_staff { get; set; }
        public int is_manager { get; set;}
        public int is_admin { get; set; }
        public int in_staff_list {get; set;}
        public string title
        {
            get
            {
                string title = real_name + " ";
                title += gender.Trim().Equals("男")? "先生" : (gender.Trim().Equals("女")? "女士" : "");
                return title.Trim();
            }
        }

        public MiniAppUser miniAppUser
        {
            get
            {
                MiniAppUser miniUser = new MiniAppUser()
                {
                    open_id = wechatMiniOpenId,
                    cell_number = cell,
                    real_name = real_name
                };
                return miniUser;
            }
        }

       

        
        [ForeignKey("member_id")]
        public List<MemberSocialAccount> memberSocialAccounts { get; set; } = new List<MemberSocialAccount>();

        public List<MemberSocialAccount> GetInfo(string type)
        {
            List<MemberSocialAccount> msaList = new List<MemberSocialAccount>();
            foreach(MemberSocialAccount msa in memberSocialAccounts)
            {
                if (msa.valid==1 && msa.type.Trim().Equals(type.Trim()))
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
                if (msaList != null && msaList.Count > 0)
                {
                    v = msaList[0].num.Trim();
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
        [ForeignKey("member_id")]
        public List<DepositAccount> depositAccounts {get; set;}
        [NotMapped]
        public List<OrderOnline> orders {get; set;}
        

    }
}
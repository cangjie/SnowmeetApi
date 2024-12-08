﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.HttpResults;


namespace SnowmeetApi.Models.Users
{

    public class UnicUser
    {
        public static Data.ApplicationDBContext _context;

        public UnicUser()
        {
        }
        /*
        public string name = "";
        public string nick = "";
        public string cellNumber = "";
        public string gender = "";
        public string headImage = "";
        */
        public string officialAccountOpenIdOld = "";
        public string officialAccountOpenId = "";
        public string miniAppOpenId = "";
        public string unionId = "";
        /*
        public int height = 0;
        public int weight = 0;
        public int footLength = 0;
        public DateTime birth = DateTime.MinValue;
        */
        public OfficialAccoutUser officialAccountUser;
        public MiniAppUser miniAppUser;
        //public bool isAdmin = false;
        //public bool isSchoolStaff = false;

        public bool isAdmin
        {
            get
            {
                if (officialAccountUser != null && officialAccountUser.is_admin == 1)
                {
                    return true;
                }
                if (miniAppUser != null && miniAppUser.is_admin == 1)
                {
                    return true;
                }
                return false;
            }
        }

        public static async Task<UnicUser> GetUnicUser(string openId, string type, Data.ApplicationDBContext _context)
        {
            var unionIds = await _context.UnionIds.FromSqlRaw(" select * from unionids where open_id = '" + openId.Trim().Replace("'", "") + "' "
                + " and source = '" + type.Replace("'", "") + "'").ToListAsync();
            if (unionIds.Count > 0)
            {
                string unionId = unionIds[0].union_id;
                var allIds = await _context.UnionIds.Where(u => u.union_id.Trim().Equals(unionId)).ToListAsync();
                string officialAccountOpenId = "";
                string officialAccountOpenIdOld = "";
                string miniAppOpenId = "";
                for (int i = 0; i < allIds.Count; i++)
                {
                    switch (allIds[i].source.Trim())
                    {
                        case "snowmeet_mini":
                            miniAppOpenId = allIds[i].open_id.Trim();
                            break;
                        case "snowmeet_official_account":
                            officialAccountOpenIdOld = allIds[i].open_id.Trim();
                            break;
                        case "snowmeet_official_account_new":
                            officialAccountOpenId = allIds[i].open_id.Trim();
                            break;
                        default:
                            break;
                    }
                }
                UnicUser user = new UnicUser()
                {
                    miniAppOpenId = miniAppOpenId.Trim(),
                    officialAccountOpenId = officialAccountOpenId.Trim(),
                    officialAccountOpenIdOld = officialAccountOpenIdOld.Trim()
                };
                return user;
            }
            else if (type.Trim().Equals("snowmeet_official_account_new"))
            {
                OfficialAccoutUser oaUser = await _context.officialAccoutUsers.FindAsync(openId.Trim());
                if (oaUser == null)
                {
                    return null;
                }
                var l = await _context.MiniAppUsers
                    .Where(m => m.union_id.Trim().Equals(oaUser.union_id.Trim()))
                    .ToListAsync();
                if (l == null || l.Count == 0)
                {
                    return null;
                }
                else
                {
                    MiniAppUser maUser = l[0];
                    UnicUser user = new UnicUser()
                    {
                        miniAppOpenId = maUser.open_id.Trim(),
                        officialAccountOpenId = oaUser.open_id.Trim(),
                        officialAccountOpenIdOld = ""
                    };
                    return user;

                }
            }
            else
            {
                return null;
            }
        }

        public static async Task<ActionResult<UnicUser>> GetUnicUserAsync(Data.ApplicationDBContext db, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            UnicUser user = new UnicUser();
            string miniAppOpenId = "";
            string officialOpenId = "";
            string unionId = "";
            List<MiniSession> mList = await db.MiniSessons.Where(s => s.session_key.Trim().Equals(sessionKey.Trim())
                && s.session_type.Trim().Equals("wl_wechat_mini_openid")).AsNoTracking().ToListAsync();
            if (mList == null || mList.Count <= 0)
            {
                return user;
            }
            miniAppOpenId = mList[0].open_id.Trim();
            List<MemberSocialAccount> msaList = await db.memberSocialAccount.Where(m => m.member_id == mList[0].member_id)
                .AsNoTracking().ToListAsync();
            foreach(MemberSocialAccount msa in msaList)
            {
                switch(msa.type)
                {
                    case "wl_wechat_mini_openid":
                        miniAppOpenId = msa.num.Trim();
                        break;
                    case "wechat_offical_openid":
                        officialOpenId = msa.num.Trim();
                        break;
                    case "wechat_unionid":
                        unionId = msa.num.Trim();
                        break;
                    default:
                        break;
                }
            }
            user.miniAppOpenId = miniAppOpenId;
            user.officialAccountOpenId = officialOpenId;
            user.unionId = unionId;
            return user;
                
        }


        public static async Task<ActionResult<UnicUser>> GetUnicUserAsync(string sessionKey, Data.ApplicationDBContext db)
        {
            UnicUser user = new UnicUser();
            string miniAppOpenId = "";
            string officialOpenId = "";
            string unionId = "";
            List<MiniSession> mList = await db.MiniSessons.Where(s => s.session_key.Trim().Equals(sessionKey.Trim())
                && s.session_type.Trim().Equals("wl_wechat_mini_openid")).AsNoTracking().ToListAsync();
            if (mList == null || mList.Count <= 0)
            {
                return user;
            }
            miniAppOpenId = mList[0].open_id.Trim();
            List<MemberSocialAccount> msaList = await db.memberSocialAccount.Where(m => m.member_id == mList[0].member_id)
                .AsNoTracking().ToListAsync();
            foreach(MemberSocialAccount msa in msaList)
            {
                switch(msa.type)
                {
                    case "wl_wechat_mini_openid":
                        miniAppOpenId = msa.num.Trim();
                        break;
                    case "wechat_offical_openid":
                        officialOpenId = msa.num.Trim();
                        break;
                    case "wechat_unionid":
                        unionId = msa.num.Trim();
                        break;
                    default:
                        break;
                }
            }
            user.miniAppOpenId = miniAppOpenId;
            user.officialAccountOpenId = officialOpenId;
            user.unionId = unionId;
            return user;
                
        }

        public static async Task<ActionResult<UnicUser>> GetUnicUserAsyncBak(string sessionKey, Data.ApplicationDBContext db)
        {
            UnicUser user = new UnicUser();
            string miniAppOpenId = "";
            string officialOpenId = "";
            string unionId = "";

            MiniSession miniSession = db.MiniSessons.Find(sessionKey);
            if (miniSession != null && miniSession.session_type.Trim().Equals("wl_wechat_mini_openid"))
            {
                //miniAppOpenId = miniSession.open_id.Trim();
                user.miniAppOpenId = miniSession.open_id.Trim();

                //var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                //    + miniAppOpenId.Trim() + "' and source = 'snowmeet_mini' ").ToList();
                /*
                var unionIds = await db.UnionIds.Where(u => (u.open_id.Trim().Equals(miniAppOpenId.Trim()) && u.source.Trim().Equals("snowmeet_mini"))).AsNoTracking().ToListAsync();

                if (unionIds.Count > 0)
                {
                    unionId = unionIds[0].union_id.Trim();
                    //unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where union_id = '"
                    //    + unionId.Trim() + "' and source = 'snowmeet_official_account' ").ToList();
                    unionIds = await db.UnionIds.Where(u => (u.union_id.Trim().Equals(unionId.Trim()) && u.source.Trim().Equals("snowmeet_official_account"))).AsNoTracking().ToListAsync();
                    if (unionIds.Count > 0)
                    {
                        officialOpenId = unionIds[0].open_id.Trim();
                    }
                }
                */
            }
            /*
            if (miniAppOpenId.Trim().Equals(""))
            {
                MToken mToken = db.MTokens.Find(sessionKey);
                if (mToken != null && mToken.isvalid == 1 && mToken.expire > DateTime.Now)
                {
                    officialOpenId = mToken.open_id.Trim();
                    //var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                    //    + officialOpenId.Trim() + "' and source = 'snowmeet_official_account' ").ToList();

                    var unionIds = await db.UnionIds.Where(u => (u.open_id.Trim().Equals(officialOpenId.Trim()) && u.source.Trim().Equals("snowmeet_official_account"))).AsNoTracking().ToListAsync();

                    if (unionIds.Count > 0)
                    {
                        unionId = unionIds[0].union_id.Trim();
                        //unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where union_id = '"
                        //    + unionId.Trim() + "' and source = 'snowmeet_mini' ").ToList();
                        unionIds = await db.UnionIds.Where(u => (u.union_id.Trim().Equals(unionId.Trim()) && u.source.Trim().Equals("snowmeet_mini"))).AsNoTracking().ToListAsync();
                        if (unionIds.Count > 0)
                        {
                            miniAppOpenId = unionIds[0].open_id.Trim();
                        }
                    }
                }
            }
            */
            //user.unionId = unionId.Trim();
            if (miniAppOpenId.Trim().Equals("") && officialOpenId.Trim().Equals(""))
            {
                if (sessionKey.Equals("SystemInvoke"))
                {
                    return new UnicUser()
                    {
                        miniAppOpenId = "system",
                        miniAppUser = new MiniAppUser()
                        {
                            open_id = "system",
                            is_admin = 1,
                            is_manager = 1
                        }
                    };
                }
                else
                {
                    return null;
                }
            }
            if (!miniAppOpenId.Trim().Equals(""))
            {
                user.miniAppOpenId = miniAppOpenId.Trim();
                user.miniAppUser = db.MiniAppUsers.Find(miniAppOpenId.Trim());

            }
            if (!officialOpenId.Trim().Equals(""))
            {
                user.officialAccountOpenId = officialOpenId.Trim();
                user.officialAccountUser = db.officialAccoutUsers.Find(officialOpenId.Trim());

            }
            return user;
        }

        public static UnicUser GetUnicUser(string sessionKey)
        {
            UnicUser user = new UnicUser();
            string miniAppOpenId = "";
            string officialOpenId = "";
            string unionId = "";
            
            MiniSession miniSession = _context.MiniSessons.Find(sessionKey);
            if (miniSession != null)
            {
                miniAppOpenId = miniSession.open_id.Trim();
                user.miniAppOpenId = miniAppOpenId;

                //var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                //    + miniAppOpenId.Trim() + "' and source = 'snowmeet_mini' ").ToList();

                var unionIds =  _context.UnionIds.Where(u => (u.open_id.Trim().Equals(miniAppOpenId.Trim()) && u.source.Trim().Equals("snowmeet_mini"))).ToList();

                if (unionIds.Count > 0)
                {
                    unionId = unionIds[0].union_id.Trim();
                    //unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where union_id = '"
                    //    + unionId.Trim() + "' and source = 'snowmeet_official_account' ").ToList();
                    unionIds = _context.UnionIds.Where(u => (u.union_id.Trim().Equals(unionId.Trim()) && u.source.Trim().Equals("snowmeet_official_account"))).ToList();
                    if (unionIds.Count > 0)
                    {
                        officialOpenId = unionIds[0].open_id.Trim();
                    }
                }

            }
            if (miniAppOpenId.Trim().Equals(""))
            {
                MToken mToken = _context.MTokens.Find(sessionKey);
                if (mToken != null && mToken.isvalid == 1 && mToken.expire > DateTime.Now)
                {
                    officialOpenId = mToken.open_id.Trim();
                    //var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                    //    + officialOpenId.Trim() + "' and source = 'snowmeet_official_account' ").ToList();

                    var unionIds = _context.UnionIds.Where(u => (u.open_id.Trim().Equals(officialOpenId.Trim()) && u.source.Trim().Equals("snowmeet_official_account"))).ToList();

                    if (unionIds.Count > 0)
                    {
                        unionId = unionIds[0].union_id.Trim();
                        //unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where union_id = '"
                        //    + unionId.Trim() + "' and source = 'snowmeet_mini' ").ToList();
                        unionIds = _context.UnionIds.Where(u => (u.union_id.Trim().Equals(unionId.Trim()) && u.source.Trim().Equals("snowmeet_mini"))).ToList();
                        if (unionIds.Count > 0)
                        {
                            miniAppOpenId = unionIds[0].open_id.Trim();
                        }
                    }
                }
            }
            user.unionId = unionId.Trim();
            if (miniAppOpenId.Trim().Equals("") && officialOpenId.Trim().Equals(""))
            {
                if (sessionKey.Equals("SystemInvoke"))
                {
                    return new UnicUser()
                    {
                        miniAppUser = new MiniAppUser()
                        {
                            open_id = "system",
                            is_admin = 1
                        }
                    };
                }
                else
                {
                    return null;
                }
            }
            if (!miniAppOpenId.Trim().Equals(""))
            {
                user.miniAppOpenId = miniAppOpenId.Trim();
                user.miniAppUser = _context.MiniAppUsers.Find(miniAppOpenId.Trim());
                
            }
            if (!officialOpenId.Trim().Equals(""))
            {
                user.officialAccountOpenId = officialOpenId.Trim();
                user.officialAccountUser = _context.officialAccoutUsers.Find(officialOpenId.Trim());
                
            }
            return user;
        }
    }
}

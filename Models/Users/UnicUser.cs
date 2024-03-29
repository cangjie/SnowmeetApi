﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;


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

        public static UnicUser GetUnicUser(string openId, string type)
        {
            var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where open_id = '" + openId.Trim().Replace("'", "") + "' "
                + " and source = '" + type.Replace("'", "") + "'").ToList();
            if (unionIds.Count > 0)
            {
                string unionId = unionIds[0].union_id;
                var allIds = _context.UnionIds.Where(u => u.union_id.Trim().Equals(unionId)).ToList();
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
            else
            {
                return null;
            }
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
                
                var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                    + miniAppOpenId.Trim() + "' and source = 'snowmeet_mini' ").ToList();
                if (unionIds.Count > 0)
                {
                    unionId = unionIds[0].union_id.Trim();
                    unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where union_id = '"
                        + unionId.Trim() + "' and source = 'snowmeet_official_account' ").ToList();
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
                    var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                        + officialOpenId.Trim() + "' and source = 'snowmeet_official_account' ").ToList();
                    if (unionIds.Count > 0)
                    {
                        unionId = unionIds[0].union_id.Trim();
                        unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where union_id = '"
                            + unionId.Trim() + "' and source = 'snowmeet_mini' ").ToList();
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
                return null;
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

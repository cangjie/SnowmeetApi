using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NuGet.Packaging.Signing.DerEncoding;


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

        public int memberId = 0;

        public string cell = "";

        public string wlMiniOpenId = "";

        public Member member;
        
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
                /*
                
                if (officialAccountUser != null && officialAccountUser.is_admin == 1)
                {
                    return true;
                }
                if (miniAppUser != null && miniAppUser.is_admin == 1)
                {
                    return true;
                }
                return false;
                */
                if (member.is_admin == 1 || member.is_manager == 1 || member.is_staff == 1)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

    
        public static async Task<UnicUser> GetUnicUser(int memberId)
        {
            if (memberId == 0)
            {
                return null;
            }
            var memberList = await _context.member.Where(m => m.id == memberId).Include(m => m.memberSocialAccounts)
                .AsNoTracking().ToListAsync();
            if (memberList == null || memberList.Count == 0)
            {
                return null;
            }
            UnicUser user = new UnicUser();
            user.member = memberList[0];
            foreach(MemberSocialAccount msa in memberList[0].memberSocialAccounts)
            {
                switch(msa.type.Trim())
                {
                    case "wechat_mini_openid":
                        user.miniAppOpenId = msa.num.Trim();
                        user.miniAppUser = await  _context.MiniAppUsers.FindAsync(msa.num.Trim());
                        break;
                    case "wechat_oa_openid":
                        user.officialAccountOpenId = msa.num.Trim();
                        user.officialAccountUser = await _context.officialAccoutUsers.FindAsync(msa.num.Trim());
                        break;
                    case "wechat_unionid":
                        user.unionId = msa.num.Trim();
                        break;
                    case "cell":
                        user.cell = msa.num.Trim();
                        break;
                    case "wl_wechat_mini_openid":
                        user.wlMiniOpenId = msa.num.Trim();
                        break;
                    default:
                        break;
                }
            }
            return user;
            
        }


        public static async Task<UnicUser> GetUnicUser(string openId, string type, Data.ApplicationDBContext _context)
        {
            _context = _context;
            switch(type)
            {
                case "snowmeet_mini":
                    type = "wechat_mini_openid";
                    break;
                case "snowmeet_official_account_new":
                    type = "wechat_oa_openid";
                    break;
                default:
                    type = "";
                    break;

            }
            if (type.Trim().Equals(""))
            {
                return null;
            }
            var msaList = await _context.memberSocialAccount.Where(m => (m.num.Trim().Equals(openId.Trim()) && m.type.Trim().Equals(type)))
                .AsNoTracking().ToListAsync();
            if (msaList == null || msaList.Count == 0)
            {
                return null;
            }
            if (msaList[0].member_id == 0)
            {
                return null;
            }
            return await GetUnicUser(msaList[0].member_id);
            /*
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
            */
        }

        public static async Task<ActionResult<UnicUser>> GetUnicUserAsync(string sessionKey, Data.ApplicationDBContext db)
        {
            _context = db;

            return await GetUnicUser(sessionKey, "wechat_mini_openid");
            /*

            UnicUser user = new UnicUser();
            string miniAppOpenId = "";
            string officialOpenId = "";
            string unionId = "";

            MiniSession miniSession = db.MiniSessons.Find(sessionKey);
            if (miniSession != null)
            {
                miniAppOpenId = miniSession.open_id.Trim();
                user.miniAppOpenId = miniAppOpenId;

                //var unionIds = _context.UnionIds.FromSqlRaw(" select * from unionids where  open_id = '"
                //    + miniAppOpenId.Trim() + "' and source = 'snowmeet_mini' ").ToList();

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

            }
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
            user.unionId = unionId.Trim();
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
            */
        }

        public static async Task<UnicUser> GetUnicUser(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            var sL = await _context.MiniSessons.Where(s => s.session_key.Trim().Equals(sessionKey.Trim())
                && s.session_type.Trim().Equals(sessionType)).OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            if (sL == null || sL.Count == 0)
            {
                return null;
            }
            int memberId = ((sL[0].member_id == null)? 0 : (int)sL[0].member_id);
            return await GetUnicUser(memberId);
        }

        

/*
        public static UnicUser GetUnicUser_bak(string sessionKey)
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
        */
    }
}

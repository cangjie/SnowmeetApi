using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
//using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.User
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MemberController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        

        public MemberController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpGet]
        public async Task<ActionResult<Member>> GetMemberInfoSimple(string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await GetMemberBySessionKey(sessionKey.Trim(), sessionType.Trim());
            //member.id = 0;
            //member.memberSocialAccounts = new List<MemberSocialAccount>();
            return Ok(RemoveSensitiveInfo(member));
        }


        [NonAction]
        public async Task<Member> GetMemberBySessionKey(string sessionKey, string sessionType="wechat_mini_openid")
        {
            var sessions = await _db.MiniSessons.Where(s => s.session_key.Trim().Equals(sessionKey.Trim()) 
                && s.session_type.Trim().Equals(sessionType.Trim())).OrderByDescending(s => s.create_date)
                .AsNoTracking().ToListAsync();
            if (sessions.Count <= 0)
            {
                return null;
            }
            int memberId = sessions[0].member_id != null ? (int)sessions[0].member_id:0;
            string openId = sessions[0].open_id.Trim();
            if (memberId == 0)
            {
                return await GetMember(openId, sessionType);
            }
            else
            {
                return await _db.member.Include(m => m.memberSocialAccounts)
                    .Where(m => m.id == memberId).FirstAsync();
            }
        }


       
        [NonAction]
        public async Task<Member> GetMember(string num, string type="")
        {
            
            type = type.Trim();
            int memberId = 0;
  
            var msaList = await _db.memberSocialAccount
                        .Where(a => (a.valid == 1 && a.num.Trim().Equals(num) && a.type.Trim().Equals(type)))
                        .OrderByDescending(a => a.id).ToListAsync();
            if (msaList.Count == 0)
            {
                return null;
            }
            memberId = msaList[0].member_id;
            if (memberId == 0)
            {
                return null;
            }
            Member member = await _db.member.Include(m => m.memberSocialAccounts)
                .Where(m => m.id == memberId).FirstAsync();
            return member;
        }

        [NonAction]
        public async Task<Member> CreateMember(Member member)
        {
            if (member.memberSocialAccounts.Count == 0)
            {
                return null;
            }
            await _db.member.AddAsync(member);
            await _db.SaveChangesAsync();
            return member;
        }
        

        private bool MemberExists(int id)
        {
            return _db.member.Any(e => e.id == id);
        }

        [NonAction]
        public Member RemoveSensitiveInfo(Member member)
        {
            if (member == null)
            {
                return member;
            }
            member.id = 0;
            IList<MemberSocialAccount> msaList = member.memberSocialAccounts.ToList();

            for(int i = 0; i < msaList.Count; i++)
            {
                MemberSocialAccount msa = msaList[i];
                msa.member_id = 0;
                if (msa.type.Trim().IndexOf("openid") >= 0)
                {
                    msaList.Remove(msa);
                    i--;
                }
                if (msa.type.Trim().IndexOf("unionid") >= 0)
                {
                    msaList.Remove(msa);
                    i--;
                }
            }
            member.memberSocialAccounts = msaList;
            return member;
        }

    }
}

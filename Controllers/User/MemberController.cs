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

        [HttpGet]
        public async Task<ActionResult<Member>> RegStaff(string name, string gender, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            name = Util.UrlDecode(name);
            gender = Util.UrlDecode(gender);

            Member member = await GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            member = await _db.member.FindAsync(member.id);
            member.real_name = name;
            member.gender = gender;
            member.in_staff_list = 1;
            _db.member.Entry(member).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return RemoveSensitiveInfo(member);
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult> SetStaffInfo(int memberId,  
            string name, string gender, string cell,
            int isAdmin, int isManager, int isStaff, int inStaffList,
            string sessionKey, string sessionType="wechat_mini_openid")
        {
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0)
            {
                return BadRequest();
            }
            Member staff = await _db.member.FindAsync(memberId);
            if (staff == null)
            {
                return NotFound();
            }
            staff.is_admin = isAdmin;
            staff.is_staff = isStaff;
            staff.is_manager = isManager;
            staff.in_staff_list = inStaffList;
            staff.real_name = name.Trim();
            staff.gender = gender;
            await ModMemberCell(memberId, cell.Trim());
            _db.member.Entry(staff).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult<Member>> GetWholeMemberInfo(int memberId, 
            string sessionKey, string sessionType="wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0 && admin.is_staff == 0 && admin.is_manager == 0)
            {
                return BadRequest();
            }
            var memberList = await _db.member.Where(m => m.id == memberId)
                .Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
            if (memberList == null || memberList.Count == 0)
            {
                return NotFound();
            }
            memberList = GetCells(memberList);
            return Ok(memberList[0]);
        }

        [HttpGet]
        public async Task<ActionResult<List<Member>>> GetStaffList(string sessionKey, 
            string sessionType="wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0)
            {
                return BadRequest();
            }
            var memberList = await _db.member.Where(m => (m.in_staff_list == 1))
                .Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
            memberList = GetCells(memberList);
            return Ok(memberList);
            
        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<Member>> GetMemberByCell(string cell, 
            string sessionKey, string sessionType="wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0 && admin.is_manager == 0 && admin.is_staff == 0)
            {
                return BadRequest();
            }
            return Ok(await GetMember(cell, "cell"));
        }

        [NonAction]
        public List<Member> GetCells(List<Member> memberList)
        {
            for(int i = 0; i < memberList.Count; i++)
            {
                Member member = memberList[i];
                foreach(MemberSocialAccount msa in member.memberSocialAccounts)
                {
                    if (msa.type.Trim().Equals("cell"))
                    {
                        member.cell = msa.num.Trim();
                        break;
                    }
                }
            }
            return memberList;
        }

        [NonAction]
        public async Task ModMemberCell(int memberId, string cell)
        {
            var list = await _db.memberSocialAccount
                .Where(m => (m.type.Trim().Equals("cell") && m.num.Trim().Equals(cell.Trim()) 
                && m.member_id == memberId))
                .ToListAsync();
            if (list == null || list.Count == 0)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = memberId,
                    type = "cell",
                    num = cell.Trim(),
                    valid = 1
                };
                await _db.memberSocialAccount.AddAsync(msa);
                await _db.SaveChangesAsync();
            }
            else
            {
                MemberSocialAccount msa = list[0];
                msa.valid = 1;
                _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }

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

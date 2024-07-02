using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.User
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MemberController : ControllerBase
    {
        private readonly ApplicationDBContext _db;

        

        public MemberController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;

        }

        [HttpGet]
        public async Task<ActionResult<Member>> GetMemberInfoSimple(string sessionKey, string sessionType)
        {
            Member member = await GetMember(sessionKey.Trim(), sessionType.Trim());
            member.id = 0;
            member.memberSocialAccounts = new List<MemberSocialAccount>();
            return Ok(member);
        }

        [NonAction]
        public async Task<Member> GetMember(string sessionKey, string type)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            type = type.Trim();
            int memberId = 0;
            string num = "";
            switch (type.Trim())
            {
                case "wechat_mini_openid":
                    UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
                    if (user == null)
                    {
                        return null;
                    }
                    num = user.miniAppOpenId.Trim();
                    break;
                default:
                    break;
            }
            if (num.Trim().Equals(""))
            {
                return null;
            }
            MemberSocialAccount msa = await _db.memberSocialAccount
                        .Where(a => (a.valid == 1 && a.num.Trim().Equals(num) && a.type.Trim().Equals(type)))
                        .FirstAsync();

            memberId = msa.member_id;
            if (memberId == 0)
            {
                return null;
            }
            Member member = await _db.member.Include(m => m.memberSocialAccounts)
                .Where(m => m.id == memberId).FirstAsync();
            return member;
        }

        /*

        // GET: api/Member
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Member>>> GetMember()
        {
            return await _context.Member.ToListAsync();
        }

        // GET: api/Member/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Member>> GetMember(int id)
        {
            var member = await _context.Member.FindAsync(id);

            if (member == null)
            {
                return NotFound();
            }

            return member;
        }

        // PUT: api/Member/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMember(int id, Member member)
        {
            if (id != member.id)
            {
                return BadRequest();
            }

            _context.Entry(member).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MemberExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Member
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Member>> PostMember(Member member)
        {
            _context.Member.Add(member);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetMember", new { id = member.id }, member);
        }

        // DELETE: api/Member/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMember(int id)
        {
            var member = await _context.Member.FindAsync(id);
            if (member == null)
            {
                return NotFound();
            }

            _context.Member.Remove(member);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool MemberExists(int id)
        {
            return _db.member.Any(e => e.id == id);
        }
    }
}

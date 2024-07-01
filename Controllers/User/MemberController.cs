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
        public async Task<ActionResult<Member>> GetMember(int id)
        {
            //MemberSocialAccount ma = await _db.memberSocialAccount.Where(m => m.MemberId == id).FirstAsync();
            Member m = await _db.member.Include(m => m.memberSocialAccounts).Where(m => m.id == id).FirstAsync();
            
            return Ok(m);
        }

        [NonAction]
        public async Task<Member> GetMember(string sessionKey, string type)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            switch (type.Trim())
            {
                case "mini":
                    UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
                    if (user != null)
                    { 
                        
                    }
                    break;
                default:
                    break;
            }
            return null;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class StaffController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        public StaffController(ApplicationDBContext context)
        {
            _db = context;
        }
        [NonAction]
        public async Task<Staff> GetStaffBySocialNum(string num, string type, DateTime? date = null)
        {
            if (date == null)
            {
                date = DateTime.Now;
            }
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => m.num == num && m.type == type && m.valid == 1)
                .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
            if (msaList.Count == 0)
            {
                return null;
            }
            List<SocialAccountForJob> jList = await _db.SocialAccountForJob
                .Include(j => j.staffSocialAccounts).ThenInclude(s => s.staff)
                .Where(j => j.member_id == msaList[0].member_id)
                .OrderByDescending(j => j.id).AsNoTracking().ToListAsync();
            if (jList.Count == 0)
            {
                return null;
            }
            return jList[0].GetStaff((DateTime)date);
        }
        //Test API
        
        [HttpGet]
        public async Task<ActionResult<SnowmeetApi.Models.Staff>> GetStaffBySocialNumTest(string num, string type, DateTime? date = null)
        {
            return Ok(await GetStaffBySocialNum(num, type, date));
        }
        
        
    }
}
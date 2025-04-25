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
            List<SocialAccountForJob> jList = await _db.socialAccountForJob
                .Include(j => j.staffSocialAccounts).ThenInclude(s => s.staff)
                .Where(j => j.member_id == msaList[0].member_id)
                .OrderByDescending(j => j.id).AsNoTracking().ToListAsync();
            if (jList.Count == 0)
            {
                return null;
            }
            return jList[0].GetStaff((DateTime)date);
        }
        [NonAction]
        public async Task<Staff> CreateStaff(string openId, string cell, string name, string gender, DateTime bizDate)
        {
            List<SocialAccountForJob> jl = await _db.socialAccountForJob
                .Where(j => j.cell.Trim().Equals(cell) || j.wechat_mini_openid.Trim().Equals(openId))
                .AsNoTracking().ToListAsync();
            if (jl.Count > 0)
            {
                return null;
            }
            SocialAccountForJob j = new SocialAccountForJob()
            {
                id = 0,
                cell = cell,
                wechat_mini_openid = openId,
                member_id = 0,
                is_private = 1,
                create_date = DateTime.Now
            };
            Staff staff = new Staff()
            {
                id = 0,
                name = name,
                gender = gender,
                title_level = 0,
                create_date = DateTime.Now
            };
            DateTime startDate = DateTime.MinValue;
            DateTime endDate = DateTime.MinValue;
            if (bizDate.Month >= 6)
            {
                startDate = new DateTime(bizDate.Year, 6, 1);
                endDate = new DateTime(bizDate.Year + 1, 5, 31);
            }
            else
            {
                startDate = new DateTime(bizDate.Year - 1, 6, 1);
                endDate = new DateTime(bizDate.Year, 5, 31);
            }
            StaffSocialAccount ssa = new StaffSocialAccount()
            {
                id = 0,
                staff = staff,
                staff_id = staff.id,
                social_account_id = j.id,
                jobMobile = j,
                start_date = DateTime.Parse("2000-6-1"),
                end_date = null,
                season_memo = (startDate.Year - 2000).ToString() + "-" + (endDate.Year - 2000).ToString() + "雪季",
                create_date = DateTime.Now
            };
            await _db.socialAccountForJob.AddAsync(j);
            await _db.staff.AddAsync(staff);
            await _db.staffSocialAccount.AddAsync(ssa);
            await _db.SaveChangesAsync();
            return staff;
        }
        //Test API
        
        [HttpGet]
        public async Task<ActionResult<SnowmeetApi.Models.Staff>> GetStaffBySocialNumTest(string num, string type, DateTime? date = null)
        {
            return Ok(await GetStaffBySocialNum(num, type, date));
        }
        [HttpGet]
        public async Task<ActionResult<Staff>> CreateStaffTest(string openId, string cell, string name, string gender, DateTime bizDate)
        {
            return Ok(await CreateStaff(openId, cell, name, gender, bizDate));
            //return null;
        }
        
    }
}
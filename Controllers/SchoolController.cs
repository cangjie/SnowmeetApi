using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.School;
using System.Collections;
using System.Text.RegularExpressions;
using Flurl.Util;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class SchoolController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        //private readonly Order.OrderPaymentController payCtrl;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;

        private readonly User.MemberController _memberHelper;

        public SchoolController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _memberHelper = new User.MemberController(_db, _config);
        }

        [HttpGet]
        public async Task<ActionResult<Staff>> GetStaffInfo(string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            sessionType = Util.UrlDecode(sessionType.Trim());
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            var staffList = await _db.schoolStaff.Where(s => s.member_id == member.id).AsNoTracking().ToListAsync();
            if (staffList.Count <= 0)
            {
                return null;
            }
            else
            {
                return Ok(RemoveSensitiveInfo(staffList[0]));
            }

        }

        [HttpGet]
        public async Task<ActionResult<Staff>> FindTrainer(string key)
        {
            bool isCell = false;
            if (Regex.IsMatch(key, @"1\d{10}"))
            {
                isCell = true;
            }
            var staffList = await _db.schoolStaff.Where(s => (s.member_id != null && s.member_id > 0))
                .AsNoTracking().ToListAsync();
            Staff staff = new Staff();
            bool find = false;
            for(int i = 0; i < staffList.Count; i++)
            {
                if ((staffList[i].temp_filled_cell.ToString().Trim().Equals(key) && isCell)
                    || (staffList[i].temp_filled_name.ToString().Trim().Equals(key) && !isCell))
                {
                    staff = staffList[i];
                    find = true;
                    break;
                }
            }
            var members = await _db.member.Where(m => m.id == staff.member_id)
                .Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
            if (members.Count > 0)
            {
                staff.member = _memberHelper.RemoveSensitiveInfo(members[0]);
                
            }
            
            if (!find)
            {
                return NotFound();
            }
            else
            {
                return Ok(staff);
            }
        }

        [HttpPost]
        public async Task<ActionResult<Course>> NewCourse([FromBody] Course course, 
            [FromQuery]string sessionKey, [FromQuery]string sessionType = "wl_wechat_mini_openid")
        {
            Staff staff = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value ;
            if (staff == null || staff.member_id == null || staff.member_id == 0)
            {
                return BadRequest();
            }
            course.oper_member_id = (int)staff.member_id;
            course.oper_cell = staff.temp_filled_cell.Trim();
            course.oper_name = staff.temp_filled_name.Trim();
            if (course.courseStudents.Count == 0)
            {
                return NoContent();
            }
            await _db.schoolCourse.AddAsync(course);
            await _db.SaveChangesAsync();
            return Ok(course);
        }

        [HttpGet]
        public async Task<ActionResult<List<Course>>> GetCoursesByStudentInfo(string cell, string name)
        {
            name = Util.UrlEncode(name);
            return Ok(await _db.courseStudent.Where(s => s.cell.Trim().Equals(cell.Trim()) 
                && s.name.Trim().Equals(name)).OrderByDescending(s => s.id)
                .AsNoTracking().ToListAsync());
        }

        [NonAction]
        public Staff RemoveSensitiveInfo(Staff staff)
        {
            staff.member_id = 0;
            return staff;
        }


       



    }
}
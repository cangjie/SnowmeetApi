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
            course.course_content = course.course_content == null ? "" : course.course_content.Trim();
            
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
                && s.name.Trim().Equals(name) && s.del == 0).OrderByDescending(s => s.id)
                .AsNoTracking().ToListAsync());
        }

        [HttpGet]
        public async Task<ActionResult<List<Course>>> GetMyCourses(string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            var courses = await GetCourses(DateTime.Parse("2024-10-1"), DateTime.Parse("2100-10-1"), member.id, 0);
            return Ok(courses);
        }

        [HttpGet]
        public async Task<ActionResult<List<Course>>> GetMyFilledCourses(string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            var courses = await GetCourses(DateTime.Parse("2024-10-1"), DateTime.Parse("2100-10-1"), 0, member.id);
            return Ok(courses);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Course>> GetCourse(int id, string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            var courses = await _db.schoolCourse.Where(c => c.id == id).Include(c => c.courseStudents)
                .AsNoTracking().ToListAsync();
            
            if (courses != null && courses.Count > 0)
            {
                Course course = courses[0];
                course.staff = (Staff)((OkObjectResult)(await FindTrainer(course.trainer_cell)).Result).Value;
                course.oper = (Staff)((OkObjectResult)(await FindTrainer(course.oper_cell)).Result).Value;
                return Ok(courses[0]);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteCourse(int id, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
    
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }

            Course c = (Course)((OkObjectResult)(await GetCourse(id, sessionKey, sessionType)).Result).Value;
            if (c.oper_member_id != member.id && c.trainer_member_id != member.id)
            {
                return BadRequest();
            }
            
            for(int i = 0; i < c.courseStudents.Count; i++)
            {
                CourseStudent student = c.courseStudents[i];
                student.update_date = DateTime.Now;
                student.del = 1;
                _db.courseStudent.Entry(student).State = EntityState.Modified;
            }

            c.update_date = DateTime.Now;
            c.del = 1;
            _db.schoolCourse.Entry(c).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok();

        }


        [NonAction]
        public async Task<List<Course>> GetCourses(DateTime start, DateTime end, int trainerId, int operId)
        {
            var courses = await _db.schoolCourse
                .Where(c => (c.course_date.Date >= start.Date && c.course_date.Date <= end.Date
                && (trainerId == 0 || c.trainer_member_id == trainerId)
                && (operId == 0 || c.oper_member_id == operId) && c.del == 0))
                .OrderByDescending(c => c.id)
                .Include(c => c.courseStudents).AsNoTracking().ToListAsync();
            return courses;
        }

        [NonAction]
        public Staff RemoveSensitiveInfo(Staff staff)
        {
            //staff.member_id = 0;
            return staff;
        }


       



    }
}
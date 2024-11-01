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

        public class Student
        {
            public int? member_id {get; set;} = null;
            public string cell {get; set;}
            public string name {get; set;}
            public string gender {get; set;}
            public string adult_type {get; set;}
            public int count {get; set;}

            public DateTime lastCourseTime {get; set;}

            public List<Course> couses {get; set;} = null;

            
        }

        public SchoolController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _memberHelper = new User.MemberController(_db, _config);
        }


        [HttpGet("{isReg}")]
        public async Task<ActionResult<List<Staff>>> GetStaffList(int isReg, string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff me = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            if (me == null)
            {
                return BadRequest();
            }
            string role = me.role.Trim();
            string sub = me.sub_school_name.Trim();
            string team = me.team.Trim();

            var list = await _db.schoolStaff.Where(s => ((s.member_id != null && isReg == 1) || (s.member_id == null && isReg == 0))
                && (role.Trim().Equals("校长") || role.Trim().Equals("客服")
                || (role.Trim().Equals("分校长") && s.sub_school_name.Trim().Equals(sub.Trim()) ) 
                || (role.Trim().Equals("队长") && s.sub_school_name.Trim().Equals(sub.Trim()) && s.team.Trim().Equals(team))  ))
                .AsNoTracking().ToListAsync();
            return Ok(list);
        }

        [HttpGet("{staffId}")]
        public async Task<ActionResult<Staff>> LinkStaffMember(int staffId, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Staff staff = await _db.schoolStaff.FindAsync(staffId);
            if (staff == null || staff.member_id != null)
            {
                return BadRequest();
            }
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            staff.member_id = member.id;
            _db.schoolStaff.Entry(staff).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(staff);

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
        public async Task<ActionResult<Staff>> FindUnRegisteredTrainer(string key)
        {
            bool isCell = false;
            if (Regex.IsMatch(key, @"1\d{10}"))
            {
                isCell = true;
            }
            var staffList = await _db.schoolStaff.Where(s => (((isCell && s.temp_filled_cell.Trim().Equals(key))
                || (!isCell &&  s.temp_filled_name.Trim().IndexOf(key)>= 0)) && (s.member_id == null || s.member_id == 0) ))
                .AsNoTracking().ToListAsync();
            if (staffList == null || staffList.Count == 0)
            {
                return NotFound();
            }
            else
            {
                return Ok(staffList[0]);
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
            key = key.Trim();
            var staffList = await _db.schoolStaff.Where(s => (s.member_id != null && s.member_id > 0 
                && ( (s.temp_filled_cell.Trim().Equals(key.Trim()) && key.Length == 11)) || s.temp_filled_name.Trim().IndexOf(key) >= 0  ))
                .AsNoTracking().ToListAsync();
            Staff staff = new Staff();
            bool find = false;
            if (staffList != null && staffList.Count > 1)
            {
                return NoContent();
            }
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

        [HttpPost]
        public async Task<ActionResult<CourseStudent>> UpdateCourseStudent([FromBody]CourseStudent courseStudent, 
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            var staffList = await _db.schoolStaff.Where(s => s.member_id == member.id).AsNoTracking().ToListAsync();
            if (staffList == null || staffList.Count == 0)
            {
                return BadRequest();
            }
            Staff staff = staffList[0];
            if (courseStudent.course.trainer_member_id != member.id)
            {
                //return BadRequest();
            }
            _db.courseStudent.Entry(courseStudent).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(courseStudent);
        }

        [HttpGet]
        public async Task<ActionResult<List<Student>>> GetMyStudents(string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            var staffList = await _db.schoolStaff.Where(s => s.member_id == member.id).AsNoTracking().ToListAsync();
            if (staffList == null || staffList.Count == 0)
            {
                return NoContent();
            }
            int memberId = member.id;
            if (staffList[0].role.Trim().Equals("校长") && staffList[0].sub_school_name.Trim().Equals(""))
            {
                memberId = 0;
            }
            var courses = await GetCourses(DateTime.Parse("2024-10-1"), DateTime.Parse("2100-10-1"), memberId, 0);
            return Ok(GetStudents(courses));
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

        [HttpGet("{id}")]
        public async Task<ActionResult<CourseStudent>> GetCourseStudent(int id, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            CourseStudent cs = await _db.courseStudent.FindAsync(id);
            if (cs == null)
            {
                return NotFound();
            }
            cs.course = await _db.schoolCourse.FindAsync(cs.course_id);
            cs.course.courseStudents = null;
            var csl = await _db.courseStudent.Where(s => s.course_id == cs.course_id && cs.del == 0).AsNoTracking().ToListAsync();
            cs.course.studentCount = csl.Count;
            cs.course.staff = (Staff)((OkObjectResult)(await FindTrainer(cs.course.trainer_cell)).Result).Value;
            return Ok(cs);
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
        public List<Student> GetStudents(List<Course> courses)
        {
            List<Student> sl = new List<Student>();
            foreach (Course course in courses)
            {
                foreach(CourseStudent courseStudent in course.courseStudents)
                {
                    string name = courseStudent.name.Trim();
                    string cell = courseStudent.cell.Trim();
                    bool exists = false;
                    foreach(Student student in sl)
                    {
                        if (student.name.Trim().Equals(name.Trim())
                            && student.cell.Trim().Equals(cell.Trim()))
                        {
                            exists = true;
                            student.couses.Add(course);

                            student.count = student.couses.Count;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        Student s = new Student()
                        {
                            name = name.Trim(),
                            cell = cell.Trim(),
                            lastCourseTime = course.course_date,
                            count = 1,
                            gender = courseStudent.gender,
                            adult_type = courseStudent.adult_type,
                            couses = (new List<Course>())
                        };
                        s.couses.Add(course);
                        sl.Add(s);
                    }
                }
            }
            for(int i = 0; i < sl.Count; i++)
            {
                for(int j = 0; j < sl[i].couses.Count; j++)
                {
                    sl[i].couses[j].courseStudents = null;
                }
            }
            return sl;
        }

        [HttpGet]
        public async Task<ActionResult<List<Course>>> GetStudentCourses(string cell, string name)
        {
            var courseList = await _db.schoolCourse.FromSqlRaw(" select * from school_course c where del = 0 and exists ( "
                + " select * from school_course_student where name = '" + name.Replace("'", "") + "' "
                + " and  cell = '" + cell.Replace("'","") + "' and c.id = course_id and del = 0 ) " )
                .Include(c => c.courseStudents).OrderByDescending(c => c.id).AsNoTracking().ToListAsync();
            return Ok(courseList);
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
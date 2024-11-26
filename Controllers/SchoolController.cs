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

            public bool needEvaluated {get; set;}

            public bool needImages {get; set;}

            public bool needVideo {get; set;}

            public List<Course> couses {get; set;} = null;

            
        }

        public SchoolController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _memberHelper = new User.MemberController(_db, _config);
        }

        [HttpGet("{staffId}")]
        public async Task<ActionResult<Staff>> GetStaffById(int staffId)
        {

            return Ok(await _db.schoolStaff.FindAsync(staffId));

        }

        [HttpPost]
        public async Task<ActionResult<Staff>> UpdateStaffInfo([FromBody]Staff staff , [FromQuery] string sessionKey, [FromQuery] string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff me = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            if (me == null || !me.role.Trim().Equals("校长") || staff.id == 0)
            {
                return BadRequest();
            }

            _db.schoolStaff.Entry(staff).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(staff);

        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<List<Student>>> GetStudentsByCell(string cell, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff me = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            if (me == null)
            {
                return BadRequest();
            }
            List<Course> courses = await GetCourses(DateTime.MinValue, DateTime.MaxValue, (int)me.member_id, 0);
            List<Student> students = GetStudents(courses);
            List<Student> newStudents = new List<Student>();
            for(int i = 0; i < students.Count; i++)
            {
                if (students[i].cell.Trim().Equals(cell.Trim()))
                {
                    newStudents.Add(students[i]);
                }
            }
            return Ok(newStudents);
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
                            student.needEvaluated = student.needEvaluated || (!courseStudent.haveEvaluated);
                            student.needImages = student.needImages || (!courseStudent.haveImages);
                            student.needVideo = student.needVideo || (!courseStudent.haveVideo);
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
                            needEvaluated = !courseStudent.haveEvaluated,
                            needImages = !courseStudent.haveImages,
                            needVideo = !courseStudent.haveVideo,
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

        [HttpGet("{trainerId}")]
        public async Task<ActionResult<List<Course>>> GetCoursesByStaff(int trainerId, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff staff = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            Staff trainer = await _db.schoolStaff.FindAsync(trainerId);
            if (staff == null || trainer == null)
            {
                return BadRequest();
            }
            if (!Belong(staff, trainer))
            {
                return NoContent();
            }
            return Ok(await GetCourses(DateTime.MinValue, DateTime.MaxValue, (int)trainer.member_id, 0));

        }
        [HttpGet("{trainerId}")]
        public async Task<ActionResult<List<Student>>> GetCourseStudentsByStaff(int trainerId, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff staff = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            Staff trainer = await _db.schoolStaff.FindAsync(trainerId);
            if (staff == null || trainer == null)
            {
                return BadRequest();
            }
            if (!Belong(staff, trainer))
            {
                return NoContent();
            }
            List<Course> courses = (List<Course>)((OkObjectResult)((await GetCoursesByStaff(trainerId, sessionKey, sessionType)).Result)).Value;
            return Ok(GetStudents(courses));
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

        [HttpGet]
        public async Task<ActionResult<Staff>> NewStaff(string name, string cell, string gender, string sub, 
            string team, string role, string skiLevel, string boardLevel, string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            name = Util.UrlDecode(name);
            sub = Util.UrlDecode(sub);
            team = Util.UrlDecode(team);
            role = Util.UrlDecode(role);
            skiLevel = Util.UrlDecode(skiLevel);
            boardLevel = Util.UrlDecode(boardLevel);
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff staff = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            if (staff == null || staff.role.Trim().Equals("教练") || staff.role.Trim().Equals("客服"))
            {
                return BadRequest();
            }

            var staffList = await _db.schoolStaff.Where(s => s.temp_filled_cell.Trim().Equals(cell.Trim()))
                .AsNoTracking().ToListAsync();

            if (staffList != null && staffList.Count > 0)
            {
                return NotFound();
            }


            Staff newStaff = new Staff()
            {
                id = 0, 
                member_id = null,
                temp_filled_cell = cell,
                temp_filled_name = name,
                school_name = "万龙滑雪学校",
                sub_school_name = sub,
                team = team,
                temp_filled_gender = gender,
                role = role,
                avatar = "",
                ski_level = skiLevel,
                board_level = boardLevel
            };

            await _db.schoolStaff.AddAsync(newStaff);
            await _db.SaveChangesAsync();
            staffList = await _db.schoolStaff.Where(s => s.temp_filled_cell.Trim().Equals(cell.Trim()))
                .AsNoTracking().ToListAsync();
            return Ok(staffList[0]);

        }

        [HttpGet]
        public async Task<ActionResult<List<Course>>> GetUnEvaluatedCoursesInMyRange(string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            List<Course> courses = (List<Course>)((OkObjectResult)(await GetCoursesInMyRange(sessionKey, sessionType)).Result).Value;
            List<Course> newCourses = new List<Course>();
            for(int i = 0; i < courses.Count; i++)
            {
                Course course = courses[i];
                if (!course.haveEvaluated || !course.haveImages || !course.haveVideo)
                {
                    newCourses.Add(courses[i]);
                }
                
            }
            return Ok(newCourses);
        }

        [HttpGet]
        public async Task<ActionResult<List<Course>>> GetCoursesInMyRange(string sessionKey, string sessionType = "wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff me = (Staff)((OkObjectResult)(await GetStaffInfo(sessionKey, sessionType)).Result).Value;
            if (me.role.Trim().Equals("教练"))
            {
                return Ok(await _db.schoolCourse.Where(c => (c.del == 0 && (c.trainer_member_id == me.member_id || c.oper_member_id == me.member_id))).AsNoTracking().ToListAsync());
            }
            else if (me.role.Trim().Equals("校长") || me.role.Trim().Equals("客服"))
            {
                return Ok(await _db.schoolCourse.Where(c => c.del == 0).AsNoTracking().ToListAsync());
            }
            List<Course> courses = await _db.schoolCourse.Where(c => c.del == 0).AsNoTracking().ToListAsync();
            List<Course> newCourses = new List<Course>();
            List<Staff> staffList = (List<Staff>)((OkObjectResult)(await GetStaffList(1, sessionKey)).Result).Value;
            for(int i = 0; i < courses.Count; i++)
            {
                Course course = courses[i];
                Staff? staff = null;
                for(int j = 0; j < staffList.Count; j++)
                {
                    if (staffList[j].member_id == course.trainer_member_id 
                        || staffList[j].member_id == course.oper_member_id)
                    {
                        staff = (Staff)staffList[j];
                    }
                }
                if (staff == null || !Belong(me, staff))
                {
                    continue;
                }
                else
                {
                    newCourses.Add(course);
                }
            }
            return Ok(newCourses);
        }

        [NonAction]
        public Staff RemoveSensitiveInfo(Staff staff)
        {
            //staff.member_id = 0;
            return staff;
        }

        [NonAction]
        public bool Belong(Staff staff, Staff trainer)
        {
            bool belong = false;
            
            switch(staff.role.Trim())
            {
                case "校长":
                case "客服":
                    belong = true;
                    break;
                case "分校长":
                    if (trainer.sub_school_name.Trim().Equals(staff.sub_school_name.Trim()))
                    {
                        belong = true;
                    }
                    else
                    {
                        belong = false;
                    }
                    break;
                case "队长":
                    if (trainer.sub_school_name.Trim().Equals(staff.sub_school_name.Trim())
                        && trainer.team.Trim().Equals(staff.team.Trim()))
                    {
                        belong = true;
                    }
                    else
                    {
                        belong = false;
                    }
                    break;
                default:
                    break;

            }

            return belong;

        }
       



    }
}
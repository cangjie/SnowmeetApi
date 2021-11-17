using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
    [ApiController]
    public class SchoolLessonController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public SchoolLessonController(ApplicationDBContext context)
        {
            _context = context;
            UnicUser._context = context;
        }
        [HttpGet("{sessionKey}")]
        public bool IsStaff(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            bool ret = false;
            if (sessionKey != null)
            {
                UnicUser user = UnicUser.GetUnicUser(sessionKey.Trim());
                if (user != null)
                {
                    if ((user.miniAppUser != null && user.miniAppUser.is_admin == 1) 
                        || (user.officialAccountUser != null && user.officialAccountUser.is_admin == 1))
                    {
                        if (_context.SchoolStaffs.Find(user.miniAppOpenId.Trim()) != null
                            || _context.SchoolStaffs.Find(user.officialAccountOpenId) != null)
                        {
                            ret = true;
                        }
                    }
                    
                }
            }
            return ret;
        }

        // GET: api/SchoolLesson
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SchoolLesson>>> GetSchoolLessons(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (!IsStaff(sessionKey))
            {
                return NotFound();
            }
            var lessonsArr = await _context.SchoolLessons.OrderByDescending(l => l.id).ToListAsync();
            for (int i = 0; i < lessonsArr.Count; i++)
            {
                if (DateTime.Now - lessonsArr[i].create_date < new TimeSpan(3, 0, 0))
                {
                    if (lessonsArr[i].status == "支付未成功")
                    {
                        if (await _context.OrderOnlines.AnyAsync<OrderOnline>(o => o.id == lessonsArr[i].order_id))
                        {
                            OrderOnline order =  _context.OrderOnlines.Find(lessonsArr[i].order_id);
                            if (order.pay_state != 0)
                            {
                                lessonsArr[i].pay_state = order.pay_state;
                                _context.Entry(lessonsArr[i]).State = EntityState.Modified;

                                try
                                {
                                    await _context.SaveChangesAsync();
                                }
                                catch (DbUpdateConcurrencyException)
                                {
                                    if (!SchoolLessonExists(lessonsArr[i].id))
                                    {
                                        return NotFound();
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                            }

                        }
                        
                    }
                }
                else
                {
                    break;
                }
            }
            return lessonsArr;
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<SchoolLesson>> GetSchoolLessonByOrderId(int orderId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            var schoolLesson = await _context.SchoolLessons.FirstAsync<SchoolLesson>(s => s.order_id == orderId);
            
            if (IsStaff(sessionKey) || schoolLesson.open_id.Trim().Equals(user.miniAppOpenId))
            {
                return  schoolLesson;
            }
            else
            {
                return NotFound();
            }
           
        }

        

        // GET: api/SchoolLesson/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SchoolLesson>> GetSchoolLesson(int id, string sessionKey, string cell = "")
        {
            /*
            if (!IsStaff(sessionKey))
            {
                return NotFound();
            }
            */
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);

            var schoolLesson = await _context.SchoolLessons.FindAsync(id);
            bool canDisplay = false;
            if (IsStaff(sessionKey))
            {
                canDisplay = true;
            }
            else if (schoolLesson.cell_number.Trim().Equals(cell.Trim())
                && schoolLesson.open_id.Trim().Equals(""))
            {
                canDisplay = true;
            }
            else if (schoolLesson.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                canDisplay = true;
            }
            if (schoolLesson == null || !canDisplay)
            {
                return NotFound();
            }

            return schoolLesson;
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<SchoolLesson>> SyncPayState(int orderId)
        {
            if (!_context.OrderOnlines.Any(o => o.id == orderId))
            {
                return NotFound();
            }
            var orderOnline = await _context.OrderOnlines.FindAsync(orderId);
            if (orderOnline.pay_state == 1)
            {
                if (!_context.SchoolLessons.Any(o => o.order_id == orderId))
                {
                    return NotFound();
                }
                var lesson = await _context.SchoolLessons.FirstAsync<SchoolLesson>(s => s.order_id == orderId);
                lesson.pay_state = 1;
                _context.Entry(lesson).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SchoolLessonExists(lesson.id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return lesson;

            }
            else
            {
                return NoContent();
            }

        }
        
        [HttpGet("{id}")]
        public async Task<ActionResult<SchoolLesson>> AssignOpenId(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            SchoolLesson lesson = _context.SchoolLessons.Find(id);
            if (lesson.cell_number.Trim().Equals(user.miniAppUser.cell_number.Trim()))
            {
                lesson.open_id = user.miniAppUser.open_id.Trim();
                _context.Entry(lesson).State = EntityState.Modified;
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SchoolLessonExists(id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return lesson;

            }
            return NoContent();
        }
        

        // PUT: api/SchoolLesson/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSchoolLesson(int id, string sessionKey, SchoolLesson schoolLesson)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (id != schoolLesson.id)
            {
                return BadRequest();
            }

            if (!IsStaff(sessionKey))
            {
                int orderId = schoolLesson.order_id;
                schoolLesson = _context.SchoolLessons.Find(id);
                schoolLesson.order_id = orderId;
            }

            _context.Entry(schoolLesson).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SchoolLessonExists(id))
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

        

        // POST: api/SchoolLesson
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SchoolLesson>> PostSchoolLesson(SchoolLesson schoolLesson, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (!IsStaff(sessionKey))
            {
                return NotFound();
            }
            string assistantOpenId = UnicUser.GetUnicUser(sessionKey).miniAppOpenId;
            if (schoolLesson.open_id == null)
            {
                schoolLesson.open_id = "";
            }
            if (schoolLesson.gender == null)
            {
                schoolLesson.gender = "";
            }
            if (schoolLesson.student_name == null)
            {
                schoolLesson.student_name = "";
            }
            if (schoolLesson.student_cell_number == null)
            {
                schoolLesson.student_cell_number = "";
            }
            if (schoolLesson.student_gender == null)
            {
                schoolLesson.student_gender = "";
            }
            if (schoolLesson.student_relation == null)
            {
                schoolLesson.student_relation = "";
            }
            if (schoolLesson.demand == null)
            {
                schoolLesson.demand = "";
            }
            if (schoolLesson.resort == null)
            {
                schoolLesson.resort = "";
            }
            if (schoolLesson.lesson_date == DateTime.MinValue)
            {
                schoolLesson.lesson_date = DateTime.Now.Date;
            }
            if (schoolLesson.instructor_open_id == null)
            {
                schoolLesson.instructor_open_id = "";
            }
            if (schoolLesson.training_plan == null)
            {
                schoolLesson.training_plan = "";
            }
            if (schoolLesson.pay_method == null)
            {
                schoolLesson.pay_method = "";
            }
            if (schoolLesson.memo == null)
            {
                schoolLesson.memo = "";
            }
            if (schoolLesson.videos == null)
            {
                schoolLesson.videos = "";
            }
            schoolLesson.create_date = DateTime.Now;
            schoolLesson.assistant = assistantOpenId.Trim();

            if (schoolLesson.use_memo == null)
            {
                schoolLesson.use_memo = "";
            }


            _context.SchoolLessons.Add(schoolLesson);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSchoolLesson", new { id = schoolLesson.id }, schoolLesson);
        }

        // DELETE: api/SchoolLesson/5
        /*
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSchoolLesson(int id)
        {
            var schoolLesson = await _context.SchoolLessons.FindAsync(id);
            if (schoolLesson == null)
            {
                return NotFound();
            }

            _context.SchoolLessons.Remove(schoolLesson);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool SchoolLessonExists(int id)
        {
            return _context.SchoolLessons.Any(e => e.id == id);
        }
    }
}

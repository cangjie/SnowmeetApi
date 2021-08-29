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
    [Route("[controller]")]
    [ApiController]
    public class SchoolLessonController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public SchoolLessonController(ApplicationDBContext context)
        {
            _context = context;
            UnicUser._context = context;
        }

        public bool IsStaff(string sessionKey)
        {
            bool ret = false;
            if (sessionKey != null)
            {
                UnicUser user = UnicUser.GetUnicUser(sessionKey.Trim());
                if (user != null)
                {
                    if (user.miniAppUser.is_admin == 1 || user.officialAccountUser.is_admin == 1)
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
            if (!IsStaff(sessionKey))
            {
                return NotFound();
            }
            return await _context.SchoolLessons.ToListAsync();
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
            UnicUser user = UnicUser.GetUnicUser(sessionKey);

            var schoolLesson = await _context.SchoolLessons.FindAsync(id);
            bool canDisplay = false;
            if (IsStaff(sessionKey))
            {
                canDisplay = true;
            }
            else if (schoolLesson.cell_number.Trim().Equals(cell.Trim()))
            {
                canDisplay = true;
            }
            else if (schoolLesson.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                canDisplay = true;
            }
            if (schoolLesson == null && !canDisplay)
            {
                return NotFound();
            }

            return schoolLesson;
        }

        // PUT: api/SchoolLesson/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSchoolLesson(int id, string sessionKey, SchoolLesson schoolLesson)
        {
            
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
            schoolLesson.assistant = assistantOpenId.Trim();



            _context.SchoolLessons.Add(schoolLesson);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSchoolLesson", new { id = schoolLesson.id }, schoolLesson);
        }

        // DELETE: api/SchoolLesson/5
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

        private bool SchoolLessonExists(int id)
        {
            return _context.SchoolLessons.Any(e => e.id == id);
        }
    }
}

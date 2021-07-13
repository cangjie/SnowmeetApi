using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SchoolLessonController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public SchoolLessonController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/SchoolLesson
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SchoolLesson>>> GetSchoolLessons()
        {
            return await _context.SchoolLessons.ToListAsync();
        }

        // GET: api/SchoolLesson/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SchoolLesson>> GetSchoolLesson(int id)
        {
            var schoolLesson = await _context.SchoolLessons.FindAsync(id);

            if (schoolLesson == null)
            {
                return NotFound();
            }

            return schoolLesson;
        }

        // PUT: api/SchoolLesson/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSchoolLesson(int id, SchoolLesson schoolLesson)
        {
            if (id != schoolLesson.id)
            {
                return BadRequest();
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
        public async Task<ActionResult<SchoolLesson>> PostSchoolLesson(SchoolLesson schoolLesson)
        {
            if (schoolLesson.gender == null)
            {
                schoolLesson.gender = "";
            }



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

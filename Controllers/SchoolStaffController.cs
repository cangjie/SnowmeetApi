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
    public class SchoolStaffController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public SchoolStaffController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/SchoolStaff
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SchoolStaff>>> GetSchoolStaffs()
        {
            return await _context.SchoolStaffs.ToListAsync();
        }

        // GET: api/SchoolStaff/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SchoolStaff>> GetSchoolStaff(string id)
        {
            var schoolStaff = await _context.SchoolStaffs.FindAsync(id);

            if (schoolStaff == null)
            {
                return NotFound();
            }

            return schoolStaff;
        }

        // PUT: api/SchoolStaff/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSchoolStaff(string id, SchoolStaff schoolStaff)
        {
            if (id != schoolStaff.open_id)
            {
                return BadRequest();
            }

            _context.Entry(schoolStaff).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SchoolStaffExists(id))
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

        // POST: api/SchoolStaff
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SchoolStaff>> PostSchoolStaff(SchoolStaff schoolStaff)
        {
            _context.SchoolStaffs.Add(schoolStaff);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SchoolStaffExists(schoolStaff.open_id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetSchoolStaff", new { id = schoolStaff.open_id }, schoolStaff);
        }

        // DELETE: api/SchoolStaff/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSchoolStaff(string id)
        {
            var schoolStaff = await _context.SchoolStaffs.FindAsync(id);
            if (schoolStaff == null)
            {
                return NotFound();
            }

            _context.SchoolStaffs.Remove(schoolStaff);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SchoolStaffExists(string id)
        {
            return _context.SchoolStaffs.Any(e => e.open_id == id);
        }
    }
}

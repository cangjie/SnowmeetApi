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
    [Route("[controller]")]
    [ApiController]
    public class MaintainLiveController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public MaintainLiveController(ApplicationDBContext context)
        {
            _context = context;
        }


        // GET: api/MaintainLive
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintainLive>>> GetMaintainLives()
        {
            return await _context.MaintainLives.ToListAsync();
        }

        // GET: api/MaintainLive/5
        [HttpGet("{id}")]
        public async Task<ActionResult<MaintainLive>> GetMaintainLive(int id)
        {
            var maintainLive = await _context.MaintainLives.FindAsync(id);

            if (maintainLive == null)
            {
                return NotFound();
            }

            return maintainLive;
        }

        // PUT: api/MaintainLive/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaintainLive(int id, MaintainLive maintainLive)
        {
            if (id != maintainLive.id)
            {
                return BadRequest();
            }

            _context.Entry(maintainLive).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MaintainLiveExists(id))
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

        // POST: api/MaintainLive
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<MaintainLive>> PostMaintainLive(MaintainLive maintainLive)
        {
            _context.MaintainLives.Add(maintainLive);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetMaintainLive", new { id = maintainLive.id }, maintainLive);
        }

        // DELETE: api/MaintainLive/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaintainLive(int id, string sessionKey)
        {
            if (sessionKey.Trim().Equals(""))
            {
                return NoContent();
            }
            var maintainLive = await _context.MaintainLives.FindAsync(id);
            if (maintainLive == null)
            {
                return NotFound();
            }

            _context.MaintainLives.Remove(maintainLive);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool MaintainLiveExists(int id)
        {
            return _context.MaintainLives.Any(e => e.id == id);
        }
    }
}

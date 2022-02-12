using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Ticket;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Card;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
    [ApiController]
    public class SummerMaintainController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;

        public string _appId = "";
        public SummerMaintainController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        // GET: api/SummerMaintain
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SummerMaintain>>> GetSummerMaintain()
        {
            return await _context.SummerMaintain.ToListAsync();
        }

        // GET: api/SummerMaintain/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SummerMaintain>> GetSummerMaintain(int id)
        {
            var summerMaintain = await _context.SummerMaintain.FindAsync(id);

            if (summerMaintain == null)
            {
                return NotFound();
            }

            return summerMaintain;
        }

        // PUT: api/SummerMaintain/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSummerMaintain(int id, SummerMaintain summerMaintain)
        {
            if (id != summerMaintain.id)
            {
                return BadRequest();
            }

            _context.Entry(summerMaintain).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SummerMaintainExists(id))
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

        // POST: api/SummerMaintain
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SummerMaintain>> PostSummerMaintain(SummerMaintain summerMaintain)
        {
            _context.SummerMaintain.Add(summerMaintain);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSummerMaintain", new { id = summerMaintain.id }, summerMaintain);
        }

        // DELETE: api/SummerMaintain/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSummerMaintain(int id)
        {
            var summerMaintain = await _context.SummerMaintain.FindAsync(id);
            if (summerMaintain == null)
            {
                return NotFound();
            }

            _context.SummerMaintain.Remove(summerMaintain);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SummerMaintainExists(int id)
        {
            return _context.SummerMaintain.Any(e => e.id == id);
        }
    }
}

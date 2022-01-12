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
    public class BltDeviceController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public BltDeviceController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/BltDevice
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BltDevice>>> GetBltDevice()
        {
            return await _context.BltDevice.ToListAsync();
        }

        
        // GET: api/BltDevice/5
        [HttpGet("{id}")]
        public async Task<ActionResult<BltDevice>> GetBltDevice(int id)
        {
            var bltDevice = await _context.BltDevice.FindAsync(id);

            if (bltDevice == null)
            {
                return NotFound();
            }

            return bltDevice;
        }
        /*
        // PUT: api/BltDevice/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutBltDevice(int id, BltDevice bltDevice)
        {
            if (id != bltDevice.id)
            {
                return BadRequest();
            }

            _context.Entry(bltDevice).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BltDeviceExists(id))
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

        // POST: api/BltDevice
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<BltDevice>> PostBltDevice(BltDevice bltDevice)
        {
            _context.BltDevice.Add(bltDevice);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetBltDevice", new { id = bltDevice.id }, bltDevice);
        }

        // DELETE: api/BltDevice/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBltDevice(int id)
        {
            var bltDevice = await _context.BltDevice.FindAsync(id);
            if (bltDevice == null)
            {
                return NotFound();
            }

            _context.BltDevice.Remove(bltDevice);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool BltDeviceExists(int id)
        {
            return _context.BltDevice.Any(e => e.id == id);
        }
    }
}

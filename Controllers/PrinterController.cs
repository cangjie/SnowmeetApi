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
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class PrinterController : ControllerBase
    {
        private readonly ApplicationDBContext _db;

        public PrinterController(ApplicationDBContext context)
        {
            _db = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Printer>>> GetPrinters(string shop, string color, string sessionKey)
        {
            var l = await _db.Printer.Where(p => p.id <= 3)
                .AsNoTracking().ToListAsync();
            return Ok(l);

            /*
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            color = Util.UrlDecode(color);
            shop = Util.UrlDecode(shop);

            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin != 1)
            {
                return BadRequest();
            }

            string cell = adminUser.cell_number.Trim();

            if (cell.Length != 11)
            {
                return BadRequest();
            }

            var l = await _db.Printer.Where(p => p.color.Equals(color)
                && p.shop.Equals(shop) && p.owner.IndexOf(cell) >= 0)
                .AsNoTracking().ToListAsync();
            return Ok(l);
            */
        }

        [NonAction]
        public async Task<MiniAppUser> GetUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
            return user.miniAppUser;
        }

        /*

        // GET: api/Printer
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Printer>>> GetPrinter()
        {
            return await _context.Printer.ToListAsync();
        }

        // GET: api/Printer/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Printer>> GetPrinter(int id)
        {
            var printer = await _context.Printer.FindAsync(id);

            if (printer == null)
            {
                return NotFound();
            }

            return printer;
        }

        // PUT: api/Printer/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPrinter(int id, Printer printer)
        {
            if (id != printer.id)
            {
                return BadRequest();
            }

            _context.Entry(printer).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PrinterExists(id))
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

        // POST: api/Printer
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Printer>> PostPrinter(Printer printer)
        {
            _context.Printer.Add(printer);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetPrinter", new { id = printer.id }, printer);
        }

        // DELETE: api/Printer/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePrinter(int id)
        {
            var printer = await _context.Printer.FindAsync(id);
            if (printer == null)
            {
                return NotFound();
            }

            _context.Printer.Remove(printer);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool PrinterExists(int id)
        {
            return _db.Printer.Any(e => e.id == id);
        }
    }
}

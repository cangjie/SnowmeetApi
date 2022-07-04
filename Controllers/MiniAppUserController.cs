using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MiniAppUserController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        public string _appId = "";

        public MiniAppUserController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        [NonAction]
        public async Task<ActionResult<string>> GetOpenIdByCell(string cell)
        {
            string openId = "";
            var uArr = await _context.MiniAppUsers.Where(u => u.cell_number.Trim().Equals(cell.Trim()))
                .OrderByDescending(u => u.create_date).ToListAsync();
            if (uArr != null && uArr.Count > 0)
            {
                openId = uArr[0].open_id.Trim();
            }
            return openId;
        }



        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> GetMiniAppUser(string openId, string sessionKey)
        {
            openId = Util.UrlDecode(openId.Trim());
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            return await _context.MiniAppUsers.FindAsync(openId);
        }

        /*

        // GET: api/MiniAppUser
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MiniAppUser>>> GetMiniAppUsers()
        {
            return await _context.MiniAppUsers.ToListAsync();
        }

        // GET: api/MiniAppUser/5
        [HttpGet("{id}")]
        public async Task<ActionResult<MiniAppUser>> GetMiniAppUser(string id)
        {
            var miniAppUser = await _context.MiniAppUsers.FindAsync(id);

            if (miniAppUser == null)
            {
                return NotFound();
            }

            return miniAppUser;
        }

        // PUT: api/MiniAppUser/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMiniAppUser(string id, MiniAppUser miniAppUser)
        {
            if (id != miniAppUser.open_id)
            {
                return BadRequest();
            }

            _context.Entry(miniAppUser).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MiniAppUserExists(id))
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

        // POST: api/MiniAppUser
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<MiniAppUser>> PostMiniAppUser(MiniAppUser miniAppUser)
        {
            _context.MiniAppUsers.Add(miniAppUser);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (MiniAppUserExists(miniAppUser.open_id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetMiniAppUser", new { id = miniAppUser.open_id }, miniAppUser);
        }

        // DELETE: api/MiniAppUser/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMiniAppUser(string id)
        {
            var miniAppUser = await _context.MiniAppUsers.FindAsync(id);
            if (miniAppUser == null)
            {
                return NotFound();
            }

            _context.MiniAppUsers.Remove(miniAppUser);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool MiniAppUserExists(string id)
        {
            return _context.MiniAppUsers.Any(e => e.open_id == id);
        }
    }
}

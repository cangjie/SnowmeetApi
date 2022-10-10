using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Background;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.Background
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class BackgroundLoginSessionController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration _originConfig;

        public BackgroundLoginSessionController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _originConfig = config;
            UnicUser._context = context;
        }

        [HttpGet("{timeStamp}")]
        public async Task<ActionResult<string>> GetLoginQrCodeUrl(long timeStamp)
        {
            BackgroundLoginSession session = new BackgroundLoginSession()
            {
                timestamp = timeStamp,
                session_key = ""
            };
            await _context.BackgroundLoginSession.AddAsync(session);
            await _context.SaveChangesAsync();
            return "http://weixin.snowmeet.top/show_qrcode.aspx?qrcodetext=" + timeStamp.ToString();
        }


        [HttpGet("{timeStamp}")]
        public async Task<ActionResult<BackgroundLoginSession>> GetBackgroundLoginSession(long timeStamp)
        {
            return await _context.BackgroundLoginSession.FindAsync(timeStamp);
        }

        [HttpGet("{timeStamp}")]
        public async Task<ActionResult<BackgroundLoginSession>> SetSessionKey(long timeStamp, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            BackgroundLoginSession session = await _context.BackgroundLoginSession.FindAsync(timeStamp);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (user.isAdmin)
            {
                session.session_key = sessionKey;
                _context.Entry(session).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return session;
            }
            else
            {
                return BadRequest();
            }
        }

        /*
        // GET: api/BackgroundLoginSession
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BackgroundLoginSession>>> GetBackgroundLoginSession()
        {
            return await _context.BackgroundLoginSession.ToListAsync();
        }

        // GET: api/BackgroundLoginSession/5
        [HttpGet("{id}")]
        public async Task<ActionResult<BackgroundLoginSession>> GetBackgroundLoginSession(long id)
        {
            var backgroundLoginSession = await _context.BackgroundLoginSession.FindAsync(id);

            if (backgroundLoginSession == null)
            {
                return NotFound();
            }

            return backgroundLoginSession;
        }

        // PUT: api/BackgroundLoginSession/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutBackgroundLoginSession(long id, BackgroundLoginSession backgroundLoginSession)
        {
            if (id != backgroundLoginSession.timestamp)
            {
                return BadRequest();
            }

            _context.Entry(backgroundLoginSession).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BackgroundLoginSessionExists(id))
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

        // POST: api/BackgroundLoginSession
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<BackgroundLoginSession>> PostBackgroundLoginSession(BackgroundLoginSession backgroundLoginSession)
        {
            _context.BackgroundLoginSession.Add(backgroundLoginSession);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetBackgroundLoginSession", new { id = backgroundLoginSession.timestamp }, backgroundLoginSession);
        }

        // DELETE: api/BackgroundLoginSession/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBackgroundLoginSession(long id)
        {
            var backgroundLoginSession = await _context.BackgroundLoginSession.FindAsync(id);
            if (backgroundLoginSession == null)
            {
                return NotFound();
            }

            _context.BackgroundLoginSession.Remove(backgroundLoginSession);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool BackgroundLoginSessionExists(long id)
        {
            return _context.BackgroundLoginSession.Any(e => e.timestamp == id);
        }
    }
}

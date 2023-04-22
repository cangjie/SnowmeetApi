using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ReceptController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public ReceptController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }

        [HttpGet]
        public ActionResult<SerialTest> TestSerial()
        {
            string json = "{\"id\": 0, \"name\": \"cangjie\", \"cell\": \"13501177897\", \"joinDate\": \"2023-4-22\"}";
            object s = JsonConvert.DeserializeObject(json, typeof(SerialTest));
            return (SerialTest)s;
        }

        [HttpGet]
        public ActionResult<string> TestDeSerial()
        {
            SerialTest t = new SerialTest()
            {
                id = 1,
                name = "cj",
                cell = "18601197897",
                joinDate = DateTime.Now
            };
            //string json = "{\"id\": 0, \"name\": \"cangjie\", \"cell\": \"13501177897\", \"joinDate\": \"2023-4-22\"}";
            string s = JsonConvert.SerializeObject(t);
            return s;
        }

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<Recept>> Recept(string sessionKey, Recept recept)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (await IsAdmin(sessionKey))
            {
                return BadRequest();
            }

            if (recept.id == 0)
            {
                await _context.Recept.AddAsync(recept);
                await _context.SaveChangesAsync();
            }
            else
            {
                _context.Entry(recept).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return Ok(recept);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> GetRecept(int id, string sessionKey)
        {
            if (await IsAdmin(sessionKey))
            {
                return BadRequest();
            }
            Recept recept = await _context.Recept.FindAsync(id);
            return Ok(recept);
        }

        [HttpGet("{sessionKey}")]
        public async Task<ActionResult<IEnumerable<Recept>>> GetUnfinishRecept(string sessionKey, string shop)
        {
            shop = Util.UrlDecode(shop);
            if(await IsAdmin(sessionKey))
            {
                return BadRequest();
            }
            var list = await _context.Recept.Where(r => r.submit_return_id == 0 && r.shop.Equals(shop))
                .OrderByDescending(r => r.id).ToListAsync();
            return Ok(list);
        }

        [NonAction]
        public async Task<bool> IsAdmin(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            return user.isAdmin;
        }

        /*
        // GET: api/Recept
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Recept>>> GetRecept()
        {
            return await _context.Recept.ToListAsync();
        }

        // GET: api/Recept/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> GetRecept(int id)
        {
            var recept = await _context.Recept.FindAsync(id);

            if (recept == null)
            {
                return NotFound();
            }

            return recept;
        }

        // PUT: api/Recept/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutRecept(int id, Recept recept)
        {
            if (id != recept.id)
            {
                return BadRequest();
            }

            _context.Entry(recept).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ReceptExists(id))
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

        // POST: api/Recept
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Recept>> PostRecept(Recept recept)
        {
            _context.Recept.Add(recept);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetRecept", new { id = recept.id }, recept);
        }

        // DELETE: api/Recept/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRecept(int id)
        {
            var recept = await _context.Recept.FindAsync(id);
            if (recept == null)
            {
                return NotFound();
            }

            _context.Recept.Remove(recept);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool ReceptExists(int id)
        {
            return _context.Recept.Any(e => e.id == id);
        }
    }
}

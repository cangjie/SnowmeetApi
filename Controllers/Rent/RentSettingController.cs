using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.Rent
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RentSettingController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public string _appId = "";
        private IConfiguration _oriConfig;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private MemberController _memberHelper;

        public RentSettingController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            _memberHelper = new MemberController(db, config);
        }

        [HttpGet]
        public async Task<ActionResult<RentCategory>> AddCategory(string code, string name, string sessionKey, string sessionType)
        {
            name = Util.UrlDecode(name);
            code = code == null? "": code.Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            List<RentCategory> rcL = await _db.rentCategory
                .Where(c => c.name.Trim().Length == code.Length + 2)
                .OrderByDescending(c => c.code).ToListAsync();
            string newCode = code;
            if (rcL == null || rcL.Count == 0)
            {
                newCode = newCode + "01";
            }
            else
            {
                RentCategory lastRc = rcL[0];
                int maxV = int.Parse(lastRc.code.Substring(lastRc.code.Length - 2, 2));
                newCode = newCode + (maxV+1).ToString().PadLeft(2, '0');
            }
            RentCategory rcNew = new RentCategory()
            {
                name = name,
                code = newCode
            };
            await _db.rentCategory.AddAsync(rcNew);
            await _db.SaveChangesAsync();
            return Ok(rcNew);
        }

        [HttpGet]
        public async Task<ActionResult<ICollection<RentCategory>>> GetAllCategories()
        {
            var topL = await _db.rentCategory.Where(r => (r.code.Trim().Length == 2)).ToListAsync();
            if (topL == null || topL.Count == 0)
            {
                return BadRequest();
            }
            List<RentCategory> rl = new List<RentCategory>();
            for (int i = 0; i < topL.Count; i++)
            {
                RentCategory rc = (RentCategory)((OkObjectResult)(await GetCategory(topL[i].code)).Result).Value;
                rl.Add(rc);
            }
            return Ok(rl);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> GetCategory(string code = "")
        {
            code = code.Trim();
            RentCategory rc = await _db.rentCategory.FindAsync(code);
            if (rc == null)
            {
                return null;
            }
            var rcL = await _db.rentCategory.Where(r => r.code.Trim().Length == code.Length + 2
                && r.code.StartsWith(code)).ToListAsync();
            if (rcL != null && rcL.Count > 0)
            {
                List<RentCategory> children = new List<RentCategory>();
                for (int i = 0; i < rcL.Count; i++)
                {
                    RentCategory child = (RentCategory)((OkObjectResult)(await GetCategory(rcL[i].code)).Result).Value;
                    if (child != null)
                    {
                        children.Add(child);
                    }

                }
                rc.children = children;
            }
            return Ok(rc);
        }


        /*

        // GET: api/RentSetting
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RentCategory>>> GetRentCategory()
        {
            return await _context.RentCategory.ToListAsync();
        }

        // GET: api/RentSetting/5
        [HttpGet("{id}")]
        public async Task<ActionResult<RentCategory>> GetRentCategory(string id)
        {
            var rentCategory = await _context.RentCategory.FindAsync(id);

            if (rentCategory == null)
            {
                return NotFound();
            }

            return rentCategory;
        }

        // PUT: api/RentSetting/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutRentCategory(string id, RentCategory rentCategory)
        {
            if (id != rentCategory.code)
            {
                return BadRequest();
            }

            _context.Entry(rentCategory).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RentCategoryExists(id))
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

        // POST: api/RentSetting
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<RentCategory>> PostRentCategory(RentCategory rentCategory)
        {
            _context.RentCategory.Add(rentCategory);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (RentCategoryExists(rentCategory.code))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetRentCategory", new { id = rentCategory.code }, rentCategory);
        }

        // DELETE: api/RentSetting/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRentCategory(string id)
        {
            var rentCategory = await _context.RentCategory.FindAsync(id);
            if (rentCategory == null)
            {
                return NotFound();
            }

            _context.RentCategory.Remove(rentCategory);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool RentCategoryExists(string id)
        {
            return _context.RentCategory.Any(e => e.code == id);
        }
        */
    }
}

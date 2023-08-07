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
using SnowmeetApi.Models.Rent;
using Org.BouncyCastle.Asn1.X509;

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



        [HttpGet]
        public async Task<ActionResult<Recept>> NewRecept(string openId, string scene, string shop, string sessionKey, string code = "")
        {
            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin != 1)
            {
                return BadRequest();
            }
            openId = Util.UrlDecode(openId);
            scene = Util.UrlDecode(scene);
            shop = Util.UrlDecode(shop);
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(openId);
            string realName = user.real_name.Trim();
            switch (user.gender.Trim())
            {
                case "男":
                    realName += " 先生";
                    break;
                case "女":
                    realName += " 女士";
                    break;
                default:
                    break;
            }
            string entityJson = "";

            switch (scene)
            {
                case "租赁下单":
                    
                    RentOrder order = new RentOrder()
                    {
                        open_id = openId,
                        cell_number = user.cell_number.Trim(),
                        real_name = realName,
                        shop = shop
                    };
                    entityJson = Newtonsoft.Json.JsonConvert.SerializeObject(order);
                    break;
                default:
                    break;
            }
            Recept recept = new Recept()
            {
                shop = shop.Trim(),
                open_id = openId.Trim(),
                cell = user.cell_number.Trim(),
                real_name = user.real_name.Trim(),
                current_step = 0,
                gender = user.gender.Trim(),
                recept_type = scene.Trim(),
                submit_data = entityJson.Trim(),
                recept_staff = adminUser.open_id.Trim(),
                update_staff = "",
                submit_return_id = 0,
                create_date = DateTime.Now,
                update_date = DateTime.Now,
                code = code
            };
            await _context.Recept.AddAsync(recept);
            await _context.SaveChangesAsync();

            return Ok(recept);

        }

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<Recept>> UpdateRecept(string sessionKey, Recept recept)
        {
            //Recept recept = JsonConvert.DeserializeObject(receptJson.ToString(), typeof(Recept));
            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin == 0)
            {
                return BadRequest();
            }
            string entityJson = "";
            switch (recept.recept_type.Trim())
            {
                case "租赁下单":
                    entityJson = Newtonsoft.Json.JsonConvert.SerializeObject(recept.rentOrder);
                    break;
                default:
                    break;
            }
            recept.submit_data = entityJson;
            recept.update_staff = adminUser.open_id.Trim();
            recept.update_date = DateTime.Now;
            _context.Entry(recept).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(recept);
        }
        /*
        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<Recept>> ReceptTest(string sessionKey, Recept recept)
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
        */
        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> GetRecept(int id, string sessionKey)
        {
            if (!(await IsAdmin(sessionKey)))
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

        [NonAction]
        public async Task<MiniAppUser> GetUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            return user.miniAppUser;
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

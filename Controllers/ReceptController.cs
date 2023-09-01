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
            sessionKey = Util.UrlDecode(sessionKey);
            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin != 1)
            {
                return BadRequest();
            }
            openId = Util.UrlDecode(openId);
            scene = Util.UrlDecode(scene);
            shop = Util.UrlDecode(shop);
            string realName = "";
            string gender = "";
            string cell = "";
            if (!openId.Trim().Equals(""))
            {
                MiniAppUser user = await _context.MiniAppUsers.FindAsync(openId);
                realName = user.real_name.Trim();
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
                cell = user.cell_number.Trim();
                gender = user.gender.Trim();
            }
            
            string entityJson = "";

            switch (scene)
            {
                case "租赁下单":
                    
                    RentOrder order = new RentOrder()
                    {
                        open_id = openId,
                        cell_number = cell,
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
                cell = cell,
                real_name = realName,
                current_step = 0,
                gender = gender,
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

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Recept>>> GetUnSubmitRecept(string shop, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (! await IsAdmin(sessionKey))
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop).Trim();
            var list = await _context.Recept
                .Where(r => (r.submit_return_id == 0 && r.create_date.Date == DateTime.Now.Date && r.shop.Trim().Equals(shop)))
                .OrderBy(r => r.id).AsNoTracking().ToListAsync();
            if (list == null)
            {
                return BadRequest();
            }
            for (int i = 0; i < list.Count; i++)
            {
                Recept r = list[i];
                if (!r.recept_staff.Trim().Equals(""))
                {
                    MiniAppUser user = await _context.MiniAppUsers.FindAsync(r.recept_staff.Trim());
                    if (user != null)
                    {
                        r.recept_staff_name = user.real_name.Trim();
                    }
                    
                }
                if (!r.update_staff.Trim().Equals(""))
                {
                    MiniAppUser user = await _context.MiniAppUsers.FindAsync(r.update_staff.Trim());
                    if (user != null)
                    {
                        r.update_staff_name = user.real_name.Trim();
                    }
                }
            }
            return Ok(list);
            //return await _context.Recept.Where(r => )
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> PlaceOrder(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            if (!await IsAdmin(sessionKey))
            {
                return BadRequest();
            }
            Recept r = await _context.Recept.FindAsync(id);
            switch (r.recept_type)
            {
                case "租赁下单":
                    r = await CreateRentOrder(r);
                    break;
                default:
                    break;
            }
            if (r == null) 
            {
                return NotFound();
            }
            return Ok(r);
        }

        [NonAction]
        public async Task<Recept> CreateRentOrder(Recept recept)
        {
            string jsonStr = recept.submit_data.Trim();
            RentOrder rentOrder = JsonConvert.DeserializeObject<RentOrder>(jsonStr);
            if (rentOrder.deposit_real == 0)
            {
                rentOrder.deposit_real = rentOrder.deposit;
            }
            rentOrder.deposit_final = rentOrder.deposit_real 
                - rentOrder.deposit_reduce - rentOrder.deposit_reduce_ticket;
            await _context.RentOrder.AddAsync(rentOrder);
            await _context.SaveChangesAsync();
            recept.rentOrder = rentOrder;
            if (rentOrder.id <= 0)
            {
                return recept;
            }
            recept.submit_return_id = rentOrder.id;
            _context.Entry(recept).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            for (int i = 0; i < rentOrder.details.Length; i++)
            {
                RentOrderDetail detail = rentOrder.details[i];
                detail.rent_list_id = rentOrder.id;
                await _context.RentOrderDetail.AddAsync(detail);
            }
            await _context.SaveChangesAsync();

            if (!rentOrder.pay_option.Trim().Equals("招待"))
            {

                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    type = "押金",
                    open_id = recept.open_id.Trim(),
                    cell_number = recept.cell.Trim(),
                    name = recept.real_name.Trim(),
                    pay_method = rentOrder.payMethod.Trim(),
                    order_price = rentOrder.deposit_final,
                    order_real_pay_price = rentOrder.deposit_final,
                    pay_state = 0,
                    pay_memo = rentOrder.pay_option.Trim(),
                    shop = recept.shop,
                    ticket_amount = 0,
                    have_score = 0,
                    score_rate = 0,
                    ticket_code = recept.code.Trim(),
                    other_discount = 0,
                    final_price = rentOrder.deposit_final,
                    staff_open_id = recept.update_staff.Trim().Equals("") ? recept.recept_staff.Trim() : recept.update_staff.Trim()
                };
                await _context.OrderOnlines.AddAsync(order);
                await _context.SaveChangesAsync();
                rentOrder.order_id = order.id;
                _context.Entry(rentOrder).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            
            return recept;
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

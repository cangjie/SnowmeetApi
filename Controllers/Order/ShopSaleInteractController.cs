using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.Order
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ShopSaleInteractController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;


        public ShopSaleInteractController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();

        }
        [HttpGet]
        public async Task<ActionResult<int>> GetInterviewId(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser._context = _context;
            UnicUser staffUser = UnicUser.GetUnicUser(sessionKey);
            if (!staffUser.isAdmin)
            {
                return NoContent();
            }
            bool needCreateNew = false;
            int retId = 0;
            try
            {
                var scan = await _context
                    .ShopSaleInteract.Where(s => s.staff_mapp_open_id == staffUser.miniAppOpenId.Trim())
                    .OrderByDescending(s => s.id).FirstAsync();
                if (scan == null || scan.scan == 1 || scan.create_date < DateTime.Now.AddMinutes(-600))
                {
                    needCreateNew = true;
                }
                else
                {
                    retId = scan.id;
                }
            }
            catch
            {
                needCreateNew = true;
            }
            
            if (needCreateNew)
            {
                var scanNew = new ShopSaleInteract()
                {
                    id = 0,
                    staff_mapp_open_id = staffUser.miniAppOpenId.Trim(),
                    scan = 0
                };
                await _context.AddAsync(scanNew);
                await _context.SaveChangesAsync();
                return scanNew.id;
            }
            else
            {
                return retId;
            }
            //return NotFound();
        }

        /*

        // GET: api/ShopSaleInteract
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ShopSaleInteract>>> GetShopSaleInteract()
        {
            return await _context.ShopSaleInteract.ToListAsync();
        }

        // GET: api/ShopSaleInteract/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ShopSaleInteract>> GetShopSaleInteract(int id)
        {
            var shopSaleInteract = await _context.ShopSaleInteract.FindAsync(id);

            if (shopSaleInteract == null)
            {
                return NotFound();
            }

            return shopSaleInteract;
        }

        // PUT: api/ShopSaleInteract/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutShopSaleInteract(int id, ShopSaleInteract shopSaleInteract)
        {
            if (id != shopSaleInteract.id)
            {
                return BadRequest();
            }

            _context.Entry(shopSaleInteract).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ShopSaleInteractExists(id))
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

        // POST: api/ShopSaleInteract
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<ShopSaleInteract>> PostShopSaleInteract(ShopSaleInteract shopSaleInteract)
        {
            _context.ShopSaleInteract.Add(shopSaleInteract);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetShopSaleInteract", new { id = shopSaleInteract.id }, shopSaleInteract);
        }

        // DELETE: api/ShopSaleInteract/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteShopSaleInteract(int id)
        {
            var shopSaleInteract = await _context.ShopSaleInteract.FindAsync(id);
            if (shopSaleInteract == null)
            {
                return NotFound();
            }

            _context.ShopSaleInteract.Remove(shopSaleInteract);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        [NonAction]
        private bool ShopSaleInteractExists(int id)
        {
            return _context.ShopSaleInteract.Any(e => e.id == id);
        }
    }
}

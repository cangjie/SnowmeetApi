using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System.Web;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OrderOnlineTempController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public OrderOnlineTempController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/OrderOnlineTemp
        /*
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderOnlineTemp>>> GetOrderOnlineTemp()
        {
            return await _context.OrderOnlineTemp.ToListAsync();
        }
        */
        // GET: api/OrderOnlineTemp/5
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderOnlineTemp>> GetOrderOnlineTemp(int id, string sessionKey)
        {
            var orderOnlineTemp = await _context.OrderOnlineTemp.FindAsync(id);

            if (orderOnlineTemp == null)
            {
                return NotFound();
            }
            if (orderOnlineTemp.online_order_id != null)
            {
                OrderOnline order = _context.OrderOnlines.Find(orderOnlineTemp.online_order_id);
                if (order != null)
                {
                    sessionKey = Util.UrlDecode(sessionKey);
                    
                    UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
                    if (user == null)
                    {
                        return NotFound();
                    }
                    if (!order.open_id.Trim().Equals(user.miniAppOpenId) && order.open_id.Trim().Equals(user.officialAccountOpenId))
                    {
                        return NotFound();
                    }
                }
            }

            return orderOnlineTemp;
        }

        /*

        // PUT: api/OrderOnlineTemp/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderOnlineTemp(int id, OrderOnlineTemp orderOnlineTemp)
        {
            if (id != orderOnlineTemp.id)
            {
                return BadRequest();
            }

            _context.Entry(orderOnlineTemp).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderOnlineTempExists(id))
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

        // POST: api/OrderOnlineTemp
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<OrderOnlineTemp>> PostOrderOnlineTemp(OrderOnlineTemp orderOnlineTemp)
        {
            _context.OrderOnlineTemp.Add(orderOnlineTemp);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetOrderOnlineTemp", new { id = orderOnlineTemp.id }, orderOnlineTemp);
        }

        // DELETE: api/OrderOnlineTemp/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderOnlineTemp(int id)
        {
            var orderOnlineTemp = await _context.OrderOnlineTemp.FindAsync(id);
            if (orderOnlineTemp == null)
            {
                return NotFound();
            }

            _context.OrderOnlineTemp.Remove(orderOnlineTemp);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool OrderOnlineTempExists(int id)
        {
            return _context.OrderOnlineTemp.Any(e => e.id == id);
        }
    }
}

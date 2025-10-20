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
    public class Mi7OrderController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        

        public Mi7OrderController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<SaleReport>>> GetSaleReport(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return NoContent();
            }
            var l = await _context.saleReport.FromSqlRaw(" select mi7_order_id, barCode, sale_price, real_charge, order_id,  "
                + " case  [name] when '' then customer.real_name  else [name] end as [name],  "
                + " case [order_online].cell_number when '' then customer.cell_number  else [order_online].cell_number end as cell_number , "
                + " final_price, shop, staff.real_name as staff, pay_time, pay_method, order_online.memo as memo from mi7_order "
                + " left join order_online on order_id = order_online.[id] "
                + " left join mini_users staff on staff.open_id = staff_open_id "
                + " left join mini_users customer on customer.open_id =  order_online.open_id "
                + " where [type] = '店销现货' and pay_state = 1 and pay_time >= '"
                + startDate.ToShortDateString() + "' and pay_time < '" + endDate.AddDays(1).ToShortDateString() + "' "
                + " order by order_id desc ")
                .AsNoTracking().ToListAsync();
            return Ok(l);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Mi7Order>> GetMi7Order(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            return await _context.mi7Order.FindAsync(id);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Mi7Order>> ModMi7Order(int id, string orderNum, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            orderNum = Util.UrlDecode(orderNum);
            Mi7Order order = await _context.mi7Order.FindAsync(id);
            if (order == null)
            {
                return BadRequest();
            }
            order.mi7_order_id = orderNum;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return order;
        }

        
        /*
        // GET: api/Mi7Order
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Mi7Order>>> Getmi7Order()
        {
            return await _context.mi7Order.ToListAsync();
        }

        // GET: api/Mi7Order/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Mi7Order>> GetMi7Order(int id)
        {
            var mi7Order = await _context.mi7Order.FindAsync(id);

            if (mi7Order == null)
            {
                return NotFound();
            }

            return mi7Order;
        }

        // PUT: api/Mi7Order/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMi7Order(int id, Mi7Order mi7Order)
        {
            if (id != mi7Order.id)
            {
                return BadRequest();
            }

            _context.Entry(mi7Order).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!Mi7OrderExists(id))
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

        // POST: api/Mi7Order
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Mi7Order>> PostMi7Order(Mi7Order mi7Order)
        {
            _context.mi7Order.Add(mi7Order);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetMi7Order", new { id = mi7Order.id }, mi7Order);
        }

        // DELETE: api/Mi7Order/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMi7Order(int id)
        {
            var mi7Order = await _context.mi7Order.FindAsync(id);
            if (mi7Order == null)
            {
                return NotFound();
            }

            _context.mi7Order.Remove(mi7Order);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool Mi7OrderExists(int id)
        {
            return _context.mi7Order.Any(e => e.id == id);
        }
    }
}

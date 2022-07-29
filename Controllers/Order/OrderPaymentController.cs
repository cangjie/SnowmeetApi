using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Order;

namespace SnowmeetApi.Controllers.Order
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderPaymentController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public OrderPaymentController(ApplicationDBContext context)
        {
            _context = context;
        }

        /*

        // GET: api/OrderPayment
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderPayment>>> GetOrderPayment()
        {
            return await _context.OrderPayment.ToListAsync();
        }

        // GET: api/OrderPayment/5
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderPayment>> GetOrderPayment(int id)
        {
            var orderPayment = await _context.OrderPayment.FindAsync(id);

            if (orderPayment == null)
            {
                return NotFound();
            }

            return orderPayment;
        }

        // PUT: api/OrderPayment/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderPayment(int id, OrderPayment orderPayment)
        {
            if (id != orderPayment.id)
            {
                return BadRequest();
            }

            _context.Entry(orderPayment).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderPaymentExists(id))
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

        // POST: api/OrderPayment
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<OrderPayment>> PostOrderPayment(OrderPayment orderPayment)
        {
            _context.OrderPayment.Add(orderPayment);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetOrderPayment", new { id = orderPayment.id }, orderPayment);
        }

        // DELETE: api/OrderPayment/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderPayment(int id)
        {
            var orderPayment = await _context.OrderPayment.FindAsync(id);
            if (orderPayment == null)
            {
                return NotFound();
            }

            _context.OrderPayment.Remove(orderPayment);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool OrderPaymentExists(int id)
        {
            return _context.OrderPayment.Any(e => e.id == id);
        }
    }
}

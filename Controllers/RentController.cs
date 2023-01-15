using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RentController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public RentController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentItem>> GetRentItem(string code, string shop)
        {
            var rentItemList = await _context.RentItem.Where(r => r.code.Trim().Equals(code.Trim())).ToListAsync();
            if (rentItemList != null && rentItemList.Count > 0)
            {
                RentItem item = rentItemList[0];
                item.rental = item.GetRental(shop);
                return Ok(item);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpPost]
        public async Task<ActionResult<RentOrder>> Recept([FromQuery]string sessionKey, [FromBody]RentOrder rentOrder)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            int orderId = 0;

            if (rentOrder.deposit_final >0)
            {
                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    type = "押金",
                    shop = rentOrder.shop.Trim(),
                    open_id = rentOrder.open_id.Trim(),
                    name = rentOrder.real_name.Trim(),
                    cell_number = rentOrder.cell_number.Trim(),
                    pay_method = rentOrder.payMethod.Trim(),
                    pay_memo = "",
                    pay_state = 0,
                    order_price = rentOrder.deposit,
                    order_real_pay_price = rentOrder.deposit_final,
                    ticket_amount = 0,
                    other_discount = 0,
                    final_price = rentOrder.deposit_final,
                    ticket_code = rentOrder.ticket_code.Trim(),
                    staff_open_id = user.miniAppOpenId.Trim(),
                    score_rate = 0,
                    generate_score = 0

                };
                await _context.AddAsync(order);
                await _context.SaveChangesAsync();

                OrderPayment payment = new OrderPayment()
                {
                    order_id = order.id,
                    pay_method = order.pay_method.Trim(),
                    amount = order.final_price,
                    status = "待支付",
                    staff_open_id = user.miniAppOpenId.Trim()
                };
                await _context.OrderPayment.AddAsync(payment);
                await _context.SaveChangesAsync();
                orderId = order.id;
            }
            rentOrder.order_id = orderId;

            await _context.RentOrder.AddAsync(rentOrder);
            await _context.SaveChangesAsync();

            for (int i = 0; i < rentOrder.details.Length; i++)
            {
                RentOrderDetail detail = rentOrder.details[i];
                detail.rent_list_id = rentOrder.id;
                await _context.RentOrderDetail.AddAsync(detail);
                await _context.SaveChangesAsync();
            }

            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
            OrderOnline newOrder = (await orderHelper.GetWholeOrderByStaff(orderId, sessionKey)).Value;

            rentOrder.order = newOrder;

            return rentOrder;
        }

        /*

        // GET: api/Rent
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RentOrder>>> GetRentOrder()
        {
            return await _context.RentOrder.ToListAsync();
        }

        // GET: api/Rent/5
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> GetRentOrder(int id)
        {
            var rentOrder = await _context.RentOrder.FindAsync(id);

            if (rentOrder == null)
            {
                return NotFound();
            }

            return rentOrder;
        }

        // PUT: api/Rent/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutRentOrder(int id, RentOrder rentOrder)
        {
            if (id != rentOrder.id)
            {
                return BadRequest();
            }

            _context.Entry(rentOrder).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RentOrderExists(id))
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

        // POST: api/Rent
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<RentOrder>> PostRentOrder(RentOrder rentOrder)
        {
            _context.RentOrder.Add(rentOrder);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetRentOrder", new { id = rentOrder.id }, rentOrder);
        }

        // DELETE: api/Rent/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRentOrder(int id)
        {
            var rentOrder = await _context.RentOrder.FindAsync(id);
            if (rentOrder == null)
            {
                return NotFound();
            }

            _context.RentOrder.Remove(rentOrder);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool RentOrderExists(int id)
        {
            return _context.RentOrder.Any(e => e.id == id);
        }
    }
}

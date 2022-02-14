using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Ticket;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Card;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Product;
namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
    [ApiController]
    public class SummerMaintainController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;

        public string _appId = "";
        public SummerMaintainController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        [HttpPost]
        public async Task<ActionResult<int>> PlaceOrder(SummerMaintain summerMaintain)
        {
            string sessionKey = summerMaintain.open_id.Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            summerMaintain.open_id = user.miniAppOpenId.Trim();
            
            int productId = 144;
            if (summerMaintain.service.Trim().Equals("代取回寄"))
            {
                productId = 145;
            }
            Product product = _context.Product.Find(productId);
            List<OrderOnlineDetail> details = new List<OrderOnlineDetail>();
            OrderOnlineDetail detail = new OrderOnlineDetail()
            {
                OrderOnlineId = 0,
                product_id = productId,
                count = 1,
                product_name = product.name,
                price = product.sale_price
            };
            double totalPrice = product.sale_price;
            details.Add(detail);

            OrderOnline order = new OrderOnline()
            {
                type = "服务卡",
                open_id = user.miniAppOpenId,
                cell_number = user.miniAppUser.cell_number.Trim(),
                name = user.miniAppUser.nick.Trim(),
                pay_method = "微信",
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                pay_state = 0,
                shop = "万龙",
                out_trade_no = "",
                ticket_code = "",
                code = ""
            };
            _context.OrderOnlines.Add(order);
            _context.SaveChanges();
            if (order.id == 0)
            {
                return null;
            }
            foreach (OrderOnlineDetail d in details)
            {
                d.OrderOnlineId = order.id;
                _context.OrderOnlineDetails.Add(d);
                _context.SaveChanges();
            }

            summerMaintain.order_id = order.id;
            _context.SummerMaintain.Add(summerMaintain);
            await _context.SaveChangesAsync();

            return order.id;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<SummerMaintain>>> GetMySummerMaintain(string sessionKey)
        { 
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);

            List<SummerMaintain> summerMaintainList = await _context.SummerMaintain
                .Where(s => (s.open_id.Trim().Equals(user.miniAppOpenId.Trim()) && !s.state.Trim().Equals("未支付")))
                .OrderByDescending(s=>s.id).ToListAsync();


            return summerMaintainList;
        }

        /*
        // GET: api/SummerMaintain
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SummerMaintain>>> GetSummerMaintain()
        {
            return await _context.SummerMaintain.ToListAsync();
        }

        // GET: api/SummerMaintain/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SummerMaintain>> GetSummerMaintain(int id)
        {
            var summerMaintain = await _context.SummerMaintain.FindAsync(id);

            if (summerMaintain == null)
            {
                return NotFound();
            }

            return summerMaintain;
        }

        // PUT: api/SummerMaintain/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSummerMaintain(int id, SummerMaintain summerMaintain)
        {
            if (id != summerMaintain.id)
            {
                return BadRequest();
            }

            _context.Entry(summerMaintain).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SummerMaintainExists(id))
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

        // POST: api/SummerMaintain
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SummerMaintain>> PostSummerMaintain(SummerMaintain summerMaintain)
        {
            _context.SummerMaintain.Add(summerMaintain);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSummerMaintain", new { id = summerMaintain.id }, summerMaintain);
        }

        // DELETE: api/SummerMaintain/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSummerMaintain(int id)
        {
            var summerMaintain = await _context.SummerMaintain.FindAsync(id);
            if (summerMaintain == null)
            {
                return NotFound();
            }

            _context.SummerMaintain.Remove(summerMaintain);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool SummerMaintainExists(int id)
        {
            return _context.SummerMaintain.Any(e => e.id == id);
        }
    }
}

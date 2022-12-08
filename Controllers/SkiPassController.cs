using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Card;
using Newtonsoft.Json;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class SkiPassController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public SkiPassController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetSkiPassProduct(string resort, DateTime date, string tags)
        {
            string[] tagArr = tags == null ? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');

            var skiPassProdustList = await _context.Product.Where(p => (p.shop.Trim().Equals(resort.Trim()) && p.hidden == 0))
                .Join(_context.SkiPass, p => p.id, s => s.product_id,
                (p, s) => new {
                    p.id,
                    p.name,
                    p.sale_price,
                    p.deposit,
                    s.product_id,
                    s.resort,
                    s.end_sale_time,
                    s.rules,
                    s.available_days,
                    s.unavailable_days,
                    s.tags
                })

                .OrderBy(p => p.sale_price).ToListAsync();


            for (int i = 0; i < skiPassProdustList.Count; i++)
            {
                var r = skiPassProdustList[i];
                SkiPass skiPass = new SkiPass()
                {
                    product_id = r.product_id,
                    resort = r.resort.Trim(),
                    end_sale_time = r.end_sale_time,
                    rules = r.rules,
                    available_days = r.available_days,
                    unavailable_days = r.unavailable_days,
                    tags = r.tags

                };

                if (!skiPass.DateMatch(date) || !skiPass.TagMatch(tagArr))
                {
                    skiPassProdustList.RemoveAt(i);
                    i--;
                }


            }


            return skiPassProdustList;
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date, int count, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Product product = await _context.Product.FindAsync(productId);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (user == null || product == null)
            {
                return BadRequest();
            }
            return (await CreateSkiPassOrder(new Product[] { product }, user, null, date, count));
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> PlaceSkiPassOrderNanshan(int productId, DateTime date, int count, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            Product productTicket = await _context.Product.FindAsync(productId);
            if (user == null || !user.isAdmin || productTicket == null)
            {
                return BadRequest();
            }
            Product productService = await _context.Product.FindAsync(297);
            return (await CreateSkiPassOrder(new Product[] { productTicket, productService }, null, user, date, count));
        }


        [NonAction]
        public async Task<OrderOnline> CreateSkiPassOrder(Product[] prodctArr, UnicUser? user, UnicUser? staff, DateTime date, int count)
        {
            double totalPrice = 0;
            string openId = "";
            string staffOpenId = "";
            bool needRent = false;

            if (user != null)
            {
                openId = user.miniAppOpenId.Trim();
            }
            if (staff != null)
            {
                staffOpenId = staff.miniAppOpenId.Trim();
            }
            for (int i = 0; i < prodctArr.Length; i++)
            {
                totalPrice = totalPrice + prodctArr[i].sale_price * count;
                if (prodctArr[i].name.IndexOf("租") >= 0)
                {
                    needRent = true;
                }
                
            }
            OrderOnline order = new OrderOnline()
            {
                type = "雪票",
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                final_price = totalPrice,
                open_id = openId.Trim(),
                staff_open_id = staffOpenId.Trim(),
                memo = "{ \"use_date\": \"" + date.ToShortDateString() + "\" \"rent\" : \"" + (needRent? "1" : "0") + "\"}"
            };
            await _context.AddAsync(order);
            await _context.SaveChangesAsync();
            if (order.id > 0)
            {
                for (int i = 0; i < prodctArr.Length; i++)
                {
                    OrderOnlineDetail detail = new OrderOnlineDetail()
                    {
                        OrderOnlineId = order.id,
                        product_id = prodctArr[i].id,
                        count = count,
                        product_name = prodctArr[i].name.Trim(),
                        retail_price = prodctArr[i].sale_price,
                        price = prodctArr[i].sale_price
                    };
                    await _context.AddAsync(detail);
                    await _context.SaveChangesAsync();
                }
                
            }

            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = order.pay_method.Trim(),
                amount = order.final_price,
                status = "待支付",
                staff_open_id = (staff != null) ? staff.miniAppOpenId : ""
            };
            await _context.OrderPayment.AddAsync(payment);
            await _context.SaveChangesAsync();
            order.payments = new OrderPayment[] { payment };
            return order;
        }

        [NonAction]
        public async Task<ActionResult<string>> CreateSkiPass(OrderOnline order)
        {
            //OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            string memo = order.memo.Trim();
            try
            {
                var objMemo = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(memo);
                DateTime reserveDate = objMemo["use_date"];
                CardController cardHelper = new CardController(_context, _config);
                string code = cardHelper.CreateCard("雪票");
                Card card = await _context.Card.FindAsync(code);
                card.use_date = reserveDate;
                card.used = 0;
                order.code = code;
                _context.Entry(card).State = EntityState.Modified;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return code;
            }
            catch
            {

            }

            return "";
        }

        /*
        // GET: api/SkiPass
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SkiPass>>> GetSkiPass()
        {
            return await _context.SkiPass.ToListAsync();
        }

        // GET: api/SkiPass/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SkiPass>> GetSkiPass(int id)
        {
            var skiPass = await _context.SkiPass.FindAsync(id);

            if (skiPass == null)
            {
                return NotFound();
            }

            return skiPass;
        }

        // PUT: api/SkiPass/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSkiPass(int id, SkiPass skiPass)
        {
            if (id != skiPass.product_id)
            {
                return BadRequest();
            }

            _context.Entry(skiPass).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SkiPassExists(id))
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

        // POST: api/SkiPass
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SkiPass>> PostSkiPass(SkiPass skiPass)
        {
            _context.SkiPass.Add(skiPass);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSkiPass", new { id = skiPass.product_id }, skiPass);
        }

        // DELETE: api/SkiPass/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSkiPass(int id)
        {
            var skiPass = await _context.SkiPass.FindAsync(id);
            if (skiPass == null)
            {
                return NotFound();
            }

            _context.SkiPass.Remove(skiPass);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool SkiPassExists(int id)
        {
            return _context.SkiPass.Any(e => e.product_id == id);
        }
    }
}

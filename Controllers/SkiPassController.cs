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

        [HttpGet("{id}")]
        public async Task<ActionResult<int>> CheckNanshanReserveAvaliabelCount(int id, DateTime date)
        {
            Product p = await _context.Product.FindAsync(id);
            if (!p.shop.Trim().Equals("南山"))
            {
                return int.MaxValue;
            }
            bool isEvening = false;
            if (p.name.IndexOf("夜场") >= 0 && p.name.IndexOf("下午") < 0)
            {
                isEvening = true;
            }
            int totalCount = 20;
            if (isEvening)
            {
                totalCount = 15;
            }
            int reserveCount = 0;

            var orderList = await _context.OrderOnlineDetails
                .Join(_context.OrderOnlines, d => d.OrderOnlineId, o => o.id, (d, o) => new { o.id, o.pay_state, o.code, o.memo, d.count, d.product_id })
                .Where(o => o.product_id != 297 && o.pay_state == 1 && o.memo.IndexOf(date.ToShortDateString()) > 0)
                .Join(_context.Product, o => o.product_id, p => p.id, (o, p) => new { o.id, o.pay_state, o.code, o.count, o.product_id, p.name, o.memo })
                .ToListAsync();
            for (int i = 0; i < orderList.Count; i++)
            {
                var order = orderList[i];
                if (isEvening && order.name.IndexOf("夜场") >= 0 && order.name.IndexOf("下午") < 0)
                {
                    reserveCount++;
                }
                if (!isEvening && (order.name.IndexOf("夜场") < 0 || order.name.IndexOf("下午") >= 0))
                {
                    reserveCount++;
                }
            }

            return totalCount - reserveCount;
        }


        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetSkiPassDetailInfo(int id)
        {
            return await _context.Product.Where(p => p.id == id)
                .Join(_context.SkiPass, p => p.id, s => s.product_id,
                (p, s) => new
                {
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
                }).FirstAsync();
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetSkiPassProduct(string resort, DateTime date, string tags)
        {

            if (date >= DateTime.Parse("2022-12-31") && date <= DateTime.Parse("2023-1-2"))
            {
                tags = "节假日";
            }


            string[] tagArr = tags == null ? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');

            


            var skiPassProdustList = await _context.Product.Where(p => (p.shop.Trim().Equals(resort.Trim()) && p.hidden == 0))
                .Join(_context.SkiPass, p => p.id, s => s.product_id,
                (p, s) => new {
                    p.id,
                    p.name,
                    p.sale_price,
                    p.deposit,
                    p.sort,
                    s.product_id,
                    s.resort,
                    s.end_sale_time,
                    s.rules,
                    s.available_days,
                    s.unavailable_days,
                    s.tags
                })

                .OrderBy(p => p.sort).ToListAsync();


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
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date, int count, string cell, string name, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Product product = await _context.Product.FindAsync(productId);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (user == null || product == null)
            {
                return BadRequest();
            }
            OrderOnline order = (await CreateSkiPassOrder(new Product[] { product }, user, null, date, count));

            order.cell_number = cell.Trim();
            order.name = name.Trim();
            _context.Entry(order).State = EntityState.Modified;
            MiniAppUser miniUser = await _context.MiniAppUsers.FindAsync(order.open_id.Trim());
            if (miniUser != null)
            {
                if (miniUser.real_name.Trim().Length <= 1)
                {
                    miniUser.real_name = name;
                }
                if (miniUser.cell_number.Length != 11)
                {
                    miniUser.cell_number = cell.Trim();
                }
                _context.Entry(miniUser).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            return order;
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
                totalPrice = totalPrice + (prodctArr[i].deposit + prodctArr[i].sale_price) * count;
                if (prodctArr[i].name.IndexOf("租") >= 0)
                {
                    needRent = true;
                }
                
            }
            OrderOnline order = new OrderOnline()
            {
                type = "雪票",
                shop = prodctArr[0].shop.Trim(),
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                final_price = totalPrice,
                open_id = openId.Trim(),
                staff_open_id = staffOpenId.Trim(),
                memo = "{ \"use_date\": \"" + date.Year.ToString() + "-" + date.Month.ToString().PadLeft(2, '0') + "-" + date.Day.ToString().PadLeft(2, '0') + "\", \"rent\" : \"" + (needRent? "1" : "0") + "\"}"
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
                var objMemo = JsonConvert.DeserializeObject<Dictionary<string, object>>(memo);
                DateTime reserveDate = DateTime.Parse(objMemo["use_date"].ToString());
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
            catch(Exception err)
            {
                Console.WriteLine(err.ToString());
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

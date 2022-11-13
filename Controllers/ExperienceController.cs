using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Order;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ExperienceController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration _originConfig;
        private readonly IHttpContextAccessor _httpContextAccessor;


        public ExperienceController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _originConfig = config;
            _httpContextAccessor = httpContextAccessor;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Experience>>> GetExperiencesTemp(string sessionKey)
        {
            DateTime currentSeasonStartDate = DateTime.Parse("2022-10-1");
            var expList = await _context.Experience.Where(e => (e.create_date >= currentSeasonStartDate) )
                .OrderByDescending(e => e.id).ToListAsync();
            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _originConfig);
            for (int i = 0; i < expList.Count; i++)
            {
                Experience exp = expList[i];
                bool paid = false;
                if (exp.guarantee_order_id > 0)
                {
                    OrderOnline order = (await orderHelper.GetOrderOnline(exp.guarantee_order_id, sessionKey)).Value;
                    if (order != null && order.payments.Length > 0 && order.payments[0].status.Trim().Equals("支付成功"))
                    {
                        paid = true;
                        exp.order = order;
                    }
                }
                if (!paid)
                {
                    expList.Remove(exp);
                    i--;
                }
            }
            return expList;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Experience>> GetExperienceTemp(int id, string sessionKey)
        {
            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _originConfig);
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            Experience exp = await _context.Experience.FindAsync(id);
            if (!user.isAdmin && !exp.open_id.Trim().Equals("") && exp.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                return BadRequest();
            }
            exp.order = (await orderHelper.GetOrderOnline(exp.guarantee_order_id, sessionKey)).Value;
            return exp;
        }

        [HttpPost]
        public async Task<ActionResult<Experience>> PlaceOrder(Experience experience, string sessionKey)
        {
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            experience.open_id = "";
            experience.staff_open_id = user.miniAppOpenId;
            await _context.Experience.AddAsync(experience);
            await _context.SaveChangesAsync();
            OrderOnline order = (await PlaceOrder(experience.id, sessionKey)).Value;
            experience.order = order;
            return experience;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrderOnline>> PlaceOrder(int id, string sessionKey)
        {
            if (ExperienceExists(id))
            {
                Experience exp = await _context.Experience.FindAsync(id);
                sessionKey = Util.UrlDecode(sessionKey);
                //
                UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
                if (!user.isAdmin)
                {
                    return BadRequest();
                }
                string openId = "";
                if (user == null && user.miniAppOpenId == null && user.miniAppOpenId.Trim().Equals("")
                    && user.officialAccountOpenId == null && user.officialAccountOpenId.Trim().Equals(""))
                {
                    return null;
                }
                else if (user.miniAppOpenId == null || user.miniAppOpenId.Trim().Equals(""))
                {
                    openId = user.officialAccountOpenId.Trim();
                }
                else
                {
                    openId = user.miniAppOpenId.Trim();
                }
                if (!exp.open_id.Trim().Equals(""))
                {
                    if (exp.guarantee_order_id > 0)
                    {
                        OrderOnline validOrder = _context.OrderOnlines.Find(exp.guarantee_order_id);
                        if (validOrder.pay_state != 0)
                        {
                            return NotFound();
                        }
                    }
                }
                string ticketCode = "";
                if (exp.ticket_code != null)
                {
                    ticketCode = exp.ticket_code.Trim();
                }
                OrderOnline order = new OrderOnline()
                {
                    type = "押金",
                    open_id = "",
                    cell_number = exp.cell_number.Trim(),
                    name = "",
                    pay_method = "微信支付",
                    order_price = exp.guarantee_cash,
                    order_real_pay_price = exp.guarantee_cash,
                    pay_state = exp.guarantee_cash==0?1:0,
                    shop = exp.shop.Trim(),
                    out_trade_no = "",
                    ticket_code = ticketCode.Trim(),
                    ticket_amount = 0,
                    other_discount = 0,
                    final_price = exp.guarantee_cash,
                    code = ""
                };
                _context.OrderOnlines.Add(order);
                _context.SaveChanges();
                if (order.id == 0)
                {
                    return NotFound();
                }
                Product product = await _context.Product.FindAsync(147);
                OrderOnlineDetail detail = new OrderOnlineDetail()
                {
                    OrderOnlineId = order.id,
                    product_id = product.id,
                    count = (int) (exp.guarantee_cash/product.sale_price) ,
                    product_name = product.name.Trim(),
                    price = product.sale_price
                };
                _context.OrderOnlineDetails.Add(detail);
                try
                {
                    _context.SaveChanges();
                }
                catch
                {

                }
                exp.guarantee_order_id = order.id;
                exp.open_id = openId.Trim();
                _context.Entry(exp).State = EntityState.Modified;
                try
                {
                    _context.SaveChanges();
                }
                catch
                {

                }

                OrderPayment payment = new OrderPayment()
                {
                    open_id = "",
                    amount = order.final_price,
                    pay_method = order.pay_method,
                    status = "待支付",
                    order_id = order.id,
                    staff_open_id = user.miniAppOpenId.Trim()
                };
                await _context.OrderPayment.AddAsync(payment);
                await _context.SaveChangesAsync();

                OrderOnlinesController orderController = new OrderOnlinesController(_context, _originConfig);

                OrderOnline orderRet = (await orderController.GetOrderOnline(order.id, sessionKey)).Value;

               
                return orderRet;
            }
            return NoContent();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Experience>> GetExperience(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            var experience = await _context.Experience.FindAsync(id);

            if (experience.guarantee_order_id > 0)
            {
                experience.order = await _context.OrderOnlines.FindAsync(experience.guarantee_order_id);
            }

            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;

            try
            {
                if (!user.isAdmin && !experience.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
                {
                    return NoContent();
                }
            }
            catch
            {
                return NoContent();
            }


            if (experience == null)
            {
                return NotFound();
            }

            return experience;
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutExperience(int id, string sessionKey, Experience experience)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (id != experience.id)
            {
                return BadRequest();
            }

            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;

            try
            {
                if (!user.isAdmin && !experience.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
                {
                    return NoContent();
                }
            }
            catch
            {
                return NoContent();
            }

            _context.Entry(experience).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ExperienceExists(id))
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

        [HttpGet("{id}")]
        public async Task<ActionResult<Experience>> Refund(int id, double amount, string sessionKey, string memo)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            memo = Util.UrlDecode(memo);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            Experience exp = (await GetExperienceTemp(id, sessionKey)).Value;
            exp.return_memo = memo.Trim();
            _context.Entry(exp).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            if ((exp.order.refunds == null || exp.order.refunds.Length == 0) && exp.order.payments != null && exp.order.paidAmount >= amount )
            {
                Order.OrderRefundController refundHelper = new Order.OrderRefundController(_context, _originConfig, _httpContextAccessor);
                for (int i = 0; i < exp.order.payments.Length; i++)
                {
                    OrderPayment payment = exp.order.payments[i];
                    if (payment.amount >= amount)
                    {
                        await refundHelper.TenpayRefund(payment.id, amount, sessionKey);
                        break;
                    }
                }

            }
            exp = (await GetExperienceTemp(id, sessionKey)).Value;
            return exp;
        }

        /*

        // GET: api/Experience
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Experience>>> GetExperience()
        {
            return await _context.Experience.ToListAsync();
        }

        // GET: api/Experience/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Experience>> GetExperience(int id)
        {
            var experience = await _context.Experience.FindAsync(id);

            if (experience == null)
            {
                return NotFound();
            }

            return experience;
        }

        // PUT: api/Experience/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutExperience(int id, Experience experience)
        {
            if (id != experience.id)
            {
                return BadRequest();
            }

            _context.Entry(experience).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ExperienceExists(id))
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

        // POST: api/Experience
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Experience>> PostExperience(Experience experience)
        {
            _context.Experience.Add(experience);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetExperience", new { id = experience.id }, experience);
        }

        // DELETE: api/Experience/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteExperience(int id)
        {
            var experience = await _context.Experience.FindAsync(id);
            if (experience == null)
            {
                return NotFound();
            }

            _context.Experience.Remove(experience);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */

        private bool ExperienceExists(int id)
        {
            return _context.Experience.Any(e => e.id == id);
        }
    }
}

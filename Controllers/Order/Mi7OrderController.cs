using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
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
        public async Task<ActionResult<List<SaleReport>>> GetSaleReport(DateTime startDate, DateTime endDate, string sessionKey, string shop = "")
        {
            MemberController _memberHelper = new MemberController(_context, _config);
            List<Mi7Order> miList = await _context.mi7Order
                .Include(m => m.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => (r.state == 1 || !r.refund_id.Trim().Equals(""))))
                .Where(m => (m.order != null && m.order.pay_state == 1 
                && ((DateTime)m.order.pay_time).Date >= startDate.Date && ((DateTime)m.order.pay_time).Date <= endDate.Date))
                .OrderByDescending(m => m.order.create_date).AsNoTracking().ToListAsync();
            List<SaleReport> ret = new List<SaleReport>();
            for(int i = 0; i < miList.Count; i++)
            {
                Mi7Order mi7Order = miList[i];
                Models.Users.Member customer = await _memberHelper.GetMember(mi7Order.order.open_id.Trim(), "wechat_mini_openid");
                Models.Users.Member staff = await _memberHelper.GetMember(mi7Order.order.staff_open_id, "wechat_mini_openid");
                
                //try
                //{
                SaleReport r = new SaleReport()
                {
                    mi7_order_id = mi7Order.mi7_order_id == null? "" : mi7Order.mi7_order_id.Trim(),
                    barCode = mi7Order.barCode.Trim(),
                    sale_price = mi7Order.sale_price,
                    real_charge = mi7Order.real_charge,
                    order_id = mi7Order.order_id,
                    name = customer == null? "" : customer.title.Trim(),
                    cell_number = customer == null || customer.cell == null ? "" : customer.cell.Trim(),
                    final_price = mi7Order.order.paidAmount,
                    refund_price = mi7Order.order.refundAmount,
                    shop = mi7Order.order.shop,
                    staff = staff == null? "" : staff.real_name,
                    pay_time = mi7Order.order.pay_time,
                    pay_method = mi7Order.order != null && mi7Order.order.paymentList.Count > 0  ?  mi7Order.order.paymentList[0].pay_method.Trim() : ""
                };
                ret.Add(r);
                /*
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                */
            }
            return Ok(ret);
        }



        [HttpGet]
        public async Task<ActionResult<IEnumerable<SaleReport>>> GetSaleReportTest(DateTime startDate, DateTime endDate, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
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
                + (shop.Equals("")? "  " : (" and order_online.shop  = '" + shop.Trim().Replace("'", "") + "'  "))
                + " order by order_id desc ")
                .AsNoTracking().ToListAsync();
            return Ok(l);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Mi7Order>> GetMi7Order(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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

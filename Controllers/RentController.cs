using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Asn1.X509;
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

            rentOrder.staff_open_id = user.miniAppOpenId.Trim();
            rentOrder.staff_name = user.miniAppUser.real_name.Trim();

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

        [HttpGet]
        public async Task<ActionResult<RentOrder[]>> GetRentOrderListByStaff(string shop,
            DateTime start, DateTime end, string status, string sessionKey)
        {
            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
            if (shop == null)
            {
                shop = "";
            }
            if (status != null)
            {
                status = Util.UrlDecode(status.Trim());
            }
            shop = Util.UrlDecode(shop.Trim());
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrder[] orderArr = await _context.RentOrder
                .Where(o => (o.start_date >= start && o.start_date < end.Date.AddDays(1)  && (shop.Trim().Equals("") || o.shop.Trim().Equals(shop)))).ToArrayAsync();
            for (int i = 0; i < orderArr.Length; i++)
            {
                RentOrder order = orderArr[i];
                order.details = await _context.RentOrderDetail.Where(d => d.rent_list_id == order.id).ToArrayAsync();
                order.order = (await orderHelper.GetWholeOrderByStaff(order.order_id, sessionKey)).Value;
                orderArr[i] = order;
            }
            if (status == null)
            {
                return Ok(orderArr);
            }
            else
            {
                List<RentOrder> newArr = new List<RentOrder>();
                for (int i = 0; i < orderArr.Length; i++)
                {
                    if (orderArr[i].status.Trim().Equals(status))
                    {
                        newArr.Add(orderArr[i]);
                    }
                }
                return Ok(newArr.ToArray());
            }

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> GetRentOrder(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;

            RentOrder rentOrder = await _context.RentOrder.FindAsync(id);
            if (rentOrder == null)
            {
                return NotFound();
            }
            if (!user.isAdmin && !rentOrder.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                return BadRequest();
            }
            rentOrder.details = await _context.RentOrderDetail
                .Where(d => d.rent_list_id == rentOrder.id).ToArrayAsync();
            if (rentOrder.order_id > 0)
            {
                OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
                rentOrder.order = (await orderHelper.GetWholeOrderByStaff(rentOrder.order_id, sessionKey)).Value;
            }

            if (!user.isAdmin)
            {
                rentOrder.open_id = "";
                if (rentOrder.order != null)
                {
                    rentOrder.order.open_id = "";
                }
            }
            DateTime nowDate = DateTime.Now;
            for (int i = 0; i < rentOrder.details.Length; i++)
            {
                RentOrderDetail detail = rentOrder.details[i];
                DateTime endTime = DateTime.Now;
                if (detail.real_end_date != null)
                {
                    endTime = (DateTime)detail.real_end_date;
                }

                if (rentOrder.start_date.Hour >= 16 && rentOrder.start_date.Date == endTime.Date)
                {
                    detail.overTime = false;
                }
                else if (endTime.Hour >= 18)
                {
                    detail.overTime = true;
                }
                else
                {
                    detail.overTime = false;

                }

                //if (rentOrder.start_date.Hour >= 16 && )

                switch (rentOrder.shop.Trim())
                {
                    case "南山":
                        TimeSpan ts = nowDate - rentOrder.start_date;
                        
                        if (ts.Hours < 4)
                        {
                            detail._suggestRental = detail.unit_rental;
                            detail._timeLength = "1场";
                        }
                        else if (nowDate.Hour > 8)
                        {
                            detail._suggestRental = detail.unit_rental * 1.5;
                            detail._timeLength = "1.5场";
                        }
                        else
                        {
                            detail._suggestRental = detail.unit_rental;
                            detail._timeLength = "1场";
                        }
                        
                        break;
                    default:
                        TimeSpan ts1 = nowDate - rentOrder.start_date;
                        int days = ts1.Days == 0 ? 1 : ts1.Days;
                        detail._suggestRental = detail.unit_rental;
                        detail._timeLength = days.ToString() + "天";

                        break;
                }
            }
            var ret = Ok(rentOrder);
            return ret;

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetReturn(int id, float rental,
            double reparation, DateTime returnDate, string memo, string sessionKey, double overTimeCharge = 0)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            detail.real_end_date = returnDate;
            detail.real_rental = rental;
            detail.reparation = reparation;
            detail.memo = memo.Trim();
            detail.overtime_charge = overTimeCharge;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(detail);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> Refund(int id, double amount,
            double rentalReduce, double rentalReduceTicket, string memo, string sessionKey)
        {
            
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }

            memo = Util.UrlDecode(memo);
            sessionKey = Util.UrlDecode(sessionKey);
            amount = Math.Round(amount, 2);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            rentOrder.memo = memo;
            rentOrder.refund = amount;
            rentOrder.end_date = DateTime.Now;
            rentOrder.rental_reduce = rentalReduce;
            rentOrder.rental_reduce_ticket = rentalReduceTicket;
            _context.Entry(rentOrder).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            if (amount > 0 && rentOrder.order_id > 0 && rentOrder.order != null && rentOrder.order.pay_method.Trim().Equals("微信支付")
                && rentOrder.order.payments != null && rentOrder.order.payments.Length > 0)
            {
                OrderPayment payment = rentOrder.order.payments[0];
                Order.OrderRefundController refundHelper = new Order.OrderRefundController(
                    _context, _oriConfig, _httpContextAccessor);
                double paidAmount = payment.amount;
                if (paidAmount >= amount)
                {
                    await refundHelper.TenpayRefund(payment.id, amount, sessionKey);
                }
            }

            return Ok(rentOrder);


        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>>SetPaid(int id, string sessionKey)
        {
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            if (rentOrder == null || rentOrder.order == null
                || rentOrder.order.payments == null || rentOrder.order.payments.Length <= 0)
            {
                return NotFound();
            }
            OrderPayment payment = rentOrder.order.payments[0];
            OrderOnline order = rentOrder.order;
            payment.status = "支付成功";
            order.pay_state = 1;
            _context.Entry(payment).State = EntityState.Modified;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(rentOrder);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> Bind(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }
            if (rentOrder.open_id.Trim().Equals(""))
            {
                rentOrder.open_id = user.miniAppOpenId;
                _context.Entry(rentOrder).State = EntityState.Modified;
            }
            if (rentOrder.order != null && rentOrder.open_id.Trim().Equals(""))
            {
                OrderOnline order = rentOrder.order;
                order.open_id = user.miniAppOpenId.Trim();
                _context.Entry(order).State = EntityState.Modified;
                if (order.payments != null && order.payments.Length > 0)
                {
                    OrderPayment pay = order.payments[0];
                    if (pay.open_id.Trim().Equals(""))
                    {
                        pay.open_id = user.miniAppOpenId.Trim();
                        _context.Entry(pay).State = EntityState.Modified;
                    }
                    
                }
            }
            await _context.SaveChangesAsync();
            return Ok(rentOrder);
        }

        


        /*
        [HttpGet]
        public async Task<ActionResult<DailyReport[]>> GetCurrentSeasonAllRentOrder(string sessionKey, DateTime seasonStart, DateTime currentDate)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<RentOrder> orderList = await _context.RentOrder
                .Where(o => o.create_date.Date >= seasonStart.Date && o.create_date.Date <= currentDate.Date)
                .ToListAsync();

            for (int i = 0; i < orderList.Count; i++)
            {
                orderList[i] = (await GetRentOrder(orderList[i].id, sessionKey)).Value;
            }

            


            return Ok(new DailyReport[] { });
        }
        */
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

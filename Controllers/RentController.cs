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
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using System.Collections;
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

        private readonly DateTime startDate = DateTime.Parse("2023-10-20");

        public class Balance
        {
            public int id { get; set; }
            public string shop { get; set; }
            public string name { get; set; } = "";
            public string cell { get; set; } = "";
            public DateTime? settleDate { get; set; }
            public double deposit { get; set; } = 0;
            public double refund { get; set; } = 0;
            public double earn { get; set; } = 0;
            public string staff { get; set; } = "";
        }

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
                if (item.rental == 0)
                {
                    item.rental = item.GetRental(shop);
                }
                //item.rental_reserve = item.rental_member;
                return Ok(item);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet]
        public async Task<ActionResult<List<Balance>>> GetBalance(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            var idList = await _context.idList.FromSqlRaw(" select distinct rent_list_id as id from rent_list_detail  "
                + " where real_end_date >= '" + startDate.ToShortDateString() + "' "
                + " and real_end_date <= '" + endDate.AddDays(1).ToShortDateString() + "' ")
                .AsNoTracking().ToListAsync();
            List<Balance> bList = new List<Balance>();
            for (int i = 0; i < idList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(idList[i].id, sessionKey)).Result).Value;
                if (!order.status.Trim().Equals("已退款"))
                {
                    continue;
                }
                double totalPayment = 0;
                double totalRefund = 0;
                for (int j = 0; j < order.order.payments.Length; j++)
                {
                    totalPayment += order.order.payments[j].amount;
                }
                for (int j = 0; j < order.order.refunds.Length; j++)
                {
                    if (!order.order.refunds[j].refund_id.Trim().Equals("") || order.order.refunds[j].state == 1)
                    {
                        totalRefund += order.order.refunds[j].amount;
                    }
                }
                Balance b = new Balance()
                {
                    id = order.id,
                    shop = order.shop,
                    name = order.real_name.Trim(),
                    cell = order.cell_number.Trim(),
                    settleDate = order.end_date,
                    deposit = totalPayment,
                    refund = totalRefund,
                    earn = totalPayment - totalRefund,
                    staff = order.staff_name
                };
                try
                {
                    if (b.settleDate >= startDate && ((DateTime)b.settleDate).Date <= endDate.Date)
                    {
                        bList.Add(b);
                    }
                }
                catch
                {

                }
                
            }
            return Ok(bList);
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

            MiniAppUser customerUser = await _context.MiniAppUsers.FindAsync(rentOrder.open_id);

            if (customerUser != null)
            {
                if (customerUser.real_name.Trim().Equals(""))
                {
                    string realName = rentOrder.real_name.Replace("先生", "").Replace("女士", "").Trim();
                    string gender = "";
                    if (rentOrder.real_name.Replace(realName, "").IndexOf("先生") >= 0)
                    {
                        gender = "男";
                    }
                    else if (rentOrder.real_name.Replace(realName, "").IndexOf("女士") >= 0)
                    {
                        gender = "女";
                    }
                    customerUser.real_name = realName;
                    customerUser.gender = gender;
                    _context.Entry(customerUser).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
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
                /*
                if (detail.deposit_type.Trim().Equals("立即租赁"))
                {
                    detail.start_date = DateTime.Now;
                }
                */
                detail.rent_staff = user.miniAppOpenId.Trim();
                detail.return_staff = "";
                detail.rent_list_id = rentOrder.id;
                await _context.RentOrderDetail.AddAsync(detail);
                await _context.SaveChangesAsync();
            }

            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
            OrderOnline newOrder = (await orderHelper.GetWholeOrderByStaff(orderId, sessionKey)).Value;

            rentOrder.order = newOrder;

            return rentOrder;
        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<RentOrder[]>> GetRentOrderListByCell(string cell, string sessionKey, string status = "", string shop = "")
        {
            cell = cell.Trim();
            if (cell.Length < 4)
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop).Trim();
            status = Util.UrlDecode(status).Trim();
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            int orderId = 0;
            if (cell.Trim().Length == 4)
            {
                try
                {
                    orderId = int.Parse(cell);
                }
                catch
                {

                }
            }

            var orderListTemp = await _context.RentOrder
                .Where(o => ((o.cell_number.EndsWith(cell) || o.id == orderId ) && (shop.Equals("") || o.shop.Trim().Equals(shop)) )
                && o.create_date.Date > DateTime.Parse("2023-11-5")    )
                .OrderByDescending(o => o.id).ToListAsync();
            if (orderListTemp == null || orderListTemp.Count <= 0)
            {
                return NotFound();
            }

            List<RentOrder> orderList = new List<RentOrder>();
            for (int i = 0; i < orderListTemp.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderListTemp[i].id, sessionKey)).Result).Value;
                if (status.Equals("") || order.status.Trim().Equals(status))
                {
                    orderList.Add(order);
                }
            }
            return Ok(orderList.ToArray<RentOrder>());
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
                .Where(o => (o.create_date >= start && o.create_date < end.Date.AddDays(1)  && (shop.Trim().Equals("") || o.shop.Trim().Equals(shop))))
                .OrderByDescending(o => o.id).ToArrayAsync();
            for (int i = 0; i < orderArr.Length; i++)
            {
                try
                {
                   
                    RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderArr[i].id, sessionKey)).Result).Value;
                    orderArr[i] = order;
                }
                catch 
                { 
                
                }
                
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
                    try
                    {
                        if (orderArr[i].status.Trim().Equals(status))
                        {
                            newArr.Add(orderArr[i]);
                        }
                    }
                    catch
                    { 
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
                .Where(d => d.rent_list_id == rentOrder.id).AsNoTracking().ToArrayAsync();
            if (rentOrder.order_id > 0)
            {
                OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
                rentOrder.order = (OrderOnline)((OkObjectResult)(await orderHelper.GetWholeOrderByStaff(rentOrder.order_id, sessionKey)).Result).Value;
            }

            if (!user.isAdmin)
            {
                rentOrder.open_id = "";
                if (rentOrder.order != null)
                {
                    rentOrder.order.open_id = "";
                }
            }
            bool allReturned = true;
            DateTime returnTime = rentOrder.create_date;
            for (int i = 0; i < rentOrder.details.Length; i++)
            {
                DateTime endDate = DateTime.Now;
                RentOrderDetail detail = rentOrder.details[i];
                if (detail.real_end_date != null)
                {
                    endDate = (DateTime)detail.real_end_date;
                }
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

                if (!detail.rent_staff.Trim().Equals(""))
                {
                    detail.rentStaff = await _context.MiniAppUsers.FindAsync(detail.rent_staff);
                    rentOrder.staff_name = detail.rentStaff.real_name;
                }
                else
                {
                    detail.rentStaff = null;

                }

                if (!detail.return_staff.Trim().Equals(""))
                {
                    detail.returnStaff = await _context.MiniAppUsers.FindAsync(detail.return_staff);
                }
                else
                {
                    detail.returnStaff = null;
                }

                if (!detail.rentStatus.Trim().Equals("已归还"))
                {
                    allReturned = false;
                }
                else
                {
                    if (detail.real_end_date != null)
                    {
                        returnTime = detail.real_end_date > returnTime ? (DateTime)detail.real_end_date : returnTime;
                    }
                }
                

                //if (rentOrder.start_date.Hour >= 16 && )

                switch (rentOrder.shop.Trim())
                {
                    case "南山":
                        TimeSpan ts = endDate - rentOrder.start_date;
                        
                        if (ts.Hours < 4)
                        {
                            detail._suggestRental = detail.unit_rental;
                            detail._timeLength = "1场";
                        }
                        else if (endDate.Hour > 8)
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

                        if (rentOrder.start_date.Date == endDate.Date && rentOrder.start_date.Hour >= 16)
                        {
                            detail._suggestRental = detail.unit_rental;
                            detail._timeLength = "夜场";
                        }
                        else 
                        {
                            if (detail.start_date == null)
                            {
                                detail._timeLength = "--";
                            }
                            else
                            {
                                TimeSpan ts1 = endDate.Date - ((DateTime)detail.start_date).Date;
                                int days = ts1.Days;
                                if (rentOrder.start_date.Hour < 16)
                                {
                                    days++;
                                }
                                detail._suggestRental = detail.unit_rental * days;
                                detail._timeLength = days.ToString() + "天";
                            }

                        }
                        //
                        

                        break;
                }
            }

            if (allReturned && rentOrder.order != null && !rentOrder.order.pay_method.Trim().Equals("微信支付")
                && rentOrder.end_date == null)
            {
                rentOrder.end_date = returnTime;
                _context.RentOrder.Entry(rentOrder).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            if (rentOrder.staff_name.Trim().Equals(""))
            {
                rentOrder.staff_name = rentOrder.order == null? "" :  rentOrder.order.staffName.Trim();
            }
            if (rentOrder.staff_name.Trim().Equals(""))
            {
                var rl = await _context.Recept
                    .Where(r => (r.recept_type.Trim().Equals("租赁下单") && r.submit_return_id == rentOrder.id))
                    .AsNoTracking().ToListAsync();
                if (rl != null && rl.Count > 0)
                {
                    
                    rentOrder.staff_name = rl[0].update_staff_name.Trim().Equals("") ?
                        rl[0].recept_staff_name : rl[0].update_staff_name.Trim();
                    if (rentOrder.staff_name.Trim().Equals(""))
                    {
                        try
                        {
                            string staffOpenId = rl[0].update_staff.Trim().Equals("") ?
                                rl[0].recept_staff.Trim() : rl[0].update_staff.Trim();
                            MiniAppUser? staffUser = await _context.MiniAppUsers.FindAsync(staffOpenId.Trim());
                            if (staffUser != null)
                            {
                                rentOrder.staff_name = staffUser.real_name.Trim();
                            }
                        }
                        catch
                        {

                        }
                    }
                }

            }
            var ret = Ok(rentOrder);
            return ret;

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetUnReturn(int id, string sessionKey)
        {
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            detail.real_end_date = null;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(detail);
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
            detail.return_staff = user.miniAppOpenId.Trim();
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            bool allReturned = true;

            double rentalTotal = 0;

            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(detail.rent_list_id, sessionKey)).Result).Value;

            for (int i = 0; i < rentOrder.details.Length; i++)
            {
                RentOrderDetail item = rentOrder.details[i];
                rentalTotal = rentalTotal + item.real_rental + item.overtime_charge + item.reparation;
                if (detail.status.Trim().Equals("未归还"))
                {
                    allReturned = false;
                    //break;
                }
            }
            if (allReturned && Math.Round(rentalTotal, 2) >= Math.Round(rentOrder.deposit_final, 2))
            {
                rentOrder.end_date = DateTime.Now;
                _context.Entry(rentOrder);
                await _context.SaveChangesAsync();
            }
            return Ok(detail);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetRentStart(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            detail.start_date = DateTime.Now;
            detail.rent_staff = user.miniAppOpenId.Trim();
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
                    await refundHelper.TenpayRefund(payment.id, amount,memo, sessionKey);
                }
            }

            return Ok(rentOrder);


        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetPaidManual(int id, string payMethod, string sessionKey)
        {
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            payMethod = Util.UrlDecode(payMethod);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (rentOrder == null || rentOrder.order == null)
            {
                return NotFound();
            }
            if (rentOrder.order.payments == null || rentOrder.order.payments.Length == 0)
            {
                OrderPayment payment = new OrderPayment()
                {
                    id = 0,
                    pay_method = payMethod,
                    amount = rentOrder.order.final_price,
                    staff_open_id = user.miniAppOpenId,
                    order_id = rentOrder.order.id
                };
                await _context.OrderPayment.AddAsync(payment);
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(rentOrder.order_id);
            order.pay_method = payMethod;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return await SetPaid(id, sessionKey);
           
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
            order.pay_time = DateTime.Now;
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

        
        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetUnSettledOrderBefore(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            /*
            var rentOrderList = await _context.RentOrder
                .Where(r => (r.create_date.Date < date.Date && r.create_date.Date >= startDate
                    && (r.end_date == null || ((DateTime)r.end_date).Date >= date.Date)
                    && r.order_id != 0 && (shop.Trim().Equals("") || shop.Trim().Equals(r.shop.Trim())) ))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new {r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund})
                .Where(o => o.pay_state == 1)
                .ToListAsync();
            */
            var rentOrderList = await _context.RentOrder.FromSqlRaw(" select  * from rent_list  "
                + " where create_date < '" + date.ToShortDateString() + "' and create_date > '" + startDate.ToShortDateString() + "' "
                + " and exists ( select 'a' from rent_list_detail  where rent_list_detail.rent_list_id = rent_list.id and "
                + " (real_end_date is null or real_end_date >= '" + date.ToShortDateString() + "' )) "
                + (shop.Trim().Equals("")? " " : " and shop = '" + shop.Replace("'", "").Trim() + "'  " ) )
                .AsNoTracking().ToListAsync();

            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> list = new List<RentOrder>();
            
            for (int i = 0; i < orderArr.Length; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                if (order.status.Equals("已付押金") || order.status.Equals("已退款"))
                {
                    list.Add(order);
                    //list.Append(order);
                }
                else
                {
                    continue;
                }

                //orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                totalDeposit = order.deposit_final + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    RentalDetail detail = order.rentalDetails[j];
                    if (detail.date.Date < date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                    
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日前未完结";
            sum.count = list.Count;
            sum.unRefundDeposit = totalDeposit;
            sum.unSettledRental = totalRental;
            sum.orders = list.ToArray();
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentSameDaySettled(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _context.RentOrder
                .Where(r => (r.create_date.Date == date.Date && r.create_date >= startDate
                    &&  ((DateTime)r.end_date).Date == date.Date
                    && r.order_id != 0 && (shop.Trim().Equals("") || shop.Trim().Equals(r.shop.Trim())) ))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < orderArr.Length; i++)
            {
                orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                if (!orderArr[i].status.Trim().Equals("已退款")
                    && !orderArr[i].status.Trim().Equals("全部归还"))
                {
                    continue;
                }
                totalDeposit = orderArr[i].deposit_final + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < orderArr[i].rentalDetails.Count; j++)
                {
                    RentalDetail detail = orderArr[i].rentalDetails[j];
                    subTotalRental = subTotalRental + detail.rental;
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "日租日结";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr;
            sum.count = sum.orders.Length;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDayPlaced(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _context.RentOrder
                .Where(r => (r.create_date.Date == date.Date && r.create_date >= startDate
                && (shop.Equals("") || shop.Equals(r.shop.Trim()))))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < orderArr.Length; i++)
            {
                orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                totalDeposit = orderArr[i].deposit_final + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < orderArr[i].rentalDetails.Count; j++)
                {
                    RentalDetail detail = orderArr[i].rentalDetails[j];
                    if (detail.date.Date <= date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日新订单";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDaySettledPlacedBefore(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _context.RentOrder
                .Where(r => (r.create_date.Date < date.Date && r.create_date >= startDate
                    && (shop.Equals("") || shop.Equals(r.shop.Trim()))
                    && (r.end_date != null && ((DateTime)r.end_date).Date == date.Date)))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < orderArr.Length; i++)
            {
                orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                totalDeposit = orderArr[i].deposit_final + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < orderArr[i].rentalDetails.Count; j++)
                {
                    RentalDetail detail = orderArr[i].rentalDetails[j];
                    if (detail.date.Date <= date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日新订单";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr;
            return Ok(sum);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> ModItemInfo(int id, float rental,
            double reparation, string memo, double overTimeCharge, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            //detail.real_end_date = returnDate;
            detail.real_rental = rental;
            detail.reparation = reparation;
            detail.memo = memo.Trim();
            detail.overtime_charge = overTimeCharge;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetClassList()
        {
            return await _context.RentItem.Select(r => r.@class).Distinct().ToListAsync();
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<RentalDetail>>> GetRentDetailReport(DateTime start, DateTime end, string sessionKey)
        {

            //RentalDetail[] details = new RentalDetail[];
            ArrayList details = new ArrayList();
            //RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(detail.rent_list_id, sessionKey)).Result).Value;
            RentOrderCollection beforeOrders = (RentOrderCollection)((OkObjectResult)(await GetUnSettledOrderBefore(start, sessionKey)).Result).Value;
            for (int i = 0; i < beforeOrders.orders.Length; i++)
            {
                RentOrder order = beforeOrders.orders[i];
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    if (order.rentalDetails[j].date >= start.Date && order.rentalDetails[j].date <= end.Date)
                    {
                        RentalDetail dtl = order.rentalDetails[j];
                        dtl._name = order.real_name;
                        dtl._cell = order.cell_number;
                        dtl._shop = order.shop.Trim();
                        dtl._staff = order.staff_name.Trim();
                        details.Add(dtl);
                    }
                }
            }

            
            var rentOrderIdList = await _context.RentOrder
                .Where(r => (r.create_date.Date >= start.Date && r.create_date.Date <= end.Date))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund, r.staff_name })
                .Where(o => o.pay_state == 1).ToListAsync();
            for (int i = 0; i < rentOrderIdList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderIdList[i].id, sessionKey)).Result).Value;
                
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    DateTime rentDate = order.rentalDetails[j].date;
                    if (rentDate.Date >= start && rentDate.Date <= end)
                    {
                        RentalDetail dtl = order.rentalDetails[j];
                        dtl._name = order.real_name;
                        dtl._cell = order.cell_number;
                        dtl._shop = order.shop.Trim();
                        dtl._staff = order.staff_name.Trim();
                        details.Add(dtl);
                    }
                }
            }


            RentalDetail[] detailArr = new RentalDetail[details.Count];
            
            for (int i = 0; i < detailArr.Length; i++)
            {
                var dtl = details[i];
                detailArr[i] = (RentalDetail)dtl;
            }

            return Ok(detailArr);

        }

        [HttpPost]
        public async Task<ActionResult<RentOrderDetail>> AppendDetail(string sessionKey, RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            await _context.RentOrderDetail.AddAsync(detail);
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpPost]
        public async Task<ActionResult<RentalDetail>> UpdateDetail(string sessionKey, RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(detail.rent_list_id, sessionKey)).Result).Value;
            detail.rental_count = order.rentalDetails.Count;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpGet("{detailId}")]
        public async Task<ActionResult<RentOrderDetail>> ReserveMore(int detailId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail item = await _context.RentOrderDetail.FindAsync(detailId);
            item.id = 0;
            item.real_end_date = null;
            item.start_date = DateTime.Now.Date.AddDays(1);
            item.reparation = 0;
            item.overtime_charge = 0;
            item.deposit_type = "预约租赁";
            await _context.AddAsync(item);
            await _context.SaveChangesAsync();
            return Ok(item);

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetClose(int id, string sessionKey)
        {
            UnicUser user = await Util.GetUser(sessionKey, _context);

            if (!user.isAdmin)
            {
                return BadRequest();
            }

            var result = (await GetRentOrder(id, sessionKey)).Result;
            if (!result.GetType().Name.Trim().Equals("OkObjectResult"))
            {
                return NotFound();
            }

            RentOrder order = (RentOrder)((OkObjectResult)result).Value;
            if (order.status.Trim().Equals("未支付"))
            {
                order.closed = 1;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return Ok(order);
        }


        [NonAction]
        public async Task<UnicUser> GetUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            return user;
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

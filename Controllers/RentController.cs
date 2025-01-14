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
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Controllers.Order;
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

        private readonly DateTime startDate = DateTime.Parse("2020-10-20");

        private MemberController _memberHelper;

        private OrderOnlinesController _orderHelper;

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
            public double reparation { get; set; } = 0;
            public double rental { get; set; } = 0;
            public string payMethod { get; set; } = "";
        }

        public RentController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            _memberHelper = new MemberController(context, config);
            _orderHelper = new OrderOnlinesController(_context, _oriConfig);
        }

        [NonAction]
        public async Task StartRent(int rentId)
        {
            var rentItemList = await _context.RentOrderDetail.Where(i => i.rent_list_id == rentId).ToListAsync();
            for (int i = 0; rentItemList != null && i < rentItemList.Count; i++)
            {
                RentOrderDetail detail = rentItemList[i];
                if (detail.deposit_type.Trim().Equals("立即租赁"))
                {
                    DateTime nowTime = DateTime.Now;
                    if (detail.start_date == null)
                    {
                        detail.start_date = DateTime.Now;
                    }
                    else
                    {
                        DateTime startDate = (DateTime)detail.start_date;
                        startDate = startDate.AddHours(nowTime.Hour).AddMinutes(nowTime.Minute)
                            .AddSeconds(nowTime.Second).AddMilliseconds(nowTime.Millisecond);
                        detail.start_date = startDate;
                    }
                    _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                }
            }
            await _context.SaveChangesAsync();
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
        public async Task<ActionResult<List<Balance>>> GetBalance(string shop, DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            shop = Util.UrlDecode(shop).Replace("'", "").Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!shop.Trim().Equals("万龙") && user.member.is_admin != 1 && user.member.is_manager != 1 )
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            var idList = await _context.idList.FromSqlRaw(" select distinct rent_list_id as id from rent_list_detail  "
                + "left join rent_list on rent_list.[id] = rent_list_id"
                + " where finish_date >= '" + startDate.ToShortDateString() + "' "
                + " and finish_date <= '" + endDate.AddDays(1).ToShortDateString() + "' and shop like '" + shop + "%'  "
                + " and finish_date is not null and closed = 0 "
                //+ " and rent_list.[id] = 2434"
                )
                .AsNoTracking().ToListAsync();
            List<Balance> bList = new List<Balance>();
            for (int i = 0; i < idList.Count; i++)
            {
               

                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(idList[i].id, sessionKey, false)).Result).Value;
                
                if (!order.status.Trim().Equals("已退款"))
                {
                    continue;
                }
                
                double totalPayment = 0;
                double totalRefund = 0;
                for (int j = 0; j < order.order.payments.Count; j++)
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
                double totalReparation = 0;
                double totalRental = 0;
                for (int j = 0; j < order.details.Count; j++)
                {
                    totalReparation += order.details[j].reparation;
                    RentOrderDetail detail = order.details[j];
                    double subRental = Math.Round(detail.suggestRental, 2) - Math.Round(detail.rental_ticket_discount, 2)
                        - Math.Round(detail.rental_discount , 2) + Math.Round(detail.overtime_charge, 2) ;
                    totalRental += subRental;
                }
                Balance b = new Balance()
                {
                    id = order.id,
                    shop = order.shop,
                    name = order.real_name.Trim(),
                    cell = order.cell_number.Trim(),
                    settleDate = order.finish_date,
                    deposit = totalPayment,
                    refund = totalRefund,
                    earn = totalPayment - totalRefund,
                    reparation = totalReparation,
                    staff = order.staff_name,
                    payMethod = order.order.pay_method.Trim(),
                    rental = totalRental //totalPayment - totalRefund - totalReparation
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            //MiniAppUser customerUser = await _context.MiniAppUsers.FindAsync(rentOrder.open_id);
            Member customerUser = await _memberHelper.GetMember(rentOrder.open_id, "wechat_mini_openid");
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
                    _context.member.Entry(customerUser).State = EntityState.Modified;
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

            for (int i = 0; i < rentOrder.details.Count; i++)
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            /*
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            */
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
                && o.create_date.Date > DateTime.Parse("2024-10-15")    )
                .OrderByDescending(o => o.id).ToListAsync();
            if (orderListTemp == null || orderListTemp.Count <= 0)
            {
                return NotFound();
            }

            List<RentOrder> orderList = new List<RentOrder>();
            for (int i = 0; i < orderListTemp.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderListTemp[i].id, sessionKey, false)).Result).Value;
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
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
                   
                    RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderArr[i].id, sessionKey, false)).Result).Value;
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

        [NonAction]
        public async Task RestoreStaffInfo(RentOrder order)
        {
            var receptList = await _context.Recept.Where(r => r.submit_return_id == order.id)
                .AsNoTracking().ToListAsync();
            if (receptList != null && receptList.Count > 0)
            {
                order.staff_open_id = receptList[0].update_staff.Trim();
                Member? staffUser = await _memberHelper.GetMember(order.staff_open_id, "wechat_mini_openid");
                order.staff_name = staffUser == null ? "" : staffUser.real_name;
                _context.RentOrder.Entry(order);
                await _context.SaveChangesAsync();
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> GetRentOrder(int id, string sessionKey, bool needAuth = true)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);

            //RentOrder rentOrder = await _context.RentOrder.FindAsync(id);

            RentOrder rentOrder = await _context.RentOrder
                //.Include(r => r.order)
                .Include(r => r.details)
                    .ThenInclude(d => d.log)
                .Where(r => r.id == id)
                .FirstAsync();
            if (needAuth)
            {
                if (rentOrder == null)
                {
                    return NotFound();
                }
                if (!user.isAdmin && !rentOrder.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
                {
                    return BadRequest();
                }
                if (rentOrder.staff_open_id.Trim().Equals("") || rentOrder.staff_name.Trim().Equals(""))
                {
                    try
                    {
                        await RestoreStaffInfo(rentOrder);
                    }
                    catch
                    {

                    }
                }
            }
            
            rentOrder.details = await _context.RentOrderDetail
                .Include(d => d.log).Where(d => d.rent_list_id == rentOrder.id)
                .AsNoTracking().ToListAsync();
            
            
            if (rentOrder.order_id > 0)
            {
                
                rentOrder.order = (OrderOnline)((OkObjectResult)(await _orderHelper.GetWholeOrderByStaff(rentOrder.order_id, sessionKey, needAuth)).Result).Value;
            }
            /*
            if (!user.isAdmin)
            {
                rentOrder.open_id = "";
                if (rentOrder.order != null)
                {
                    rentOrder.order.open_id = "";
                }
            }
            */
            bool allReturned = true;
            DateTime returnTime = rentOrder.create_date;
            for (int i = 0; i < rentOrder.details.Count; i++)
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
                    //Member member = await _memberHelper.GetMember(detail.rent_staff, "wechat_mini_openid");
                    //UnicUser.GetUnicUserAsync()
                    detail.rentStaff = (await UnicUser.GetUnicUserByDetailInfo(detail.rent_staff, "wechat_mini_openid", _context)).miniAppUser;
                    //rentOrder.staff_name = detail.rentStaff.real_name;
                }
                else
                {
                    if (!rentOrder.staff_open_id.Trim().Equals(""))
                    {
                        detail.rentStaff = (await UnicUser.GetUnicUserByDetailInfo(rentOrder.staff_open_id, "wechat_mini_openid", _context)).miniAppUser;//await _context.MiniAppUsers.FindAsync(rentOrder.staff_open_id);
                    }
                  

                }

                if (!detail.return_staff.Trim().Equals(""))
                {
                    //detail.returnStaff = await _context.MiniAppUsers.FindAsync(detail.return_staff);
                    detail.returnStaff = (await UnicUser.GetUnicUserByDetailInfo(detail.return_staff, "wechat_mini_openid", _context)).miniAppUser;
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

                /*
                detail.log = await _context.rentOrderDetailLog.Where(r => r.detail_id == detail.id)
                    .OrderByDescending(d => d.id).AsNoTracking().ToListAsync();
                */
                
                switch (rentOrder.shop.Trim())
                {
                    case "南山":
                        TimeSpan ts = endDate - rentOrder.start_date;
                        detail._suggestRental = detail.unit_rental * (ts.Days + 1);
                        detail._timeLength = (ts.Days + 1).ToString() + "天";


                        /*
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
                        */
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
                                /*
                                if (rentOrder.start_date.Hour < 16)
                                {
                                    days++;
                                }
                                */
                                days++;
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
                            //MiniAppUser? staffUser = await _context.MiniAppUsers.FindAsync(staffOpenId.Trim());
                            Member staffUser =  await _memberHelper.GetMember(staffOpenId.Trim(), "wechat_mini_openid");
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

            if (rentOrder.pay_option.Trim().Equals("招待"))
            {
                rentOrder.backColor = "yellow";
            }
            if (rentOrder.order != null)
            {
                if (rentOrder.order.pay_state == 0)
                {
                    rentOrder.backColor = "red";
                    if (rentOrder.status.Trim().Equals("已关闭"))
                    {
                        rentOrder.backColor = "";
                    }
                }

                if (!rentOrder.order.pay_method.Trim().Equals("微信支付") && rentOrder.status.Equals("全部归还"))
                {
                    rentOrder.textColor = "red";
                }
            }
            else
            {
                rentOrder.backColor = "yellow";
            }

            if (rentOrder.status.Equals("已退款"))
            {
                rentOrder.textColor = "red";
            }
            if (rentOrder.status.Trim().Equals("已关闭"))
            {
                rentOrder.textColor = "#C0C0C0";
            }

            if (!rentOrder.real_name.Trim().EndsWith("先生") && !rentOrder.real_name.Trim().EndsWith("女士"))
            {
                Member member = await _memberHelper.GetMember(rentOrder.open_id.Trim(), "wechat_mini_openid");
                if (member != null)
                {
                    rentOrder.real_name = member.real_name.Trim();
                    switch(member.gender)
                    {
                        case "男":
                            rentOrder.real_name += " 先生";
                            break;
                        case "女":
                            rentOrder.real_name += " 女士";
                            break;
                        default:
                            break;
                    }
                }
            }

            


            var ret = Ok(rentOrder);
            return ret;

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetailLog>> SetDetailLog(int id, string status, string sessionKey)
        {
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            status = Util.UrlDecode(status);
            if (status.Trim().Equals("已发放"))
            {
                RentOrderDetail? detail = await _context.RentOrderDetail.FindAsync(id);
                if (detail != null)
                {
                    if (detail.start_date == null)
                    {
                        detail.start_date = DateTime.Now;
                    }
                    else
                    {
                        DateTime startDate = (DateTime)detail.start_date;
                        if (startDate.Hour == 0 && startDate.Minute == 0 && startDate.Second == 0 && startDate.Millisecond == 0)
                        {
                            startDate = startDate.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute);
                        }
                        detail.start_date = startDate;
                        _context.RentOrderDetail.Entry(detail);
                        await _context.SaveChangesAsync();

                    }
                }
            }
            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = id,
                status = status,
                staff_open_id = user.miniAppOpenId,
                create_date = DateTime.Now
            };
            
            await _context.rentOrderDetailLog.AddAsync(log);
            await _context.SaveChangesAsync();
            return Ok(log);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetUnReturn(int id, string sessionKey)
        {
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            detail.real_end_date = null;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await SetDetailLog(id, "已发放", sessionKey);
            detail.log = await _context.rentOrderDetailLog.Where(l => l.detail_id == detail.id)
                .OrderByDescending(l => l.id).AsNoTracking().ToListAsync();
            return Ok(detail);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetReturn(int id, float rental,
            double reparation, DateTime returnDate, string memo, string sessionKey, double overTimeCharge = 0)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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


            await SetDetailLog(id, "已归还", sessionKey);
            detail.log = await _context.rentOrderDetailLog.Where(l => l.detail_id == detail.id)
                .OrderByDescending(l => l.id).AsNoTracking().ToListAsync();


            bool allReturned = true;

            double rentalTotal = 0;

            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(detail.rent_list_id, sessionKey, false)).Result).Value;

            for (int i = 0; i < rentOrder.details.Count; i++)
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);

            DateTime startDate = DateTime.Now;
            if (detail.start_date != null)
            {
                startDate = (DateTime)detail.start_date;
                startDate = startDate.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute);

            }
            else
            {
                startDate = DateTime.Now;
            }

            detail.start_date = startDate;
            detail.rent_staff = user.miniAppOpenId.Trim();
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await SetDetailLog(detail.id, "已发放", sessionKey);

            return Ok(detail);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> Refund(int id, double amount,
            double rentalReduce, double rentalReduceTicket, string memo, string sessionKey)
        {
            
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }

            memo = Util.UrlDecode(memo);
            sessionKey = Util.UrlDecode(sessionKey);
            amount = Math.Round(amount, 2);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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

            if (amount > 0 && rentOrder.order_id > 0 && rentOrder.order != null && rentOrder.payMethod.Trim().Equals("微信支付")
                && rentOrder.order.payments != null && rentOrder.order.payments.Count > 0)
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
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            payMethod = Util.UrlDecode(payMethod);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (rentOrder == null || rentOrder.order == null)
            {
                return NotFound();
            }
            if (rentOrder.order.payments == null || rentOrder.order.payments.Count == 0)
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
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            if (rentOrder == null || rentOrder.order == null
                || rentOrder.order.payments == null || rentOrder.order.payments.Count <= 0)
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
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
                if (order.payments != null && order.payments.Count > 0)
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            
            var rentOrderList = await _context.RentOrder.FromSqlRaw(" select  * from rent_list  "
                + " where create_date < '" + date.ToShortDateString() + "' and create_date > '" + startDate.ToShortDateString() + "' "
                + " and exists ( select 'a' from rent_list_detail  where rent_list_detail.rent_list_id = rent_list.id and "
                + " (real_end_date is null or real_end_date >= '" + date.ToShortDateString() + "' )) "
                + (shop.Trim().Equals("")? " " : " and shop = '" + shop.Replace("'", "").Trim() 
                + "' and closed = 0  and (finish_date >  '" + date.ToShortDateString() + "' or finish_date is null) " 
                //+ " and [id] = 6290 "
                ) )
                .ToListAsync();

            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> list = new List<RentOrder>();
            
            for (int i = 0; i < orderArr.Length; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                if (order.status.Equals("已付押金") || order.status.Equals("已退款"))
                {
                    list.Add(order);
                    //list.Append(order);
                }
                else
                {
                    Console.WriteLine(order.id.ToString() + " " + order.status);
                    /*
                    rentOrderList[i].finished = 1;
                    _context.RentOrder.Entry(rentOrderList[i]).State = EntityState.Modified;
                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch(Exception err)
                    {
                        Console.WriteLine(err.ToString());
                    }
                    */
                    continue;
                }
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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
            List<RentOrder> orderArr = new List<RentOrder>();
            //RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < rentOrderList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                //orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                if (!order.status.Trim().Equals("已退款")
                    && !order.status.Trim().Equals("全部归还"))
                {
                    continue;
                    
                }
                orderArr.Add(order);
                totalDeposit = order.deposit_final + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    RentalDetail detail = order.rentalDetails[j];
                    subTotalRental = subTotalRental + detail.rental;
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "日租日结";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr.ToArray<RentOrder>();
            sum.count = sum.orders.Length;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDayPlaced(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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
                orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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
            List<RentOrder> orderArr = new List<RentOrder>();

            for (int i = 0; i < rentOrderList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                if (!order.status.Trim().Equals("已退款")
                    && !order.status.Trim().Equals("全部归还"))
                {
                    continue;

                }
                orderArr.Add(order);
                totalDeposit = order.deposit_final + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    RentalDetail detail = order.rentalDetails[j];
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
            sum.orders = orderArr.ToArray<RentOrder>();
            return Ok(sum);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> ModItemInfo(int id, float rental,
            double reparation, string memo, double overTimeCharge, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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
        public async Task<ActionResult<List<string>>> GetClassList()
        {
            List<string> list = new List<string>();
            list.Add("双板");
            list.Add("双板鞋");
            list.Add("雪杖");
            list.Add("单板");
            list.Add("单板鞋");
            list.Add("头盔");
            list.Add("雪镜");
            list.Add("雪服");
            list.Add("雪裤");
            list.Add("连体雪服");
            list.Add("手套");
            list.Add("护具");
            list.Add("电加热马甲");
            list.Add("运动相机");
            list.Add("无人机");
            list.Add("对讲机");


            var oriList = await _context.RentItem.Select(r => r.@class)
                .AsNoTracking().Distinct().ToListAsync();
            
            foreach(var ori in oriList)
            {
                bool exists = false;
                foreach(var l in list)
                {
                    if (ori.ToString().Equals(l.ToString()))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists && !ori.ToString().Equals("其他")
                    && ori.ToString().IndexOf("电子") < 0
                    && ori.ToString().IndexOf("雪服上衣") < 0)
                {
                    list.Add(ori.ToString());
                }
            }
            list.Add("其他");
            return Ok(list); 
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
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderIdList[i].id, sessionKey, false)).Result).Value;
                
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

        [HttpGet("{orderId}")]
        public async Task<ActionResult<RentOrder>> SetMemo(int orderId, string memo, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            memo = Util.UrlDecode(memo).Trim();

            RentOrder? order = await _context.RentOrder.FindAsync(orderId);
            if (order == null)
            {
                return BadRequest();
            }
            order.memo = memo;
            _context.RentOrder.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<RentOrderDetail>> AppendDetail(string sessionKey, RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            detail.memo = DateTime.Now.ToString() + " " + user.miniAppUser.real_name + " 追加";
            await _context.RentOrderDetail.AddAsync(detail);
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpPost]
        public async Task<ActionResult<RentalDetail>> UpdateDetail(string sessionKey, RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(detail.rent_list_id, sessionKey, false)).Result).Value;
            detail.rental_count = order.rentalDetails.Count;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpGet("{detailId}")]
        public async Task<ActionResult<RentOrderDetail>> ReserveMore(int detailId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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

            var result = (await GetRentOrder(id, sessionKey, false)).Result;
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
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            return user;
        }

        [HttpGet]
        public async Task<ActionResult<RentOrder>> GetUnReturnedItems(string sessionKey, string shop)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            return Ok(await GetUnReturnedItems(shop));
        }


        [NonAction]
        public async Task<List<RentOrder>> GetUnReturnedItems(string shop = "")
        {
            var rentItemList = await _context.RentOrderDetail
                .FromSqlRaw(" select * from rent_list_detail  "
                + "  where  datepart(hh,rent_list_detail.start_date) <> 0 and  "
                + " datepart(mi,rent_list_detail.start_date) <> 0 "
                + " and datepart(s,rent_list_detail.start_date) <> 0 "
                + " and real_end_date is null order by [id] desc ")
                .AsNoTracking().ToListAsync();
            List<RentOrder> ret = new List<RentOrder>();
            for (int i = 0; i < rentItemList.Count; i++)
            {
                RentOrderDetail item = rentItemList[i];
                if (!item.status.Trim().Equals("已发放"))
                {
                    continue;
                }
                var rL = await _context.RentOrder.Where(r => r.id == item.rent_list_id)
                    .AsNoTracking().ToListAsync();
                if (rL == null || rL.Count == 0)
                {
                    continue;
                }
                RentOrder rentOrder = rL[0];
                if (rentOrder == null)
                {
                    continue;
                }
                if (rentOrder.order_id > 0)
                {
                    rentOrder.order = await _context.OrderOnlines.FindAsync(rentOrder.order_id);
                    rentOrder.order.payments = await _context.OrderPayment
                        .Where(p => p.order_id == rentOrder.order_id).ToListAsync();
                    rentOrder.order.refunds = await _context.OrderPaymentRefund
                        .Where(r => r.order_id == rentOrder.order_id).ToArrayAsync();
                    
                }
                rentOrder.details = (new RentOrderDetail[] { item }).ToList();
                if (!rentOrder.status.Equals("已关闭")
                    && !rentOrder.status.Equals("未支付")
                    && !rentOrder.status.Equals("已退款")
                    && !rentOrder.status.Equals("全部归还"))
                {
                    if (shop.Trim().Equals("") || rentOrder.shop.Trim().Equals(shop))
                    {
                        
                        ret.Add(rentOrder);
                    }
                }
            }
            return ret;
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderList>> GetRentOrderList(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            RentOrderList list = new RentOrderList();
            list.items = new List<RentOrderList.ListItem>();

            var rentList = await _context.RentOrder.FromSqlRaw(" select * from rent_list where ( pay_option = '招待' "
                + " or exists ( select 'a' from order_online where rent_list.order_id = order_online.id and pay_state = 1 ) ) "
                + " and create_date >= '" + startDate.ToShortDateString() + "' and create_date < '" + endDate.AddDays(1).ToShortDateString() + "' "
                //+ " and [id] = 4297 "
                
                ).OrderBy(r => r.shop).OrderByDescending(r =>r.create_date.Date)
                //.OrderBy(r => r.shop)
                .AsNoTracking().ToListAsync();
            for (int i = 0; i < rentList.Count; i++)
            {
                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(rentList[i].id, sessionKey, false)).Result).Value;
                if (!rentOrder.pay_option.Trim().Equals("招待")
                    && (rentOrder.order_id == 0 || rentOrder.order == null || rentOrder.order.pay_state != 1))
                {
                    continue;
                }
                if (!rentOrder.status.Equals("全部归还") && !rentOrder.status.Equals("已退款"))
                {
                    continue;
                }
                    
                RentOrderList.ListItem item = new RentOrderList.ListItem();
                item.cell = rentOrder.cell_number.Trim();
                item.name = rentOrder.real_name.Trim();
                item.dayOfWeek = Util.GetDayOfWeek(rentOrder.create_date);
                item.staffOpenId = rentOrder.staff_open_id;
                item.staffName = rentOrder.staff_name;
                item.status = rentOrder.status;
                item.shop = rentOrder.shop.Trim();
                item.orderDate = rentOrder.create_date;
                item.payDate = (rentOrder.order != null && rentOrder.order.pay_state == 1) ?
                    rentOrder.order.pay_time : null;
                item.id = rentOrder.id;
                item.memo = rentOrder.memo;
                item.entertain = rentOrder.pay_option.IndexOf("招待") >= 0 ? "是" : "否";
                for (int j = 0; rentOrder.order_id > 0
                    && rentOrder.order != null && j < rentOrder.order.payments.Count; j++)
                {
                    if (rentOrder.order.payments[j].status.Trim().Equals("支付成功")
                        && rentOrder.order.payments[j].out_trade_no != null)
                    {
                        item.out_trade_no = rentOrder.order.payments[j].out_trade_no.Trim();
                        break;
                    }
                }
                //item.out_trade_no = rentOrder.order.payments
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    &&  j < rentOrder.order.payments.Count; j++)
                {
                    if (rentOrder.order.payments[j].status.Trim().Equals("支付成功"))
                    {
                        RentOrderList.RentDeposit deposit = new RentOrderList.RentDeposit();
                        deposit.id = rentOrder.order.payments[j].id;
                        deposit.payDate = (DateTime)rentOrder.order.pay_time;
                        deposit.payMethod = rentOrder.order.payments[j].pay_method.Trim();
                        deposit.amount = rentOrder.order.payments[j].amount;
                        item.deposits = new RentOrderList.RentDeposit[] { deposit };
                    }
                }

                List<RentOrderList.RentRefund> refundList = new List<RentOrderList.RentRefund>();
                //item.refunds = new RentOrderList.RentRefund[rentOrder.order.refunds.Length];
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && j < rentOrder.order.refunds.Length; j++)
                {
                    RentOrderList.RentRefund r = new RentOrderList.RentRefund();
                    r.id = rentOrder.order.refunds[j].id;
                    r.refundDate = rentOrder.order.refunds[j].create_date;
                    r.depositId = rentOrder.order.refunds[j].payment_id;
                    r.amount = rentOrder.order.refunds[j].amount;
                    r.refund_id = rentOrder.order.refunds[j].refund_id.Trim();
                    string operOpenId = rentOrder.order.refunds[j].oper;


                    //MiniAppUser refundUser = await _context.MiniAppUsers.FindAsync(operOpenId);

                    Member refundUser = await _memberHelper.GetMember(operOpenId, "wechat_mini_openid");
                    if (refundUser != null)
                    {
                        r.staffName = refundUser.real_name.Trim();
                    }
                    else
                    {
                        r.staffName = "";
                    }
                    //r.staffName = rentOrder.order.refunds[j].s
                    //item.refunds[j] = r;
                    refundList.Add(r);
                }
                item.refunds = refundList.ToArray<RentOrderList.RentRefund>();

                List<RentOrderList.Rental> rentalList = new List<RentOrderList.Rental>();
                //item.rental = new RentOrderList.Rental[rentOrder.rentalDetails.Count];
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    &&  j < rentOrder.rentalDetails.Count; j++)
                {
                    if (rentOrder.rentalDetails[j] == null)
                    {
                        continue;
                    }
                    RentalDetail rentalDtl = rentOrder.rentalDetails[j];
                    bool exists = false;
                    for (int k = 0; k < rentalList.Count; k++)
                    {
                        if (rentalList[k].rentalDate.Date == rentalDtl.date.Date)
                        {
                            rentalList[k].rental += rentalDtl.rental;
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        RentOrderList.Rental r = new RentOrderList.Rental();
                        r.rental = rentOrder.rentalDetails[j].rental;
                        r.rentalDate = rentOrder.rentalDetails[j].date;
                        rentalList.Add(r);
                    }
                    
                }
                item.rental = rentalList.ToArray();
                list.items.Add(item);

            }

            list.startDate = startDate;
            list.endDate = endDate;
            int dayIndex = 1;
            for (int i = list.items.Count - 1; i >= 0; i--)
            {
                list.items[i].indexOfDay = dayIndex;
                if (i > 0)
                {
                    if (list.items[i].orderDate.Date < list.items[i - 1].orderDate.Date || !list.items[i - 1].shop.Trim().Equals(list.items[i].shop.Trim()) )
                    {
                        dayIndex = 1;
                    }
                    else
                    {
                        dayIndex++;
                    }
                }
                list.maxDepositsLength = Math.Max(list.maxDepositsLength,
                    (list.items[i].deposits != null) ?list.items[i].deposits.Length: 0);
                list.maxRefundLength = Math.Max(list.maxRefundLength,
                    (list.items[i].refunds != null) ?list.items[i].refunds.Length : 0);
                list.maxRentalLength = Math.Max(list.maxRentalLength,
                    (list.items[i].rental != null) ? list.items[i].rental.Length : 0);
            }
            return Ok(list);
        }

        [HttpGet("{rentListId}")]
        public async Task<ActionResult<RentAdditionalPayment>> CreateAdditionalPayment(int rentListId, double amount, string reason, 
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            if (!user.isStaff)
            {
                return BadRequest();
            }
            RentAdditionalPayment addPay = new RentAdditionalPayment()
            {
                rent_list_id = rentListId,
                amount = amount,
                reason = Util.UrlDecode(reason),
                staff_open_id = user.member.wechatMiniOpenId.Trim(),
                create_date = DateTime.Now
            };
            await _context.rentAdditionalPayment.AddAsync(addPay);
            await _context.SaveChangesAsync();
            return Ok(addPay);
        }
        [HttpGet("{rentListId}")]
        public async Task<ActionResult<OrderOnline>> PlaceAdditionalOrder(int rentListId, string payMethod, 
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            payMethod = Util.UrlDecode(payMethod).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            RentAdditionalPayment addPay = await _context.rentAdditionalPayment.FindAsync(rentListId);
            if (addPay == null || addPay.order_id != null)
            {
                return NotFound();
            }
            RentOrder rentOrder = await _context.RentOrder.FindAsync(addPay.rent_list_id);
            if (rentOrder == null)
            {
                return NotFound();
            }
            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "押金",
                shop = rentOrder.shop.Trim(),
                open_id = user.wlMiniOpenId.Trim(),
                name = rentOrder.real_name.Trim(),
                cell_number = rentOrder.cell_number.Trim(),
                pay_method = payMethod.Trim(),
                pay_memo = "追加押金",
                pay_state = 0,
                order_price = addPay.amount,
                order_real_pay_price = addPay.amount,
                ticket_amount = 0,
                other_discount = 0,
                final_price = addPay.amount,
                ticket_code = rentOrder.ticket_code.Trim(),
                staff_open_id = addPay.staff_open_id,
                score_rate = 0,
                generate_score = 0

            };
            await _context.OrderOnlines.AddAsync(order);
            await _context.SaveChangesAsync();
            addPay.order_id = order.id;
            addPay.order = order;
            addPay.update_date = DateTime.Now;
            _context.rentAdditionalPayment.Entry(addPay).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            if (order.id == 0)
            {
                return BadRequest();
            }
            OrderPaymentController _orderHelper = new OrderPaymentController(_context, _oriConfig, _httpContextAccessor);
            OrderPayment payment = (OrderPayment)((OkObjectResult)(await _orderHelper.CreatePayment(order.id, payMethod, order.final_price)).Result).Value;
            order.payments = (new OrderPayment[] {payment}).ToList();
            return Ok(order);
        }

        [NonAction]
        public async Task AdditionalOrderPaid(int orderId)
        {
            List<RentAdditionalPayment> addPayList = await _context.rentAdditionalPayment
                .Where(r => r.order_id == orderId).ToListAsync();
            for(int i = 0; i < addPayList.Count; i++)
            {
                RentAdditionalPayment addPay = addPayList[i];
                addPay.is_paid = 1;
                _context.rentAdditionalPayment.Entry(addPay).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }

       
        private bool RentOrderExists(int id)
        {
            return _context.RentOrder.Any(e => e.id == id);
        }
    }
}

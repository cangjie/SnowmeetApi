using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
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
        private IHttpContextAccessor _http;
        public Mi7OrderController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor http)
        {
            _context = context;
            _config = config;
            _http = http;
        }
        [NonAction]
        public async Task SetMi7OrderPaySuccess(int orderId)
        {
            List<Mi7Order> mOrder = await _context.mi7Order.Where(m => m.order_id == orderId).ToListAsync();
            for (int i = 0; i < mOrder.Count; i++)
            {
                mOrder[i].valid = 1;
                _context.Entry(mOrder[i]).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }
        [HttpGet]
        public async Task<ActionResult<OrderOnline>> AddSupplementOrder(string shop, string openId, string cell,
            string? payer, string name, string gender, bool urgent, string? mi7No, DateTime? bizDate, string payMethod,
            double salePrice, double dealPrice, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (mi7No != null)
            {
                List<Mi7Order> mi7List = await _context.mi7Order.Where(m => m.mi7_order_id.Trim().Equals(mi7No) && m.valid == 1)
                    .AsNoTracking().ToListAsync();
                if (mi7List.Count > 0)
                {
                    return NotFound();
                }
            }
            if (shop.Trim().Equals(""))
            { 
                return BadRequest();
            }
            name = (name + " " + (gender.Trim().Equals("男")?"先生":(gender.Trim().Equals("女")?"女士":""))).Trim();
            if (urgent)
            {
                if (bizDate == null)
                {
                    return BadRequest();
                }
                mi7No = null;
            }
            else
            {
                if (mi7No == null)
                {
                    return BadRequest();
                }
                bizDate = null;
            }
            int payState = 1;
            DateTime? payTime = DateTime.Now;
            int mi7Valid = 1;
            if (payMethod.Trim().Equals("微信支付"))
            {
                payState = 0;
                payTime = null;
                mi7Valid = 0;
            }
            Mi7Order mi7Order = new Mi7Order()
            {
                id = 0,
                order_id = 0,
                mi7_order_id = mi7No,
                sale_price = salePrice,
                real_charge = dealPrice,
                barCode = "",
                biz_date = bizDate,
                supplement = 1,
                valid = mi7Valid
            };
            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "店销现货",
                shop = shop.Trim(),
                open_id = openId,
                cell_number = cell.Trim(),
                name = name.Trim() + " " + (gender.Trim().Equals("男") ? "先生" : (gender.Trim().Equals("女") ? "女士" : "")),
                pay_method = payMethod,
                pay_state = payState,
                pay_time = payTime,
                order_price = salePrice,
                order_real_pay_price = dealPrice,
                pay_memo = "",
                code = "",
                syssn = "",
                memo = "补单",
                other_discount = 0,
                final_price = dealPrice,
                staff_open_id = user.member.wechatMiniOpenId,
                biz_date = bizDate == null ? DateTime.Now : (DateTime.Now),
                payer = payer
                
                //mi7Orders = new List<Mi7Order>() { mi7Order }

            };
            mi7Order.order_id = order.id;
            order.mi7Orders = new List<Mi7Order>() { mi7Order };
            if (!payMethod.Trim().Equals("微信支付"))
            {
                OrderPayment payment = new OrderPayment()
                {
                    id = 0,
                    order_id = order.id,
                    pay_method = payMethod,
                    amount = dealPrice,
                    status = "支付成功",
                    create_date = DateTime.Now,
                    open_id = openId,
                    staff_open_id = user.member.wechatMiniOpenId.Trim()
                };
                order.paymentList = new List<OrderPayment>() { payment };
            }
            await _context.OrderOnlines.AddAsync(order);
            await _context.SaveChangesAsync();
            return Ok(order);
            //return BadRequest();
        }
        [HttpGet("{mi7OrderId}")]
        public async Task<ActionResult<Mi7Order>> Enterain(string mi7OrderId, string name, 
            string cell, string gender, DateTime date, double price, string shop, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            gender = Util.UrlDecode(gender);
            name = Util.UrlDecode(name);
            shop = Util.UrlDecode(shop);
            List<Mi7Order> orderList = await _context.mi7Order
                .Where(m => m.mi7_order_id.Trim().Equals(mi7OrderId.Trim()) && m.valid == 1)
                .AsNoTracking().ToListAsync();
            if (orderList.Count != 0)
            {
                return NotFound();
            }
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            int? memberId = null;
            MemberController _memberHelper = new MemberController(_context, _config);
            Models.Users.Member member = (Models.Users.Member)((OkObjectResult)(await _memberHelper.GetMemberByCell(cell, sessionKey, sessionType)).Result).Value;
            if (member != null)
            {
                memberId = member.id;
            }

            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "店销现货",
                shop = shop.Trim(),
                open_id = ((member == null)? "": member.wechatMiniOpenId.Trim()),
                cell_number = cell.Trim(),
                name = name.Trim() + " " + (gender.Trim().Equals("男")? "先生" : (gender.Trim().Equals("女")? "女士": "")),
                pay_method = "",
                pay_state = 1,
                pay_time = date,
                order_price = 0,
                order_real_pay_price = 0,
                pay_memo = "无需支付",
                code = "",
                syssn = "",
                memo = "招待",
                other_discount = 0,
                final_price = 0,
                staff_open_id = user.member.wechatMiniOpenId,
                biz_date = date
            };
            await _context.OrderOnlines.AddAsync(order);
            await _context.SaveChangesAsync();
            Mi7Order mi7Order = new Mi7Order()
            {
                id = 0,
                order_id = order.id,
                mi7_order_id = mi7OrderId.Trim(),
                sale_price = price,
                real_charge = 0,
                barCode = "",
                order_type = "招待",
                enterain_member_id = memberId,
                enterain_date = date.Date,
                enterain_gender = gender.Trim(),
                enterain_cell = cell.Trim(),
                enterain_real_name = name.Trim(),
                valid = 1
            };
            await _context.mi7Order.AddAsync(mi7Order);
            await _context.SaveChangesAsync();
            return Ok(mi7Order);
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderPayment>> PaySupplement(int orderId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (!order.pay_method.Trim().Equals("微信支付"))
            {
                return BadRequest();
            }
            OrderPaymentController _payHelper = new OrderPaymentController(_context, _config, _http);
            OrderPayment payment = (OrderPayment)((OkObjectResult)(await _payHelper.CreatePayment(orderId, order.pay_method, order.final_price)).Result).Value ;
            payment = (OrderPayment)((OkObjectResult)(await _payHelper.Pay(payment.id, sessionKey)).Result).Value;
            return Ok(payment);
            //return BadRequest();
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
                    order_id = (int)mi7Order.order_id,
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

        [HttpGet("{id}")]
        public async Task<ActionResult<Mi7Order>> GetMi7OrderById(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<Mi7Order> orders = await _context.mi7Order
                .Include(m => m.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Where(m => (m.id == id && m.order.pay_state == 1))
                .OrderByDescending(m => m.order.pay_time).AsNoTracking().ToListAsync();
            if (orders.Count == 0)
            {
                return NotFound();
            }
            else
            {
                Models.Order.Mi7Order mi7Order = orders[0];
                MemberController _memberHelper = new MemberController(_context, _config);
                mi7Order.order.member = await _memberHelper.GetMember(mi7Order.order.open_id, "wechat_mini_openid");
                return Ok(mi7Order);
            }
        }

        [HttpGet("{mi7OrderId}")]
        public async Task<ActionResult<Mi7Order>> GetMi7Order(string mi7OrderId, 
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<Mi7Order> orders = await _context.mi7Order
                .Include(m => m.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Where(m => (m.mi7_order_id.Trim().Equals(mi7OrderId) && m.order.pay_state == 1))
                .OrderByDescending(m => m.order.pay_time).AsNoTracking().ToListAsync();
            if (orders.Count == 0)
            {
                return NotFound();
            }
            else
            {
                Models.Order.Mi7Order mi7Order = orders[0];
                MemberController _memberHelper = new MemberController(_context, _config);
                mi7Order.order.member = await _memberHelper.GetMember(mi7Order.order.open_id, "wechat_mini_openid");
                return Ok(mi7Order);
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<List<Models.StaffModLog>>> GetLogs(int id, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<Models.StaffModLog> logs = await _context.staffModLog
                .Include(l => l.staffMember).ThenInclude(m => m.memberSocialAccounts)
                .Where(l => (l.table_name.Trim().Equals("mi7_order") && l.key_id.Trim().Equals(id.ToString()) ))
                .OrderByDescending(l => l.id).AsNoTracking().ToListAsync();
            return Ok(logs); 
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Mi7Order>> ModMi7Order(int id, string orderNum, string sessionKey, string orderType = "普通")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            orderNum = Util.UrlDecode(orderNum);
            Mi7Order order = await _context.mi7Order.FindAsync(id);
            if (order == null)
            {
                return BadRequest();
            }
            
            if (!orderNum.Trim().Equals(""))
            {
                order.mi7_order_id = orderNum;
                StaffModLog log = new StaffModLog()
                {
                    id = 0,
                    table_name = "mi7_order",
                    field_name = "mi7_order_id",
                    key_id = id.ToString(),
                    scene = "修改七色米订单号",
                    staff_member_id = user.member.id,
                    prev_value = order.mi7_order_id,
                    current_value = orderNum,
                    create_date = DateTime.Now
                };
                await _context.staffModLog.AddAsync(log);
            }
            if (!order.order_type.Trim().Equals(orderType.Trim()))
            {
                
                StaffModLog log = new StaffModLog()
                {
                    id = 0,
                    table_name = "mi7_order",
                    field_name = "order_type",
                    key_id = id.ToString(),
                    scene = "修改七色米订单类型",
                    staff_member_id = user.member.id,
                    prev_value = order.order_type,
                    current_value = orderType.Trim(),
                    create_date = DateTime.Now
                };
                await _context.staffModLog.AddAsync(log);
                order.order_type = orderType.Trim();
            }
            
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return order;
        }
        [HttpGet]
        public async Task PairWithMemo()
        {
            List<Mi7ExportedSaleList> list = await _context.mi7ExportedSaleList
                .Where(m => m.mi7_order_id == null).ToListAsync();
            List<Mi7Order> mi7OrderListOri = await _context.mi7Order
                .Include(m => m.order).ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Where(o =>  !o.mi7_order_id.StartsWith("XSD"))
                .ToListAsync();
            List<Mi7Order> mi7OrderList = mi7OrderListOri.Where(m => m.create_date>DateTime.Parse("2024-9-1")).ToList();
            for(int i = 0; i < list.Count; i++)
            {
                //bool paired = false;
                Mi7ExportedSaleList mi7e = list[i];
                if (mi7e.orderIdArr.Length == 0)
                {
                    continue;
                }
                for(int j = 0; j < mi7OrderList.Count; j++)
                {
                    Mi7Order order = mi7OrderList[j];
                   
                    for(int k = 0; k < mi7e.orderIdArr.Length; k++)
                    {
                        if (mi7e.orderIdArr[k] == order.order_id)
                        {
                            mi7e.mi7_order_id = order.id;
                            order.mi7_order_id = mi7e.单据编号.Trim();
                            _context.mi7ExportedSaleList.Entry(mi7e).State = EntityState.Modified;
                            _context.mi7Order.Entry(order).State = EntityState.Modified;
                            await _context.SaveChangesAsync();
                        }
                    }
                }

            }
            
        }
        [HttpGet]
        public async Task PairWithCell()
        {
            List<Mi7ExportedSaleList> list = await _context.mi7ExportedSaleList
                .Where(m => m.mi7_order_id == null).ToListAsync();
            List<Mi7Order> mi7OrderListOri = await _context.mi7Order
                .Include(m => m.order).ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Where(o =>  !o.mi7_order_id.StartsWith("XSD"))
                .ToListAsync();
            List<Mi7Order> mi7OrderList = mi7OrderListOri.Where(m => m.create_date>DateTime.Parse("2024-9-1")).ToList();
            for(int i = 0; i < mi7OrderList.Count; i++)
            {
                Mi7Order order = mi7OrderList[i];
                if (order.member == null)
                {
                    List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                        .Include(m => m.member).ThenInclude(m => m.memberSocialAccounts)
                        .Where(m => m.num.Trim().Equals(order.order.open_id.Trim()) && m.type.Trim().Equals("wechat_mini_openid"))
                        .ToListAsync();
                    if (msaList != null && msaList.Count > 0)
                    {
                        order.member = msaList[0].member;
                    }
                }
                if (order == null || order.member == null || order.member.cell == null || order.member.cell.Trim().Equals(""))
                {
                    continue;
                }
                List<Mi7ExportedSaleList> subList = list.Where(l => l.cell.Trim().Equals(order.member.cell)).ToList();
                if (subList == null || subList.Count == 0)
                {
                    continue;
                }
                for(int j = 0; j < subList.Count; j++)
                {
                    if (DateTime.Parse(subList[j].业务日期).Date == order.create_date.Date 
                        && double.Parse(subList[j].实收金额.ToString()) == order.order.paidAmount)
                    {
                        Mi7ExportedSaleList mi7e = subList[j];
                        mi7e.mi7_order_id = order.id;
                        order.mi7_order_id = mi7e.单据编号.Trim();
                        _context.mi7ExportedSaleList.Entry(mi7e).State = EntityState.Modified;
                        _context.mi7Order.Entry(order).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                }
            }
        }
        /*
        [HttpGet]
        public async Task PairWithCell()
        {
            List<Mi7ExportedSaleList> list = await _context.mi7ExportedSaleList
                .Where(m => m.mi7_order_id == null).ToListAsync();
            List<Mi7Order> mi7OrderListOri = await _context.mi7Order
                .Include(m => m.order).ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Where(o =>  !o.mi7_order_id.StartsWith("XSD"))
                .ToListAsync();
            List<Mi7Order> mi7OrderList = mi7OrderListOri.Where(m => m.create_date>DateTime.Parse("2024-9-1")).ToList();
            for(int i = 0; i < list.Count; i++)
            {
                //bool paired = false;
                Mi7ExportedSaleList mi7e = list[i];
                
                for(int j = 0; j < mi7OrderList.Count; j++)
                {
                    Mi7Order order = mi7OrderList[j];
                    
                    if (order.member == null)
                    {
                        List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                            .Include(m => m.member).ThenInclude(m => m.memberSocialAccounts)
                            .Where(m => m.num.Trim().Equals(order.order.open_id.Trim()) && m.type.Trim().Equals("wechat_mini_openid"))
                            .ToListAsync();
                        if (msaList != null && msaList.Count > 0)
                        {
                            order.member = msaList[0].member;
                        }
                    }
                    string cell = order.member != null && order.member.cell != null ? order.member.cell.Trim() : "";
                    if (cell.Equals("") || mi7e.cell.Trim().Equals(""))
                    {
                        continue;
                    }
                    if (cell.Trim().Equals(mi7e.cell.Trim()) && DateTime.Parse(mi7e.业务日期) == order.create_date.Date 
                        && double.Parse(mi7e.实收金额) == order.order.paidAmount )
                    {
                        mi7e.mi7_order_id = order.id;
                        order.mi7_order_id = mi7e.单据编号.Trim();
                        _context.mi7ExportedSaleList.Entry(mi7e).State = EntityState.Modified;
                        _context.mi7Order.Entry(order).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                    
                    //bool paired = false;
                    
                }

            }
            
        }
        */

        private bool Mi7OrderExists(int id)
        {
            return _context.mi7Order.Any(e => e.id == id);
        }
    }
}

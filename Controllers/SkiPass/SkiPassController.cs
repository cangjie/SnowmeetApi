using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using Newtonsoft.Json;
using SnowmeetApi.Controllers.User;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SixLabors.ImageSharp;
using SnowmeetApi.Controllers.Order;
using Org.BouncyCastle.Asn1.Crmf;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class SkiPassController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        private MemberController _memberHelper;
        private OrderPaymentController _refundHelper;

        private WanlongZiwoyouHelper _zwHelper;
        private IHttpContextAccessor _http;

        public SkiPassController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor http)
        {
            _context = context;
            _config = config;
            _memberHelper = new MemberController(context,  config);
            _refundHelper = new OrderPaymentController(context, config, http);
            _zwHelper = new WanlongZiwoyouHelper(context, config);
            _http = http;
        }
        [HttpGet]
        public  ActionResult<List<string>> GetResorts()
        {
            List<string> ret = new List<string>();
            ret.Add("万龙");
            ret.Add("南山");
            return Ok(ret);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetSkiPassDetailInfo(int id)
        {
            return await _context.product.Where(p => p.id == id)
                .Join(_context.skiPassProduct, p => p.id, s => s.product_id,
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
        public async Task<ActionResult<IEnumerable<object>>> GetSkiPassProduct(string resort, DateTime date, string tags, int showAll = 0)
        {
            if (date >= DateTime.Parse("2026-1-1") && date <= DateTime.Parse("2026-1-2") )
            {
                tags = tags.Replace("平日", "节假日").Replace("周六", "节假日").Replace("周日", "节假日").Replace("周末", "节假日");
            }

            if (date.Date == DateTime.Parse("2026-1-4"))
            {
                tags = tags.Replace("节假日", "平日").Replace("周六", "平日").Replace("周日", "平日").Replace("周末", "平日");
            }
            string[] tagArr = tags == null ? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');
            var skiPassProdustList = await _context.product.Where(p => (p.shop.Trim().Equals(resort.Trim()) && p.hidden == 0 && p.end_date >= DateTime.Now.Date))
                .Join(_context.skiPassProduct, p => p.id, s => s.product_id,
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
                Models.SkiPassProduct skiPass = new Models.SkiPassProduct()
                {
                    product_id = r.product_id,
                    resort = r.resort.Trim(),
                    end_sale_time = r.end_sale_time,
                    rules = r.rules,
                    available_days = r.available_days,
                    unavailable_days = r.unavailable_days,
                    tags = r.tags

                };
                if (date >= DateTime.Parse("2023-1-28") && date <= DateTime.Parse("2023-1-29"))
                {
                    if (!skiPass.DateMatch(date))
                    {
                        skiPassProdustList.RemoveAt(i);
                        i--;
                    }
                }
                else if (!skiPass.DateMatch(date) || !skiPass.TagMatch(tagArr))
                {
                    if (showAll == 0)
                    {
                        skiPassProdustList.RemoveAt(i);
                        i--;
                    }
                }


            }
            return Ok(skiPassProdustList);
        }
        [HttpGet("{skipassId}")]
        public async Task<ActionResult<Models.SkiPass>> GetSkipass(int skipassId, string sessionKey, string sessionType="wechat_mini_openid")
        {
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            Models.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
            if (member.id != skipass.member_id && member.wechatMiniOpenId.Trim().Equals(skipass.wechat_mini_openid)
                && member.is_admin == 0 && member.is_staff == 0 && member.is_manager == 0)
            {
                return BadRequest();
            }
            return Ok(skipass);
        }

        [HttpGet]
        public async Task<ActionResult<List<Models.SkiPass>>> GetMySkipass
            (string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            List<Models.SkiPass> l = await _context.skiPass.Where(s => ( s.valid == 1
                && (s.member_id == member.id || s.wechat_mini_openid.Trim().Equals(member.wechatMiniOpenId.Trim())  )))
                .OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            
            return Ok(l);
        }

        

        [HttpGet("{skipassId}")]
        public async Task<ActionResult<Models.SkiPass>> Cancel(int skipassId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            Staff staff = await Util.GetStaffBySessionKey(_context, sessionKey,sessionType);
            Models.SkiPass skipass = await _context.skiPass.Where(s => s.id == skipassId).AsNoTracking().FirstOrDefaultAsync();   //.FindAsync(skipassId);
            if (member.id != skipass.member_id && staff == null && staff.title_level < 100)
            {
                return BadRequest();
            }
            if (skipass.status.Trim().Equals("出票失败"))
            {
                return await Refund(skipassId, "出票失败退款", sessionKey, sessionType);
            }
            skipass.card_member_return_time = DateTime.Now;
            skipass.cancel_member_id = member.id;
            skipass.is_cancel = 3;
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            try
            {
                Models.SkiPassProduct product = await _context.skiPassProduct.FindAsync(skipass.product_id);
                WanlongZiwoyouHelper _wlHelper = new WanlongZiwoyouHelper(_context, _config, product.source.Trim());
                WanlongZiwoyouHelper.ZiwoyouQueryResult r = await _wlHelper.CancelOrder(int.Parse(skipass.reserve_no));
                WanlongZiwoyouHelper.ZiwoyouCancel cancel = (WanlongZiwoyouHelper.ZiwoyouCancel)r.data;
                skipass.is_cancel = cancel.cancelState;
                skipass.memo += " " + r.msg.Trim();
                if (cancel.cancelState == 1)
                {
                    return await Refund(skipassId, "退票即时确认退款", sessionKey, sessionType);
                }
                
            }
            catch(Exception err)
            {
                skipass.is_cancel = -1;
                skipass.memo = "取消出错 " + err.ToString();
            }
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(skipass);
        }

        [HttpGet]
        public async Task<ActionResult<Models.SkiPass>> Refund(int skipassId, string reason, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            
            Models.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
            if (skipass.resort.Trim().Equals("南山"))
            {
                return BadRequest();
            }
            if (skipass.refund_amount != null || skipass.have_refund != null)
            {
                return NotFound();
            }
            if (skipass.reserve_no != null && !skipass.reserve_no.Trim().Equals(""))
            {
                string json = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrder?orderId=" + skipass.reserve_no);
                WanlongZiwoyouHelper.ZiwoyouOrder order = JsonConvert.DeserializeObject<WanlongZiwoyouHelper.ZiwoyouOrder>(json);
                if (order.orderState != 3)
                {
                    return BadRequest();
                }
            }
            List<OrderPayment> pL = (await _context.OrderPayment.Where(p => p.order_id == skipass.order_id && p.status.Trim().Equals("支付成功")).AsNoTracking().ToListAsync());
            if (pL == null || pL.Count == 0 )
            {
                return NotFound();
            }
            int paymentId = pL[0].id;
            double amount = pL[0].amount;
            OrderPaymentRefund refund = (OrderPaymentRefund)((OkObjectResult)(await _refundHelper.Refund(paymentId, amount, reason, sessionKey, sessionType)).Result).Value;
            if (!refund.refund_id.Equals(""))
            {
                skipass.have_refund = 1;
                skipass.update_date = DateTime.Now;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return Ok(skipass);    
        }
        [HttpGet]
        public async Task CreateSkiPass(int orderId)
        {
            OrderController _orderHelper = new OrderController(_context, _config, _http);
            Models.Order order = await _context.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            if (order == null)
            {
                return;
            }
            List<Models.SkiPass> dealList = new List<Models.SkiPass>();
            order.skipasses = await _context.skiPass.Where(s => s.order_id == order.id).AsNoTracking().ToListAsync();
            for(int i = 0; i < order.skipasses.Count; i++)
            {
                if (!order.skipasses[i].booking_now && !order.skipasses[i].reserve_success)
                {
                    order.skipasses[i].booking_now = true;
                    order.skipasses[i].valid = 1;
                    order.skipasses[i].update_date = DateTime.Now;
                    _context.skiPass.Entry(order.skipasses[i]).State = EntityState.Modified;
                    dealList.Add(order.skipasses[i]);
                }
            }
            await _context.SaveChangesAsync();
            CoreDataModLog logOrderTicket = CoreDataModLog.CreateManualLog("Order", "", order.id, "开始订票", null, null, null, null, "");
            await _context.coreDataModLog.AddAsync(logOrderTicket);
            await _context.SaveChangesAsync();
            for(int i = 0; i < dealList.Count; i++)
            {
                Models.SkiPass skipass = dealList[i];
                if (!skipass.resort.Trim().Equals("南山"))
                {
                    await AutoReserve(skipass);
                    CoreDataModLog logTicket = CoreDataModLog.CreateManualLog("ski_pass", "", skipass.id, "万龙雪票下单成功", null, null, null, null, "");
                    await _context.coreDataModLog.AddAsync(logTicket);
                    await _context.SaveChangesAsync();
                }
                /*
                try
                {
                    await SendTicket(skipass);
                }
                catch
                {

                }   
                */
            }
            TicketController _ticketHelper = new TicketController(_context, _config);
            CoreDataModLog logOrder = CoreDataModLog.CreateManualLog("Order", "", order.id, "开始处理雪票杂项", null, null, null, null, "");
            await _context.coreDataModLog.AddAsync(logOrder);
            await _context.SaveChangesAsync();
            for(int i = 0; order.skipasses != null && i < order.skipasses.Count; i++)
            {
                try
                {
                    CoreDataModLog logPrev = CoreDataModLog.CreateManualLog("ticket", "create_memo", order.skipasses[i].id, "买雪票发券开始", null, null, null, null, "发券之前");
                    await _context.coreDataModLog.AddAsync(logPrev);
                    await _context.SaveChangesAsync();
                    Ticket tNew = await _ticketHelper.CreateTicketBySkiPass(order.skipasses[i]);
                    CoreDataModLog logAfter = CoreDataModLog.CreateManualLog("ticket", "create_memo", order.skipasses[i].id, "买雪票发券结束", null, null, null, null, "发券" + tNew==null?"失败":"成功");
                    await _context.coreDataModLog.AddAsync(logAfter);
                    await _context.SaveChangesAsync();
                    try
                    {
                        if (order.shop != "南山" && order.skipasses[i].card_no != null && order.skipasses[i].card_no != "")
                        {
                            await SetNotify(order.skipasses[i]);
                        }
                        else if (order.shop == "南山")
                        {
                            await SetNotify(order.skipasses[i]);
                        }
                    }
                    catch
                    {
                        
                    }
                }
                catch
                {
                    
                }
            }
        }
       
        /*
        [NonAction]
        public async Task CreateSkiPass1(int orderId)
        {
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            List<OrderPayment> pList = await _context.OrderPayment
                .Include(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Where(p => p.order_id == orderId && p.status.Trim().Equals("支付成功")).AsNoTracking().ToListAsync();
            if (order == null)
            {
                return;
            }
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Where(s => s.order_id == order.id).ToListAsync();
            bool notified = false;
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                skipass.valid = 1;
                skipass.update_date = DateTime.Now;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                if (!notified && pList.Count > 0)
                {
                    OrderPayment payment = pList[0];
                    await SetNotify(payment.open_id, payment.wepay_trans_id, 1, skipass.product_name, (int)(payment.amount * 100), payment.timestamp, 1);
                    notified = false;
                }
                
            }
            

            //雪票生效后附赠以及分账
            for (int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                //await SetNotify(skipass.id, 1);
                //await SendTicket(skipass);
                if (!skipass.resort.Trim().Equals("南山"))
                {

                    await AutoReserve(skipass.id);

                }
                try
                {
                    await SendTicket(skipass);
                }
                catch
                {

                }
                
            }
            await CreateShare(orderId);
        }
        */
        /*
        [NonAction]
        public async Task CreateShare(int orderId)
        {
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (order == null)
            {
                return;
            }
            Member? member = await _memberHelper.GetWholeMemberByNum(order.open_id.Trim(), "wechat_mini_openid");
            if (member == null)
            {
                return;
            }

            int refereeMemberId = 0;
            DateTime onlineDate = DateTime.Parse("2024-12-15");
            //分账系统上线后，有推荐人ID并且支付成功的雪票订单
            List<OrderOnline> prevSkipassList = await _context.OrderOnlines
                .Where(o => o.open_id.Trim().Equals(order.open_id.Trim()) && o.pay_state == 1 
                && o.type.Trim().Equals("雪票") && o.id < order.id && o.referee_member_id !=0 
                && (DateTime)o.pay_time >= onlineDate)
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();

      

            if (prevSkipassList != null && prevSkipassList.Count > 0)
            {
                refereeMemberId = prevSkipassList[0].referee_member_id;
            }
            

            RefereeController _refHelper = new RefereeController(_context, _config);
            Referee referee = await  _refHelper.GetReferee(member.id, "雪票");
            //确认分过账的kol
            if (referee != null)
            {
                refereeMemberId = referee.channel_member_id;
            }
            //上一笔支付成功的并且带推荐人用户ID的
            else if (refereeMemberId == 0)
            {
                
                List<OrderOnline> prevSkipassListOld = await _context.OrderOnlines
                .Where(o => o.open_id.Trim().Equals(order.open_id.Trim()) && o.pay_state == 1 
                && o.type.Trim().Equals("雪票") && o.id < order.id   && (DateTime)o.pay_time < onlineDate)
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
                //是否是今年的新用户
                if (prevSkipassListOld == null || prevSkipassListOld.Count == 0)
                {
                    refereeMemberId = order.referee_member_id;
                }
                else
                {
                    refereeMemberId = 0;
                }
            }
            if (refereeMemberId == 0)
            {
                return;
            }
            List<Models.SkiPass> skipasses = await _context.skiPass
                .Where(s => s.valid == 1 && s.order_id == orderId)
                .AsNoTracking().ToListAsync();
            Models.Kol kol = await _refHelper.GetKol(refereeMemberId);
            List<OrderPayment> payments = await _context.OrderPayment
                .Where(p => p.status.Trim().Equals("支付成功") && p.order_id == orderId)
                .AsNoTracking().ToListAsync();
            if (payments == null || payments.Count <= 0)
            {
                return;
            }
            for(int i = 0; i<skipasses.Count; i++)
            {
               
            }
            await _context.SaveChangesAsync();
        }
        */
        [NonAction]
        public async Task SendTicket(Models.SkiPass skipass)
        {
            if (skipass.product_name.Trim().IndexOf("租板") >= 0)
            {
                return;
            }
            
            TicketController _ticketHelper = new TicketController(_context, _config);
            for(int i = 0; i < skipass.count; i++)
            {
                    
                Models.Ticket ticket = await _ticketHelper.GenerateTicketByAction(12, 
                        skipass.member_id, 0, skipass.order_id == null? 0 : (int)skipass.order_id, "");
            } 
        }

        [HttpGet]
        public async Task AutoReserve(Models.SkiPass skipass)
        {
            if (!skipass.status.Equals("已付款"))
            {
                skipass.memo += " 雪票状态不对。";
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return;
            }
            Models.SkiPassProduct skipassProduct = await _context.skiPassProduct.FindAsync(skipass.product_id);
            WanlongZiwoyouHelper _zwHelper = new WanlongZiwoyouHelper(_context, _config, skipassProduct.source);
            List<OrderPayment> pList = await _context.OrderPayment
                .Where(p => (p.order_id == skipass.order_id && p.status.Trim().Equals("支付成功")))
                .AsNoTracking().ToListAsync();
            string outTradeNo = "";
            foreach (OrderPayment payment in pList)
            {
                if (!payment.out_trade_no.Trim().Equals(""))
                {
                    outTradeNo = payment.out_trade_no.Trim();
                    break;
                }
            }
            if (outTradeNo.Trim().Equals(""))
            {
                outTradeNo = skipass.id.ToString() + Util.GetLongTimeStamp(DateTime.Now);
            }
            double balance = (double)((OkObjectResult)(await _zwHelper.GetBalance()).Result).Value;
            //balance = 10000;
            if (balance <= skipass.deal_price)
            {
                skipass.memo += " 账户余额不足";
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return;
            }
            string orderId = "";
            try
            {
                Models.WanLong.ZiwoyouPlaceOrderResult orderResult = 
                    await _zwHelper.PlaceOrder(skipassProduct.third_party_no, skipass.contact_name, skipass.contact_cell,
                    skipass.contact_id_type, skipass.contact_id_no, skipass.count, (DateTime)skipass.reserve_date, 
                    "", outTradeNo);
                if (orderResult.state == 1)
                {
                    orderId = orderResult.data.orderId;
                    
                }
                else
                {
                    skipass.memo += (" " + orderResult.msg.Trim()) ;
                    skipass.is_cancel = -2;
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    return;
                }
                
            }
            catch(Exception err)
            {
                skipass.memo += (" 预定失败 " + err.ToString()) ;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return;

            }
            Models.WanLong.PayResult payResult = await _zwHelper.Pay(int.Parse(orderId));
            if (payResult.state != 1 || !payResult.msg.Trim().Equals("支付成功"))
            {
                skipass.memo += (" " + payResult.msg);
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                
                return;
            }
            skipass.reserve_no = payResult.data.orderId.ToString();
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            _context.skiPass.Entry(skipass).State = EntityState.Detached;
            await _context.SaveChangesAsync();
            CoreDataModLog logTicket = CoreDataModLog.CreateManualLog("ski_pass", "", skipass.id, "万龙雪票预定成功", null, null, null, null, "");
                await _context.coreDataModLog.AddAsync(logTicket);
                await _context.SaveChangesAsync();
            for(int i = 0; i < 5; i++)
            {
                System.Threading.Thread.Sleep(5000);
                //_zwHelper.
                await RefreshAutoReserve();
            }
        }

        [NonAction]
        public async Task RefundCallBack(int paymentId)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);

            TicketController _ticketHelper = new TicketController(_context, _config);
            await _ticketHelper.Cancel(payment.order_id);

            Models.SkiPass skipass = await _context.skiPass
                .Where(s => (s.valid == 1 && s.order_id == payment.order_id))
                .OrderByDescending(s => s.id).FirstAsync();
            OrderPaymentRefund refund = await _context.orderPaymentRefund
                .Where(p => p.order_id == payment.order_id && p.state == 1)
                .AsNoTracking().FirstAsync();
            if (!skipass.resort.Trim().Equals("南山"))
            {
                skipass.refund_amount = refund.amount;
                skipass.update_date = DateTime.Now;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
        }

        [HttpGet]
        public async Task RefreshCancel()
        {
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.reserve_no != null && !s.resort.Trim().Equals("南山")
                && s.is_cancel == 2)).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                Models.SkiPassProduct skipassProduct = await _context.skiPassProduct.FindAsync(skipass.product_id);
                if (skipassProduct == null)
                {
                    continue;
                }
                try
                {
                    WanlongZiwoyouHelper _zwHelper = new WanlongZiwoyouHelper(_context, _config, skipassProduct.source.Trim());
                    WanlongZiwoyouHelper.ZiwoyouOrder order = (WanlongZiwoyouHelper.ZiwoyouOrder)((OkObjectResult)(await _zwHelper.GetOrder(int.Parse(skipass.reserve_no))).Result).Value;
                    if (order.orderState == 3)
                    {
                        skipass.is_cancel = 1;
                        _context.skiPass.Entry(skipass).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        
                    }
                    
                }
                catch
                {

                }
            }
        }
        [HttpGet]
        public async Task RefreshUsedAll()
        {
            TicketController _tHelper = new TicketController(_context, _config);
            OrderShareController _shareHelper = new OrderShareController(_context, _config, _http);
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.is_cancel == 0 
                && s.is_used == 0 && !s.resort.Trim().Equals("南山")))
                .OrderByDescending(s => s.id).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                if (skipass.reserve_no == null)
                {
                    continue;
                }
                string url = "https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrder?orderId=" + skipass.reserve_no.ToString();
                string ret = Util.GetWebContent(url);
                WanlongZiwoyouHelper.ZiwoyouOrder order = JsonConvert.DeserializeObject<WanlongZiwoyouHelper.ZiwoyouOrder>(ret);
                if (order != null && order.orderState == 4)
                {
                    skipass.is_used = 1;
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    //await _tHelper.ActiveTicket((int)skipass.order_id);
                    if (skipass.order_id != null)
                    {
                        try
                        {
                            await _tHelper.ActiveSkipassTicket(skipass);
                            if (skipass.order_id != null)
                            {
                                await _shareHelper.ExecuteShare((int)skipass.order_id);    
                            }
                            OrderController _oH = new OrderController(_context, _config, _http);
                            if (skipass.order_id != null)
                            {
                                await _oH.SetReferee((int)skipass.order_id, skipass.id);
                            }
                        }
                        catch
                        {
                            
                        }
                    }
                }
            }
            
        }
        [HttpGet]
        public async Task RefreshAutoReserve()
        {
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Include(s => s.order)
                    .ThenInclude(o => o.payments.Where(p => p.status.Equals("支付成功")))
                .Where(s => (s.valid == 1 && s.reserve_no != null && !s.resort.Trim().Equals("南山")
                && s.card_no == null && s.qr_code_url == null && s.send_content == null && s.is_cancel == 0
                //&& s.create_date > DateTime.Now.AddHours(-480)
                //&& s.id == 2008
                )).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                Models.SkiPassProduct skipassProduct = await _context.skiPassProduct.FindAsync(skipass.product_id);
                if (skipassProduct == null)
                {
                    continue;
                }
                try
                {
                    //WanlongZiwoyouHelper _zwHelper = new WanlongZiwoyouHelper(_context, _config, skipassProduct.source.Trim());
                    //WanlongZiwoyouHelper.ZiwoyouOrder order = _zwHelper.GetOrder(int.Parse(skipass.reserve_no));
                    string json = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrder?orderId=" + skipass.reserve_no);
                    WanlongZiwoyouHelper.ZiwoyouOrder order = JsonConvert.DeserializeObject<WanlongZiwoyouHelper.ZiwoyouOrder>(json);
                    if (order == null)
                    {
                        skipass.is_cancel = -2;
                        skipass.memo = "未获取到自我游订单号";
                        skipass.update_date = DateTime.Now;
                        
                        _context.skiPass.Entry(skipass).State = EntityState.Modified;
                        await _context.SaveChangesAsync();

                        continue;
                    }
                    if (order.vouchers.Length > 0)
                    {
                        bool updated = false;
                        if (order.vouchers[0].code != null && !order.vouchers[0].code.Trim().Equals(""))
                        {
                            skipass.card_no = order.vouchers[0].code.Trim();
                            updated = true;
                        }
                        if (order.vouchers[0].qrcodeUrl != null && !order.vouchers[0].qrcodeUrl.Trim().Equals(""))
                        {
                            skipass.qr_code_url = order.vouchers[0].qrcodeUrl.Trim();
                            updated = true;
                        }
                        if (updated)
                        {
                            try
                            {
                                await SetNotify(skipass);    
                            }
                            catch
                            {
                                
                            }
                        }
                    }
                    switch(order.orderState)
                    {
                        case 3:
                            skipass.is_cancel = -3;
                            skipass.update_date = DateTime.Now;
                            break;
                        case 4:
                            skipass.is_used = 1;
                            skipass.update_date = DateTime.Now;
                            break;
                        default:
                            break;
                    }
                    string sendContent = "";
                    if (order.sendContent1 != null)
                    {
                        sendContent += order.sendContent1;
                    }
                    if (order.sendContent2 != null)
                    {
                        sendContent += order.sendContent2;
                    }
                    if (order.sendContent3 != null)
                    {
                        sendContent += order.sendContent3;
                    }
                    if (!sendContent.Trim().Equals(""))
                    {
                        skipass.send_content = sendContent.Trim();
                    }
                    skipass.update_date = DateTime.Now;
                    skipass.card_member_pick_time = DateTime.Now;
                }
                catch
                {
                    continue;
                }
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                _context.skiPass.Entry(skipass).State = EntityState.Detached;
                await _context.SaveChangesAsync();
                CoreDataModLog logTicket = CoreDataModLog.CreateManualLog("ski_pass", "", skipass.id, "万龙雪票信息更新成功", null, null, null, null, "");
                await _context.coreDataModLog.AddAsync(logTicket);
                await _context.SaveChangesAsync();
            }
        }
        
        
        /*
        [NonAction]
        private bool SkiPassExists(int id)
        {
            return _context.SkiPass.Any(e => e.product_id == id);
        }
        */
        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date, 
            int count, string cell, string name, string sessionKey, string sessionType = "wechat_mini_openid", string idNo = "", int refereeMemberId = 0, int? staffId = null)
        {
            Models.Product product = await _context.product.FindAsync(productId);
            Models.SkiPassProduct skipassProduct = await _context.skiPassProduct.FindAsync(productId);
            SkipassDailyPrice dailyPrice = await _context.skipassDailyPrice
                .Where(s => s.product_id == productId && s.valid == 1 && s.reserve_date.Date == date.Date)
                .OrderBy(s => s.reserve_date).AsNoTracking().FirstAsync();
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.real_name == null || member.real_name.Trim() == "" )
            {
                member.real_name = name;
                _context.member.Entry(member).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                _context.member.Entry(member).State =EntityState.Detached;
                await _context.SaveChangesAsync();
            }
            //double totalPrice = 0;
            Models.SkiPass skipass = new Models.SkiPass()
            {
                member_id = member.id,
                wechat_mini_openid = member.wechatMiniOpenId,
                product_id = productId,
                resort = skipassProduct.resort.Trim(),
                product_name = product.name,
                count = count,
                //order_id = orderId,
                deal_price = dailyPrice.deal_price * count,
                ticket_price = dailyPrice.settlementPrice,
                deposit = product.deposit,
                valid = 0,
                contact_cell = cell,
                contact_name = name,
                contact_id_no = idNo.Trim(),
                contact_id_type = "身份证",
                reserve_date = date.Date,
                booking_now = false,
                reserve_success = false
            };
            Models.Order order = new Models.Order()
            {
                id = 0,
                member_id = member.id,
                type = "雪票",
                shop = product.shop.Trim(),
                total_amount = (double)skipass.deal_price * count,
                paying_amount = (double)skipass.deal_price * count,
                create_date = DateTime.Now,
                valid = 1
            };
            BizReferee referee = await _context.bizReferee.Where(r => r.valid && r.biz_type == "雪票" && r.member_id == member.id).AsNoTracking().FirstOrDefaultAsync();
            if (staffId == null && referee != null && referee.staff_id != null)
            {
                staffId = referee.staff_id;
            }
            if (staffId == null)
            {
                List<MemberSocialAccount> msaOaList = member.GetInfo("wechat_oa_openid");
                if (msaOaList != null && msaOaList.Count > 0)
                {
                    string? oaOpenId = msaOaList[0].num.Trim();
                    if (oaOpenId != null)
                    {
                        OAReceive lastScan = await _context.oAReceive.Where(o => o.FromUserName == oaOpenId 
                            && o.create_date.Date == DateTime.Now.Date && o.EventKey.Trim().IndexOf("reserveskipassbystaff_") >= 0)
                            .OrderByDescending(o => o.id).AsNoTracking().FirstOrDefaultAsync();
                        if (lastScan != null)
                        {
                            try
                            {
                                string key = lastScan.EventKey.Trim();
                                string[] keyArr = key.Split('_');
                                staffId = int.Parse(keyArr[keyArr.Length - 1]);
                            }
                            catch
                            {
                                
                            }
                            
                        }
                    }
                    
                }
            }
            if (staffId != null)
            {
                order.staff_id = staffId;
                OrderShareRelation shareRelation = await _context.orderShareRelation.Where(r => r.staff_id == staffId).AsNoTracking().FirstOrDefaultAsync();
                if (shareRelation != null)
                {
                    OrderShare share = new OrderShare()
                    {
                        id = 0,
                        order_id = order.id,
                        relation_id = shareRelation.id,
                        amount = 1,
                        valid = true,
                        dealed = false,
                        create_date = DateTime.Now
                    };
                    order.orderShares.Add(share);
                }
                
            }
            OrderController _orderHelper = new OrderController(_context, _config, _http);
            await _orderHelper.GenerateOrderCode(order);
            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = "微信支付",
                amount = (double)order.paying_amount,
                status = "待支付",
                staff_open_id = "",
                out_trade_no = order.code + "_ZF_01",
                valid = 1,
                need_share = order.orderShares.Count > 0?1:0,
                create_date = DateTime.Now
            };
            order.payments = new List<OrderPayment>() { payment };
            order.skipasses = new List<Models.SkiPass>() { skipass };
            await _context.order.AddAsync(order);
            await _context.SaveChangesAsync();
            return Ok(order);
        }
        [HttpGet]
        public async Task CommitSkipassOrder(int orderId)
        {
            List<Models.SkiPass> skipasses = await _context.skiPass
                .Where(s => s.valid == 1 && s.order_id == orderId)
                .AsNoTracking().ToListAsync();
            bool allFinish = true;
            int memberId = 0;
            int skipassId = 0;
            for(int i = 0; i < skipasses.Count; i++)
            {
                if (memberId == 0)
                {
                    memberId = skipasses[i].member_id;
                }
                if (skipassId == 0)
                {
                    skipassId = skipasses[i].id;
                }
                if (skipasses[i].CanRefund)
                {
                    allFinish = false;
                    break;
                }
            }
            if (allFinish)
            {
                TicketController _tHelper = new TicketController(_context, _config);
                await _tHelper.ActiveTicket((int)orderId);
                /*
                var shareList = await _context.paymentShare
                    .Where(s => s.order_id == orderId && s.state == 0 && s.submit_date == null)
                    .AsNoTracking().ToListAsync();
                if (shareList != null && shareList.Count > 0)
                {
                    OrderPaymentController _paymentHelper = new OrderPaymentController(_context, _config, _http);
                    int paymentId = 0;
                    for(int i = 0; i < shareList.Count; i++)
                    {
                        Models.PaymentShare share = shareList[i];
                        await _paymentHelper.SubmitShare(share.id);
                        if (paymentId != share.payment_id)
                        {
                            if (paymentId != 0)
                            {
                                await _paymentHelper.ShareFinish(paymentId, "雪票分账结束"); 
                            }
                            paymentId = share.payment_id;
                        }           
                    }
                    if (paymentId != 0)
                    {
                        await _paymentHelper.ShareFinish(paymentId, "雪票分账结束"); 
                    }
                    
                    OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
                    if (order != null)
                    {
                        RefereeController _refHelper = new RefereeController(_context, _config);
                        await _refHelper.SetReferee(memberId, order.referee_member_id, "雪票", order.id, skipassId);
                    }
                }
                */
            }
        }


        
        [NonAction]
        public async Task CommitSkipass(int skipassId)
        {
            Models.SkiPass skipass = await _context.skiPass.Where(s => s.id == skipassId)
                .AsNoTracking().FirstAsync();
           
            if (skipass == null)
            {
                return;
            }
            if (skipass.order_id != null)
            {
                TicketController _tHelper = new TicketController(_context, _config);
                await _tHelper.ActiveTicket((int)skipass.order_id);

                
                /*
                var shareList = await _context.paymentShare
                    .Where(s => s.order_id == (int)skipass.order_id && s.state == 0 && s.submit_date == null)
                    .AsNoTracking().ToListAsync();

                if (shareList != null && shareList.Count > 0)
                {
                    OrderPaymentController _paymentHelper = new OrderPaymentController(_context, _config, _http);
                    int paymentId = 0;
                    for(int i = 0; i < shareList.Count; i++)
                    {
                        Models.PaymentShare share = shareList[i];
                        await _paymentHelper.SubmitShare(share.id);
                        if (paymentId != share.payment_id)
                        {
                            if (paymentId != 0)
                            {
                                await _paymentHelper.ShareFinish(paymentId, "雪票分账结束"); 
                            }
                            paymentId = share.payment_id;
                        }
                        
                    }
                    if (paymentId != 0)
                    {
                        await _paymentHelper.ShareFinish(paymentId, "雪票分账结束"); 
                    }
                    
                    OrderOnline order = await _context.OrderOnlines.FindAsync(skipass.order_id);
                    if (order != null)
                    {
                        RefereeController _refHelper = new RefereeController(_context, _config);
                        await _refHelper.SetReferee(skipass.member_id, order.referee_member_id, "雪票", order.id, skipass.id);
                    }
                }
                */
            }
        }




        [HttpGet]
        public async Task<ActionResult<List<SkipassWithPrice>>> GetProductsByResort(string resort, int showHidden = 0)
        {
            resort = Util.UrlDecode(resort);
            var l = await _context.skiPassProduct//.Include(s => s.dailyPrice)
                .Join(_context.product, s=>s.product_id, p=>p.id,
                (s, p)=> new {s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop, 
                s.commonDayDealPrice, s.weekendDealPrice, 
                //s.dailyPrice, 
                //s.avaliablePriceList,
                p.sale_price, p.market_price, p.cost, p.type, p.hidden })
                .Where(p => p.type.Trim().Equals("雪票") && p.resort.Trim().Equals(resort)
                && p.third_party_no != null && p.source == "万龙自我游" 
                && ((p.hidden == 0 && showHidden == 0) || (showHidden == 1))
                ).OrderBy(p => p.market_price).AsNoTracking().ToListAsync();
            
            List<SkipassWithPrice> ret = new List<SkipassWithPrice>();
            for(int i = 0; i < l.Count; i++)
            {
                var p = l[i];
                Models.SkiPassProduct skipass = new Models.SkiPassProduct()
                {
                    resort = p.resort,
                    rules = p.rules,
                    third_party_no = p.third_party_no,
                    source = p.source,
                    dailyPrice = await _context.skipassDailyPrice.Where(s => s.product_id == p.product_id && s.valid == 1)
                        .OrderBy(s => s.reserve_date).AsNoTracking().ToListAsync(),
                    
                };
                SkipassWithPrice sp = new SkipassWithPrice()
                {
                    product_id = p.product_id,
                    resort = skipass.resort,
                    rules = skipass.rules,
                    source = skipass.source,
                    third_party_no = skipass.third_party_no,
                    name = p.name,
                    shop = p.shop,
                    commonDayDealPrice = skipass.commonDayDealPrice,
                    weekendDealPrice = skipass.weekendDealPrice,
                    dailyPrice = skipass.dailyPrice,
                    avaliablePriceList = skipass.avaliablePriceList,
                    sale_price = p.sale_price,
                    market_price = p.market_price,
                    cost = p.cost,
                    type = p.type,
                    hidden = p.hidden
                };
                ret.Add(sp);
            }


            return Ok(ret);
        }

        public class SkipassWithPrice
        {
            public int product_id {get; set;}
            public string resort {get; set;}
            public string rules {get; set;}
            public string source {get; set;}
            public string third_party_no {get; set;}
            public string name {get; set;}
            public string shop {get;set;}
            public double commonDayDealPrice {get; set;}
            public double weekendDealPrice {get; set;}
            public List<SkipassDailyPrice> dailyPrice {get; set;}
            public List<SkipassDailyPrice> avaliablePriceList {get; set;}
            public double sale_price {get; set;}
            public double? market_price {get ;set;}
            public double? cost {get; set;}
            public string type {get; set;}
            public int hidden {get; set;}
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<SkipassWithPrice>> GetProduct(int productId)
        {
            Product p = await _context.product.FindAsync(productId);
            Models.SkiPassProduct skipass = await _context.skiPassProduct.FindAsync(productId);
            skipass.dailyPrice = await _context.skipassDailyPrice.Where(s => s.product_id == productId && s.valid == 1)
                .OrderBy(s => s.reserve_date).AsNoTracking().ToListAsync();
            
            SkipassWithPrice ret = new SkipassWithPrice()
            {
                product_id = p.id,
                resort = skipass.resort,
                rules = skipass.rules,
                source = skipass.source,
                third_party_no = skipass.third_party_no,
                name = p.name,
                shop = p.shop,
                commonDayDealPrice = skipass.commonDayDealPrice,
                weekendDealPrice = skipass.weekendDealPrice,
                dailyPrice = skipass.dailyPrice,
                avaliablePriceList = skipass.avaliablePriceList,
                sale_price = p.sale_price,
                market_price = p.market_price,
                cost = p.cost,
                type = p.type,
                hidden = p.hidden
            };
            return Ok(ret);
        }

        [HttpGet("{priceId}")]
        public async Task<ActionResult<SkipassDailyPrice>> ModDailyPrice(int priceId, double price, 
            string dayType, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            MemberController _memberHelper = new MemberController(_context, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            */
            Staff staff = await Util.GetStaffBySessionKey(_context, sessionKey);
            if (staff.title_level <= 200)
            {
                return BadRequest();
            }
            SkipassDailyPrice priceObj = await _context.skipassDailyPrice.FindAsync(priceId);
            if (priceObj == null)
            {
                return NotFound();
            }
            priceObj.day_type = Util.UrlDecode(dayType);
            priceObj.deal_price = price;
            _context.skipassDailyPrice.Entry(priceObj).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(priceObj);
        }
        [HttpGet]
        public async Task<ActionResult<List<Models.SkiPass>>> GetDHHSReservedSkipasses(DateTime start,
            DateTime end, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_context, sessionKey, sessionType);
            if (staff.title_level < 100)
            {
                return BadRequest();
            }
            List<Models.SkiPass> sList = await _context.skiPass.Where(s => s.valid == 1 && !s.resort.Trim().Equals("南山")
                && s.create_date.Date >= start.Date && s.create_date.Date <= end.Date ).OrderByDescending(s => s.id)
                .Include(s => s.order)
                    .ThenInclude(o => o.payments.Where(p => p.status.Equals("支付成功")))
                       .ThenInclude(p => p.refunds.Where(r => (r.state == 1 || r.refund_id != "")))
                .AsNoTracking().ToListAsync();
            return Ok(sList);
        }
        [NonAction]
        public async Task<ActionResult<string>> SetNotify(Models.SkiPass skiPass)
        {
            string dateStr = ((DateTime)skiPass.reserve_date).Date.ToString("yyyy-MM-dd");
            string resort = skiPass.resort == "南山" ? "南山滑雪场" : "万龙度假天堂";
            string title = "";
            string cardNo = "";
            switch(skiPass.resort)
            {
                case "万龙":
                    if (skiPass.product_name.IndexOf("夜场") >= 0)
                    {
                        title = "万龙夜场自带板雪票";
                    }
                    else if (skiPass.product_name.IndexOf("4小时") >= 0)
                    {
                        title = "万龙日场4小时自带板雪票";
                    }
                    else if (skiPass.product_name.IndexOf("6小时") >= 0)
                    {
                        title = "万龙日场6小时自带板雪票";
                    }
                    else if (skiPass.product_name.IndexOf("万龙日场1天自带板雪票") >= 0)
                    {
                        title = "万龙日场1天自带板雪票";
                    }
                    else if (skiPass.product_name.IndexOf("万龙日场1.5天自带板雪票") >= 0)
                    {
                        title = "万龙日场1.5天自带板雪票";
                    }
                    else if (skiPass.product_name.IndexOf("万龙日场2天自带板雪票") >= 0)
                    {
                        title = "万龙日场2天自带板雪票";
                    }
                    cardNo = skiPass.card_no;
                    break;
                case "南山":
                    cardNo = skiPass.id.ToString();
                    title = skiPass.product_name.Trim().Replace("【", "").Replace("】", "").Replace("-", "").Trim();
                    break;
                default:
                    break;
            }
            string miniAppPath ="/pages/mine/skipass/my_skipass";
            string miniAppQuery = "";
            string miniAppUrl = "https://mini.snowmeet.top/mapp/open_mapp_page.html?path=" + Util.UrlEncode(miniAppPath) + "&query=" + Util.UrlEncode(miniAppQuery);

            string notUrl = "https://wxoa.snowmeet.top/api/TemlateMessage/SendTemplateMessage?memberId="
                + skiPass.member_id.ToString() +  "&templateId=oxAKjz6JBBs_nPm6j89nMlGJoTtoQKrSm_E_HKn0QXE&first=test&keywords=" 
                + Util.UrlEncode(resort + "【免费打蜡】|" + title + "|" + dateStr + "|" + cardNo ) 
                + "&remark=test2&url=" + miniAppUrl + "&sessionKey=" + Util.UrlEncode("abcd123!@#");
            string ret = Util.GetWebContent(notUrl);
            return Ok(ret);
        }
        /*
        [HttpGet]
        public async Task SetNotify(string openId, string transId, int count, string name, int amount, string timeStamp, int curState)
        {
            
            string postJson = "";
            string memo = "";
            switch(curState)
            {
                case 1:
                    memo = "雪票模版消息-激活";
                    Thread.Sleep(30000);
                    postJson = "{"
                        + "\"openid\": \"" + openId.Trim() + "\", "
                        + "\"notify_type\": 2011, "
                        + "\"notify_code\": \"" + transId + "\", "
                        + "\"content_json\" : \"{ "
                            + "\\\"cur_status\\\": " + curState.ToString() + ", "
                            + "\\\"product_count\\\": " + count.ToString() + ", "
                            + "\\\"product_list\\\": {"
                                + "\\\"info_list\\\": [{"
                                    + "\\\"product_img\\\": \\\"https://mini.snowmeet.top/images/snowmeet_logo.png\\\", "
                                    + "\\\"product_name\\\": \\\"" + name + "\\\", "
                                    + "\\\"product_path_query\\\":\\\"pages/mine/skipass/my_skipass\\\" }]} ,"
                                + "\\\"wxa_path_query\\\":\\\"pages/mine/skipass/my_skipass\\\" }\", "
                        + "\"check_json\" : \"{ \\\"pay_amount\\\": " + amount.ToString() 
                        + ", \\\"pay_time\\\": " + timeStamp + " }\" }" ;
                break;
                case 2:
                    memo = "雪票模版消息-出票";
                    postJson = "{"
                            + "\"openid\": \"" + openId.Trim() + "\", "
                            + "\"notify_type\": 2011, "
                            + "\"notify_code\": \"" + transId + "\", "
                            + "\"content_json\" : \"{ "
                                + "\\\"cur_status\\\": " + curState.ToString() + ", "
                                + "\\\"product_count\\\": " + count.ToString() + ", "
                                + "\\\"product_list\\\": {"
                                    + "\\\"info_list\\\": [{"
                                        + "\\\"product_img\\\": \\\"https://mini.snowmeet.top/images/snowmeet_logo.png\\\", "
                                        + "\\\"product_name\\\": \\\"" + name + "\\\", "
                                        + "\\\"product_path_query\\\":\\\"pages/mine/skipass/my_skipass\\\" }]} ,"
                                    + "\\\"wxa_path_query\\\":\\\"pages/mine/skipass/my_skipass\\\" }\" }";
                            
                break;
                case 4:
                    memo = "雪票模版消息-待使用";
                    postJson = "{"
                            + "\"openid\": \"" + openId.Trim() + "\", "
                            + "\"notify_type\": 2011, "
                            + "\"notify_code\": \"" + transId + "\", "
                            + "\"content_json\" : \"{ "
                                + "\\\"cur_status\\\": " + curState.ToString() + ", "
                                + "\\\"product_count\\\": " + count.ToString() + ", "
                                + "\\\"product_list\\\": {"
                                    + "\\\"info_list\\\": [{"
                                        + "\\\"product_img\\\": \\\"https://mini.snowmeet.top/images/snowmeet_logo.png\\\", "
                                        + "\\\"product_name\\\": \\\"" + name + "\\\", "
                                        + "\\\"product_path_query\\\":\\\"pages/mine/skipass/my_skipass\\\" }]} ,"
                                    + "\\\"wxa_path_query\\\":\\\"pages/mine/skipass/my_skipass\\\" }\" }";
                            
                break;
                default:
                break;
            }
            MiniAppHelperController _helper = new MiniAppHelperController(_context, _config);
            string token = _helper.GetAccessToken();
            string url = "https://api.weixin.qq.com/wxa/set_user_notify?access_token=" + token.Trim();

            await _helper.PerformRequest(url, "", postJson, "POST", "易龙雪聚小程序", "预订雪票", memo);
        }
        */
    }

    
    
}

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
using SnowmeetApi.Controllers.User;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SixLabors.ImageSharp;
using SnowmeetApi.Controllers.Order;
using Org.BouncyCastle.Asn1.Crmf;
using System.Runtime.CompilerServices;
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

            if ((date >= DateTime.Parse("2023-12-30") && date <= DateTime.Parse("2024-1-1") )
                || (date >= DateTime.Parse("2024-2-9") && date <= DateTime.Parse("2024-2-18")) )
            {
                tags = "节假日";
            }

            if (date.Date == DateTime.Parse("2024-2-19"))
            {
                tags = "平日";
            }

            string[] tagArr = tags == null ? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');

            


            var skiPassProdustList = await _context.Product.Where(p => (p.shop.Trim().Equals(resort.Trim()) && p.hidden == 0 && p.end_date >= DateTime.Now.Date))
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
                Models.Product.SkiPass skiPass = new Models.Product.SkiPass()
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
                    skiPassProdustList.RemoveAt(i);
                    i--;
                }


            }
            return Ok(skiPassProdustList);
        }
        [HttpGet("{skipassId}")]
        public async Task<ActionResult<Models.SkiPass.SkiPass>> GetSkipass(int skipassId, string sessionKey, string sessionType="wechat_mini_openid")
        {
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
            if (member.id != skipass.member_id && member.wechatMiniOpenId.Trim().Equals(skipass.wechat_mini_openid)
                && member.is_admin == 0 && member.is_staff == 0 && member.is_manager == 0)
            {
                return BadRequest();
            }
            return Ok(skipass);
        }

        [HttpGet]
        public async Task<ActionResult<List<Models.SkiPass.SkiPass>>> GetMySkipass
            (string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            List<Models.SkiPass.SkiPass> l = await _context.skiPass.Where(s => ( s.valid == 1
                && (s.member_id == member.id || s.wechat_mini_openid.Trim().Equals(member.wechatMiniOpenId.Trim())  )))
                .OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            
            return Ok(l);
        }

        

        [HttpGet("{skipassId}")]
        public async Task<ActionResult<Models.SkiPass.SkiPass>> Cancel(int skipassId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
            if (member.id != skipass.member_id && member.wechatMiniOpenId.Trim().Equals(skipass.wechat_mini_openid)
                && member.is_admin == 0 && member.is_staff == 0 && member.is_manager == 0)
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
                Models.Product.SkiPass product = await _context.SkiPass.FindAsync(skipass.product_id);
                WanlongZiwoyouHelper _wlHelper = new WanlongZiwoyouHelper(_context, _config, product.source.Trim());
                WanlongZiwoyouHelper.ZiwoyouQueryResult r = _wlHelper.CancelOrder(int.Parse(skipass.reserve_no));
                WanlongZiwoyouHelper.ZiwoyouCancel cancel = (WanlongZiwoyouHelper.ZiwoyouCancel)r.data;
                skipass.is_cancel = cancel.cancelState;
                skipass.memo += " " + r.msg.Trim();
                if (cancel.cancelState == 1)
                {
                    return await Refund(skipassId, "退票即时确认退款", sessionKey, sessionType);
                }
                
            }
            catch
            {
                skipass.is_cancel = -1;
            }
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(skipass);
        }

        [HttpGet]
        public async Task<ActionResult<Models.SkiPass.SkiPass>> Refund(int skipassId, string reason, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
            if (skipass.resort.Trim().Equals("南山"))
            {
                return BadRequest();
            }
            if (skipass.refund_amount != null || skipass.have_refund != null)
            {
                return NotFound();
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
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return Ok(skipass);    
        }


       
        
        [NonAction]
        public async Task CreateSkiPass(OrderOnline order)
        {
            List<Models.SkiPass.SkiPass> skipassList = await _context.skiPass
                .Where(s => s.order_id == order.id).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassList[i];
                skipass.valid = 1;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            for (int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassList[i];
                //await SendTicket(skipass);
                if (!skipass.resort.Trim().Equals("南山"))
                {
                    await AutoReserve(skipass.id);
                }
                //买雪票送打蜡
                try
                {
                    /*
                    TicketController _tHelper = new TicketController(_context, _config);
                    await _tHelper.GenerateTicketByAction(12, skipass.member_id, 0,
                            skipass.order_id == null? 0: (int)skipass.order_id , "");
                    */
                    await SendTicket(skipass);
                    
                }
                catch
                {

                }
            }
        }

        [NonAction]
        public async Task SendTicket(Models.SkiPass.SkiPass skipass)
        {
            if (skipass.product_name.Trim().IndexOf("租板") >= 0)
            {
                return;
            }
            var l = await _context.Ticket.Where(t => t.order_id == (int)skipass.order_id)
                .AsNoTracking().ToListAsync();
            if (l == null || l.Count == 0 )
            {
                TicketController _ticketHelper = new TicketController(_context, _config);
                Models.Ticket.Ticket ticket = await _ticketHelper.GenerateTicketByAction(12, 
                    skipass.member_id, 0, skipass.order_id == null? 0 : (int)skipass.order_id, "");
            }
        }

        [HttpGet]
        public async Task AutoReserve(int skipassId)
        {
            
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
            if (!skipass.status.Equals("已付款"))
            {
                skipass.memo += " 雪票状态不对。";
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return;
            }
            Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(skipass.product_id);
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
            double balance = _zwHelper.GetBalance();
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
                    _zwHelper.PlaceOrder(skipassProduct.third_party_no, skipass.contact_name, skipass.contact_cell,
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
            Models.WanLong.PayResult payResult = _zwHelper.Pay(int.Parse(orderId));
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


            
        }

        [NonAction]
        public async Task RefundCallBack(int paymentId)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);

            TicketController _ticketHelper = new TicketController(_context, _config);
            await _ticketHelper.Cancel(payment.order_id);

            Models.SkiPass.SkiPass skipass = await _context.skiPass
                .Where(s => (s.valid == 1 && s.order_id == payment.order_id))
                .OrderByDescending(s => s.id).FirstAsync();
            OrderPaymentRefund refund = await _context.OrderPaymentRefund
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
            List<Models.SkiPass.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.reserve_no != null && !s.resort.Trim().Equals("南山")
                && s.is_cancel == 2)).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassList[i];
                Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(skipass.product_id);
                if (skipassProduct == null)
                {
                    continue;
                }
                try
                {
                    WanlongZiwoyouHelper _zwHelper = new WanlongZiwoyouHelper(_context, _config, skipassProduct.source.Trim());
                    WanlongZiwoyouHelper.ZiwoyouOrder order = _zwHelper.GetOrder(int.Parse(skipass.reserve_no));
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

            List<Models.SkiPass.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.is_cancel == 0 
                && s.is_used == 0 && !s.resort.Trim().Equals("南山"))).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassList[i];

                string url = "https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrder?orderId=" + skipass.reserve_no.ToString();
                string ret = Util.GetWebContent(url);
                WanlongZiwoyouHelper.ZiwoyouOrder order = JsonConvert.DeserializeObject<WanlongZiwoyouHelper.ZiwoyouOrder>(ret);

                //WanlongZiwoyouHelper.ZiwoyouOrder order =  _zwHelper.GetOrder(int.Parse(skipass.reserve_no.Trim()));

                if (order.orderState == 4)
                {
                    skipass.is_used = 1;
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _tHelper.ActiveTicket((int)skipass.order_id);
                }
            }
            await _context.SaveChangesAsync();
        }

        

        [HttpGet]
        public async Task RefreshAutoReserve()
        {
            List<Models.SkiPass.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.reserve_no != null && !s.resort.Trim().Equals("南山")
                && s.card_no == null && s.qr_code_url == null && s.send_content == null && s.is_cancel == 0
                && s.create_date > DateTime.Now.AddHours(-1))).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassList[i];
                Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(skipass.product_id);
                if (skipassProduct == null)
                {
                    continue;
                }
                try
                {
                    WanlongZiwoyouHelper _zwHelper = new WanlongZiwoyouHelper(_context, _config, skipassProduct.source.Trim());
                    WanlongZiwoyouHelper.ZiwoyouOrder order = _zwHelper.GetOrder(int.Parse(skipass.reserve_no));
                    if (order == null)
                    {
                        skipass.is_cancel = -2;
                        skipass.update_date = DateTime.Now;
                        
                        _context.skiPass.Entry(skipass).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        continue;
                    }
                    if (order.orderState != 2 || order.vouchers == null || order.vouchers.Length <= 0)
                    {
                        continue;
                    }
                    if (order.vouchers[0].code != null && !order.vouchers[0].code.Trim().Equals(""))
                    {
                        skipass.card_no = order.vouchers[0].code.Trim();
                    }
                    if (order.vouchers[0].qrcodeUrl != null && !order.vouchers[0].qrcodeUrl.Trim().Equals(""))
                    {
                        skipass.qr_code_url = order.vouchers[0].qrcodeUrl.Trim();
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
                    skipass.is_cancel = -2;
                    skipass.update_date = DateTime.Now;
                }
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
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
            int count, string cell, string name, string sessionKey, string sessionType = "wechat_mini_openid", string idNo = "", int refereeMemberId = 0)
        {
            Models.Product.Product product = await _context.Product.FindAsync(productId);
            Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(productId);
            SkipassDailyPrice dailyPrice = await _context.skipassDailyPrice
                .Where(s => s.product_id == productId && s.valid == 1 && s.reserve_date.Date == date.Date)
                .OrderBy(s => s.reserve_date).AsNoTracking().FirstAsync();
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || product == null)
            {
                return BadRequest();
            }
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            double totalPrice = 0;
            Models.SkiPass.SkiPass skipass = new Models.SkiPass.SkiPass()
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
                reserve_date = date.Date
            };
                //skipassArr[i] = skipass;
                //totalPrice += (double)skipass.deal_price;
            

            OrderOnline order = new OrderOnline()
            {
                type = "雪票",
                shop = product.shop.Trim(),
                order_price = (double)skipass.deal_price,
                order_real_pay_price = (double)skipass.deal_price,
                final_price = (double)skipass.deal_price,
                open_id = member.wechatMiniOpenId,
                staff_open_id = "",
                memo = "",
                pay_method = "微信支付",
                referee_member_id = refereeMemberId
            };
            await _context.OrderOnlines.AddAsync(order);
            await _context.SaveChangesAsync();
            string outTradeNo = "";
            if (order.shop.Trim().Equals("南山"))
            {
                outTradeNo = "NS";
            }
            else
            {
                outTradeNo = "QJ";
            }
            outTradeNo += "_XP_" + DateTime.Now.ToString("yyyyMMdd") + "_" + order.id.ToString().PadLeft(6, '0') + "_ZF_01";

            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = order.pay_method.Trim(),
                amount = order.final_price,
                status = "待支付",
                staff_open_id = "",
                out_trade_no = outTradeNo
            };
            await _context.OrderPayment.AddAsync(payment);

            

            skipass.order_id = order.id;
            await _context.skiPass.AddAsync(skipass);
            
            await _context.SaveChangesAsync();

            

            
            


            order.payments = new OrderPayment[] { payment };

            member.real_name = name;
            _context.member.Entry(member).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await _memberHelper.UpdateDetailInfo(member.id, cell, "cell", false);

            if (refereeMemberId > 0)
            {
                Models.Order.Kol k = await _memberHelper.GetKol(refereeMemberId);
                PaymentShare share = new PaymentShare()
                {
                    id = 0,
                    payment_id = payment.id,
                    order_id = payment.order_id,
                    kol_id = k.id,
                    amount = 1,
                    memo = "雪票佣金",
                    state = 0,
                    ret_msg = "",
                    out_trade_no = payment.out_trade_no + "_FZ_" + DateTime.Now.ToString("yyyyMMdd") + "_01"
                };
                await _context.paymentShare.AddAsync(share);
                await _context.SaveChangesAsync();
            }

            return Ok(order);
        }
        [NonAction]
        public async Task CommitSkipass(int skipassId)
        {
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skipassId);
           
            if (skipass == null)
            {
                return;
            }
             List<Models.Order.OrderPayment> pl = await _context.OrderPayment
                .Where(p => p.order_id == skipass.order_id && p.status.Trim().Equals("支付成功"))
                .AsNoTracking().ToListAsync(); 
            if (skipass.order_id != null)
            {
                TicketController _tHelper = new TicketController(_context, _config);
                await _tHelper.ActiveTicket((int)skipass.order_id);

                var shareList = await _context.paymentShare
                    .Where(s => s.order_id == (int)skipass.order_id && s.state == 0 && s.submit_date == null)
                    .AsNoTracking().ToListAsync();
                if (shareList != null && shareList.Count > 0)
                {
                    OrderPaymentController _paymentHelper = new OrderPaymentController(_context, _config, _http);
                    for(int i = 0; i < shareList.Count; i++)
                    {
                        Models.Order.PaymentShare share = shareList[i];
                         
                        await _paymentHelper.SubmitShare(share.id);
                        await _paymentHelper.ShareFinish(share.payment_id, "雪票分账结束");

                    }
                    
                }
            }

            
            
        }


        [HttpGet]
        public async Task<ActionResult<List<SkipassWithPrice>>> GetProductsByResort(string resort, int showHidden = 0)
        {
            resort = Util.UrlDecode(resort);
            var l = await _context.SkiPass//.Include(s => s.dailyPrice)
                .Join(_context.Product, s=>s.product_id, p=>p.id,
                (s, p)=> new {s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop, 
                s.commonDayDealPrice, s.weekendDealPrice, 
                //s.dailyPrice, 
                //s.avaliablePriceList,
                p.sale_price, p.market_price, p.cost, p.type, p.hidden })
                .Where(p => p.type.Trim().Equals("雪票") && p.resort.Trim().Equals(resort)
                && p.third_party_no != null 
                && ((p.hidden == 0 && showHidden == 0) || (showHidden == 1))
                ).OrderBy(p => p.market_price).AsNoTracking().ToListAsync();
            
            List<SkipassWithPrice> ret = new List<SkipassWithPrice>();
            for(int i = 0; i < l.Count; i++)
            {
                var p = l[i];
                Models.Product.SkiPass skipass = new Models.Product.SkiPass()
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
            Product p = await _context.Product.FindAsync(productId);
            Models.Product.SkiPass skipass = await _context.SkiPass.FindAsync(productId);
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
            MemberController _memberHelper = new MemberController(_context, _config);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
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
    }
}

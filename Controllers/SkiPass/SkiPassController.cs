using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
using SnowmeetApi.Helpers;
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

        /// <summary>
        /// 雪场名 → 雪票分类 id。分类表里 biz_type = "雪票" 的两条是「南山雪票」(0301) / 「万龙雪票」(0302)。
        ///
        /// 2026-08-19 之前这里是拿 product.shop 去比 resort —— 那是**门店**比**雪场**，
        /// 只因为南山的门店名和雪场名恰好相同才没出事；崇礼旗舰店卖的是万龙雪场的票
        /// （product.shop = "崇礼旗舰店"、resort = "万龙"），resort="万龙" 一条也查不出来。
        /// 生产实测：按分类筛 南山 80 条 / 万龙 74 条，与按 skiPassProduct.resort 筛逐条相同。
        /// </summary>
        [NonAction]
        private async Task<int?> ResolveSkiPassCategoryId(string resort)
        {
            string name = (resort ?? "").Trim() + "雪票";
            return await _context.category.AsNoTracking()
                .Where(c => c.biz_type == "雪票" && c.name == name && c.valid == 1)
                .Select(c => (int?)c.id).FirstOrDefaultAsync();
        }

        /// <summary>
        /// 商品的门店名，以 shop_id 关联 shop_list 为准，product.shop 文本只作兜底。
        /// 这个名字要写进 order.shop，再由 GetMchId 按子串判定选微信商户号，不能取自自由文本列。
        /// </summary>
        [NonAction]
        private async Task<string> ResolveProductShopName(Models.Product p)
        {
            if (p == null)
            {
                return "";
            }
            if (p.shop_id == null)
            {
                return "";
            }
            Models.Shop sh = await _context.shop.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == p.shop_id);
            return sh == null ? "" : (sh.name ?? "").Trim();
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
            if ((date >= DateTime.Parse("2026-1-1") && date <= DateTime.Parse("2026-1-2"))
            || (date >= DateTime.Parse("2026-2-15") && date <= DateTime.Parse("2026-2-23")) )
            {
                tags = tags.Replace("平日", "节假日").Replace("周六", "节假日").Replace("周日", "节假日").Replace("周末", "节假日");
            }
            if (date.Date == DateTime.Parse("2026-2-14").Date || date.Date == DateTime.Parse("2026-2-28").Date)
            {
                tags = tags.Replace("节假日", "平日").Replace("周六", "平日").Replace("周日", "平日").Replace("周末", "平日");
            }
            string[] tagArr = tags == null ? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');
            int? skiPassCategoryId = await ResolveSkiPassCategoryId(resort);
            var skiPassProdustList = await _context.product.Where(p => (p.category_id == skiPassCategoryId && p.hidden == 0 && p.end_date >= DateTime.Now.Date))
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
            List<Models.SkiPass> l = await _context.skiPass.Where(s => s.valid == 1
                && s.member_id == member.id)
                .OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            
            return Ok(l);
        }

        

        // 每张票的退票、退款、出票失败等关键步骤都留一条记录（core_data_mod_log，table_name=ski_pass，key_value=票号）：
        // 谁、什么时候、向供应商提交了什么、供应商反馈了什么、我们据此做了什么。供应商每次接口调用的原始请求和返回
        // 另外在 web_api_log 里（按供应商订单号能对上）。写记录失败不能影响业务本身。
        [NonAction]
        private async Task LogSkipassEvent(Models.SkiPass skipass, string scene, int? memberId, int? staffId, string detail)
        {
            try
            {
                CoreDataModLog log = CoreDataModLog.CreateManualLog("ski_pass", "", skipass.id, scene, memberId, staffId, null, null, detail);
                await _context.coreDataModLog.AddAsync(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception err)
            {
                Console.WriteLine("[雪票记录] 写入失败 skipass=" + skipass.id + " " + scene + " " + err.Message);
            }
        }

        // 供应商对「取消订单」的反馈
        public sealed record SupplierCancelFeedback(int CancelState, string Msg, string? CancelId, double? CancelMoney);

        // 向供应商提交取消；测试里替换成假的
        public Func<Models.SkiPass, string, Task<SupplierCancelFeedback>>? SupplierCancelSubmitter { get; set; }

        [NonAction]
        private async Task<SupplierCancelFeedback> SubmitSupplierCancel(Models.SkiPass skipass, string source)
        {
            if (SupplierCancelSubmitter != null)
            {
                return await SupplierCancelSubmitter(skipass, source);
            }
            WanlongZiwoyouHelper helper = new WanlongZiwoyouHelper(_context, _config, source);
            WanlongZiwoyouHelper.ZiwoyouQueryResult r = await helper.CancelOrder(int.Parse(skipass.reserve_no));
            WanlongZiwoyouHelper.ZiwoyouCancel cancel = (WanlongZiwoyouHelper.ZiwoyouCancel)r.data;
            return new SupplierCancelFeedback(cancel.cancelState, (r.msg ?? "").Trim(), cancel.cancelId, cancel.cancelMoney);
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
            if (!SkipassSaleRules.CanRefundBeforeIssued(skipass.reserve_no, skipass.is_cancel))
            {
                return BadRequest("出票中，暂时不能退票，请等出票结果");
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
            await LogSkipassEvent(skipass, "退票申请提交", member.id, staff?.id,
                "向供应商提交取消，供应商订单号 " + skipass.reserve_no);

            try
            {
                Models.SkiPassProduct product = await _context.skiPassProduct.FindAsync(skipass.product_id);
                SupplierCancelFeedback feedback = await SubmitSupplierCancel(skipass, product.source.Trim());
                skipass.is_cancel = feedback.CancelState;
                skipass.memo += " " + feedback.Msg;
                await LogSkipassEvent(skipass, "供应商退票反馈", member.id, staff?.id,
                    "cancelState=" + feedback.CancelState + "（1 即时确认，2 提交待审核），msg=" + feedback.Msg
                    + "，cancelId=" + feedback.CancelId + "，cancelMoney=" + feedback.CancelMoney);
                if (feedback.CancelState == 1)
                {
                    // 供应商同意取消就算退票成功：直接退这一张的票款。不再回头查一次订单状态——刚确认时查到的可能还是旧状态，
                    // 那样退款会被误拦，而且之后没有人补退
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    await AutoRefundCancelled(skipass, "退票即时确认退款", staff?.id);
                    return Ok(skipass);
                }
                
            }
            catch(Exception err)
            {
                skipass.is_cancel = -1;
                skipass.memo = "取消出错 " + err.ToString();
                await LogSkipassEvent(skipass, "供应商退票出错", member.id, staff?.id, err.Message);
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
            if (!SkipassSaleRules.CanRefundBeforeIssued(skipass.reserve_no, skipass.is_cancel))
            {
                return BadRequest("出票中，暂时不能退款，请等出票结果");
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
            // 只退这一张票的钱（一单多张的购物车订单）；已经为这张票发起过退款的不再退
            int validPasses = await _context.skiPass.CountAsync(s => s.order_id == skipass.order_id && s.valid == 1);
            double amount = SkipassSaleRules.RefundAmountFor(validPasses, skipass.deal_price ?? 0, pL[0].amount);
            bool refundExists = validPasses > 1
                ? await _context.orderPaymentRefund.AnyAsync(r => r.skipass_id == skipassId)
                : await _context.orderPaymentRefund.AnyAsync(r => r.payment_id == paymentId);
            if (refundExists)
            {
                return BadRequest("这张票已经发起过退款");
            }
            var refundResult = await _refundHelper.Refund(paymentId, amount, reason, sessionKey, sessionType, skipassId);
            if (refundResult.Result is not OkObjectResult refundOk || refundOk.Value is not OrderPaymentRefund refund)
            {
                return BadRequest("退款被拒绝");
            }
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

        // 向自我游真实下单、扣预存款。只能由 CreateSkiPass 等服务端流程调用：
        // 它原来是公开的 [HttpGet] 且参数是整个 SkiPass 对象，外部可以自己拼一个对象绕过 ReserveSkiPass 的店员限制。
        // 取票二维码图片：由我们的服务器去供应商那里取，再转给小程序（小程序真机不能直接加载供应商域名的图片）。
        // 只给这张票的购买者本人或在职店员；只取供应商域名下的 https 图片。图片内容不会变，按地址缓存在内存里。
        public Func<string, Task<(byte[] Bytes, string ContentType)>>? QrFetcher { get; set; }
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> QrCache = new();
        const int QR_CACHE_MAX = 300;

        [HttpGet]
        public async Task<IActionResult> QrImage(int skipassId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Member? member = string.IsNullOrWhiteSpace(sessionKey) ? null : await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            Models.SkiPass? skipass = await _context.skiPass.Where(s => s.id == skipassId && s.valid == 1).AsNoTracking().FirstOrDefaultAsync();
            if (skipass == null)
            {
                return NotFound();
            }
            if (skipass.member_id != member.id && !SkipassSaleRules.CanReserve(await GetStaffOrNull(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            string url = (skipass.qr_code_url ?? "").Trim();
            if (url == "")
            {
                return NotFound();
            }
            if (!SkipassSaleRules.IsAllowedQrImageUrl(url))
            {
                return BadRequest();
            }
            if (!QrCache.TryGetValue(url, out var image))
            {
                try
                {
                    image = QrFetcher != null ? await QrFetcher(url) : await FetchQrImage(url);
                }
                catch (Exception err)
                {
                    Console.WriteLine("[取票二维码] 取图失败 skipass=" + skipassId + " " + err.Message);
                    return StatusCode(502);
                }
                if (QrCache.Count >= QR_CACHE_MAX)
                {
                    QrCache.Clear();
                }
                QrCache[url] = image;
            }
            if (HttpContext != null)
            {
                Response.Headers["Cache-Control"] = "private, max-age=600";
            }
            return File(image.Bytes, image.ContentType);
        }

        [NonAction]
        private static async Task<(byte[] Bytes, string ContentType)> FetchQrImage(string url)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            if (!contentType.StartsWith("image/"))
            {
                throw new InvalidOperationException("不是图片：" + contentType);
            }
            return (await response.Content.ReadAsByteArrayAsync(), contentType);
        }

        // 同一张票同一时间只允许一处在出票（付款回调、后台重试可能同时碰到）
        static readonly HashSet<int> IssuingNow = new HashSet<int>();

        // 供应商那边这张票对应订单的核对结果（超过 30 分钟没出成、准备确认失败之前用）
        public sealed record SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind Kind, string? OrderId);

        // 按我方单号去供应商核对这张票有没有订单；测试里替换成假的
        public Func<Models.SkiPass, string, Task<SupplierOrderCheck>>? SupplierOrderChecker { get; set; }
        // 取消供应商订单并确认它已变成「已取消」才返回 true；测试里替换成假的
        public Func<string, string, Task<bool>>? SupplierOrderCanceller { get; set; }

        // 这张票向供应商下单用的我方单号（出票和事后核对必须一致）
        [NonAction]
        private async Task<string> SupplierSourceIdOf(Models.SkiPass skipass)
        {
            OrderPayment? payment = await _context.OrderPayment
                .Where(p => p.order_id == skipass.order_id && p.status.Trim().Equals("支付成功") && p.out_trade_no != null && p.out_trade_no.Trim() != "")
                .OrderBy(p => p.id).AsNoTracking().FirstOrDefaultAsync();
            string outTradeNo = payment != null ? payment.out_trade_no.Trim() : skipass.id.ToString() + Util.GetLongTimeStamp(DateTime.Now);
            int passesInOrder = await _context.skiPass.CountAsync(s => s.order_id == skipass.order_id && s.valid == 1);
            return SkipassSaleRules.SupplierSourceId(outTradeNo, passesInOrder, skipass.id);
        }

        [NonAction]
        private async Task<SupplierOrderCheck> CheckSupplierOrder(Models.SkiPass skipass, string source)
        {
            if (SupplierOrderChecker != null)
            {
                return await SupplierOrderChecker(skipass, source);
            }
            try
            {
                string sourceId = await SupplierSourceIdOf(skipass);
                var helper = new WanlongZiwoyouHelper(_context, _config, source);
                DateTime start = skipass.create_date.Date.AddDays(-1);
                DateTime end = DateTime.Now.Date.AddDays(1);
                for (int page = 0; page < 50; page++)
                {
                    var listResult = await helper.GetOrderListByPage(start, end, page);
                    if (listResult.Result is not OkObjectResult ok || ok.Value is not WanlongZiwoyouHelper.ZiwoyouQueryList list)
                    {
                        return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Unknown, null);
                    }
                    var hit = list.results?.FirstOrDefault(o => o.orderSourceId == sourceId);
                    if (hit != null)
                    {
                        if (!int.TryParse(hit.orderId, out int orderNo))
                        {
                            return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Unknown, null);
                        }
                        var detail = await helper.GetOrder(orderNo);
                        if (detail.Result is not OkObjectResult dok || dok.Value is not WanlongZiwoyouHelper.ZiwoyouOrder order)
                        {
                            return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Unknown, hit.orderId);
                        }
                        if (order.orderState == 3)
                        {
                            return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Cancelled, hit.orderId);
                        }
                        bool hasCode = order.vouchers != null && order.vouchers.Any(v => !string.IsNullOrWhiteSpace(v.code));
                        return new SupplierOrderCheck(hasCode ? SkipassSaleRules.SupplierOrderKind.LiveWithCode
                            : SkipassSaleRules.SupplierOrderKind.LiveWithoutCode, hit.orderId);
                    }
                    if (page + 1 >= list.pageCount)
                    {
                        return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.NoOrder, null);
                    }
                }
                return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Unknown, null); // 页数太多没翻完，不能当成没有
            }
            catch (Exception err)
            {
                Console.WriteLine("[出票核对] 查供应商订单失败 skipass=" + skipass.id + " " + err.Message);
                return new SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Unknown, null);
            }
        }

        [NonAction]
        private async Task<bool> CancelSupplierOrderConfirmed(string orderId, string source)
        {
            if (SupplierOrderCanceller != null)
            {
                return await SupplierOrderCanceller(orderId, source);
            }
            try
            {
                if (!int.TryParse(orderId, out int orderNo)) return false;
                var helper = new WanlongZiwoyouHelper(_context, _config, source);
                await helper.CancelOrder(orderNo);
                var detail = await helper.GetOrder(orderNo);
                return detail.Result is OkObjectResult ok && ok.Value is WanlongZiwoyouHelper.ZiwoyouOrder order && order.orderState == 3;
            }
            catch (Exception err)
            {
                Console.WriteLine("[出票核对] 取消供应商订单失败 order=" + orderId + " " + err.Message);
                return false;
            }
        }

        // 出票。结果只有三种：
        //   成功——拿到供应商订单号 reserve_no；
        //   确认失败（供应商拒绝、预存款不够、支付被拒、返回别的票的订单号）——停止出票、必要时取消供应商订单、自动退这一张的款；
        //   不确定（网络异常等）——留在「出票中」，后台每分钟重试，30 分钟还不行就算确认失败（RefreshIssuing）。
        // 出票中的票不允许退票、退款。
        [NonAction]
        public async Task AutoReserve(Models.SkiPass skipass)
        {
            lock (IssuingNow)
            {
                if (!IssuingNow.Add(skipass.id)) return;
            }
            try
            {
                await AutoReserveOnce(skipass);
            }
            catch (Exception err)
            {
                skipass.memo += " 出票异常，稍后重试：" + err.Message;
                try
                {
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                catch (Exception saveErr)
                {
                    Console.WriteLine("[出票] 记录异常失败 skipass=" + skipass.id + " " + saveErr.Message);
                }
            }
            finally
            {
                lock (IssuingNow)
                {
                    IssuingNow.Remove(skipass.id);
                }
            }
        }

        // 确认这张票不可能出了：停止出票（供应商那边已有订单号的取消掉）、标成出票失败、自动退这一张的款
        [NonAction]
        private async Task MarkBookingFailed(Models.SkiPass skipass, string reason, string? supplierOrderId, string? source)
        {
            skipass.memo += " 出票失败：" + reason;
            skipass.is_cancel = -2;
            skipass.update_date = DateTime.Now;
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await LogSkipassEvent(skipass, "确认出票失败", null, null, reason);
            if (!string.IsNullOrWhiteSpace(supplierOrderId) && int.TryParse(supplierOrderId, out int orderNo))
            {
                try
                {
                    await new WanlongZiwoyouHelper(_context, _config, source ?? "万龙自我游").CancelOrder(orderNo);
                }
                catch (Exception err)
                {
                    Console.WriteLine("[出票失败] 取消供应商订单失败 skipass=" + skipass.id + " order=" + supplierOrderId + " " + err.Message);
                }
            }
            await AutoRefundFailedBooking(skipass, "出票失败退款");
        }

        [NonAction]
        private async Task AutoReserveOnce(Models.SkiPass skipass)
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
            // 一单多张票（购物车）时每张票单独一个我方单号，否则自我游把第 2、3… 张当成重复下单，
            // 返回第一张的订单号，这几张票就一直没有出成。单号固定，所以重试同一张票不会重复出票
            string outTradeNo = await SupplierSourceIdOf(skipass);
            double balance = await _zwHelper.QueryBalance();
            //balance = 10000;
            if (balance <= skipass.deal_price)
            {
                // 没向供应商下单，顾客的钱要退回
                await MarkBookingFailed(skipass, "账户余额不足", null, null);
                return;
            }
            Models.WanLong.ZiwoyouPlaceOrderResult orderResult =
                await _zwHelper.PlaceOrder(skipassProduct.third_party_no, skipass.contact_name, skipass.contact_cell,
                skipass.contact_id_type, skipass.contact_id_no, skipass.count, (DateTime)skipass.reserve_date,
                "", outTradeNo);
            if (orderResult.state != 1)
            {
                await MarkBookingFailed(skipass, (orderResult.msg ?? "供应商拒绝下单").Trim(), null, null);
                return;
            }
            string orderId = orderResult.data.orderId;
            // 自我游返回的订单号已经属于另一张票：说明它把这次下单当成了重复，没有新建订单
            if (await _context.skiPass.AnyAsync(s => s.reserve_no == orderId && s.id != skipass.id))
            {
                await MarkBookingFailed(skipass, "自我游返回了已存在的订单号 " + orderId + "，没有新建订单", null, null);
                return;
            }
            Models.WanLong.PayResult payResult = await _zwHelper.Pay(int.Parse(orderId));
            // 「已经支付成功」是同一个单号重试时的正常回答（上一次已经付过），也算成功
            if (payResult.state != 1 || !(payResult.msg ?? "").Contains("支付成功"))
            {
                // 供应商那边已经建了订单但没付成：先取消它，确认变成「已取消」才退款；没确认就先不退，
                // 留在出票中，后台一分钟后再试，30 分钟内还没有结果会按核对流程处理
                if (await CancelSupplierOrderConfirmed(orderId, skipassProduct.source))
                {
                    await MarkBookingFailed(skipass, "供应商支付没有成功：" + (payResult.msg ?? "").Trim(), null, null);
                }
                else
                {
                    skipass.memo += " 供应商支付没有成功（" + (payResult.msg ?? "").Trim() + "），订单 " + orderId + " 还没确认取消，稍后再核对";
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
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
            // 取票码由后台轮询（SkipassCancelPoller 每分钟调 RefreshAutoReserve）补上。
            // 这里原来睡 25 秒再查，买 N 张票就要睡 N 个 25 秒，把付款回调拖得很长。
        }

        // 超过 30 分钟还没出成：先去供应商核对这张票到底有没有订单，再决定（见 SkipassSaleRules.DecideGiveUp）。
        // 核对不了就什么都不做，下一分钟再来，不会在没把握的时候退款。
        [NonAction]
        private async Task ConfirmIssuingOutcome(Models.SkiPass skipass)
        {
            Models.SkiPassProduct? product = await _context.skiPassProduct.FindAsync(skipass.product_id);
            string source = product?.source ?? "万龙自我游";
            SupplierOrderCheck check = await CheckSupplierOrder(skipass, source);
            switch (SkipassSaleRules.DecideGiveUp(check.Kind))
            {
                case SkipassSaleRules.GiveUpAction.ConfirmFailedAndRefund:
                    await MarkBookingFailed(skipass, "超过 30 分钟仍没有出票成功，供应商那边没有这张票的有效订单", null, null);
                    break;
                case SkipassSaleRules.GiveUpAction.RecordIssued:
                    // 其实出成了，只是我们没记上：补记订单号，取票码由 RefreshAutoReserve 补，不退款
                    skipass.reserve_no = check.OrderId;
                    skipass.memo += " 超时后核对供应商订单，补记订单号 " + check.OrderId;
                    skipass.update_date = DateTime.Now;
                    _context.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    await LogSkipassEvent(skipass, "补记供应商订单号", null, null, "出票超过 30 分钟，核对发现供应商已有订单 " + check.OrderId + " 且有取票码，补记，不退款");
                    break;
                case SkipassSaleRules.GiveUpAction.CancelThenConfirm:
                    if (!string.IsNullOrWhiteSpace(check.OrderId) && await CancelSupplierOrderConfirmed(check.OrderId, source))
                    {
                        await MarkBookingFailed(skipass, "超过 30 分钟仍没有出票成功，已取消供应商订单 " + check.OrderId, null, null);
                    }
                    break;
                default:
                    Console.WriteLine("[出票核对] 暂时核对不了供应商订单，不退款 skipass=" + skipass.id);
                    break;
            }
        }

        // 后台每分钟调一次：付款了还没出成的票，30 分钟内重试，超过 30 分钟确认出票失败并自动退款。
        // 另外给已经是出票失败、还没退款的票补退（近两天内）。
        [HttpGet]
        public async Task RefreshIssuing()
        {
            DateTime now = DateTime.Now;
            List<Models.SkiPass> stuck = await _context.skiPass
                .Where(s => s.valid == 1 && s.booking_now && s.is_cancel == 0 && s.reserve_no == null
                    && !s.resort.Trim().Equals("南山") && s.create_date > now.AddDays(-2)).ToListAsync();
            foreach (Models.SkiPass skipass in stuck)
            {
                try
                {
                    DateTime startedAt = skipass.update_date ?? skipass.create_date;
                    switch (SkipassSaleRules.DecideIssuing(startedAt, now))
                    {
                        case SkipassSaleRules.IssuingAction.Retry:
                            await AutoReserve(skipass);
                            break;
                        case SkipassSaleRules.IssuingAction.GiveUp:
                            await ConfirmIssuingOutcome(skipass);
                            break;
                        default:
                            break;
                    }
                }
                catch (Exception err)
                {
                    Console.WriteLine("[出票重试] 处理失败 skipass=" + skipass.id + " " + err.Message);
                }
            }
            List<Models.SkiPass> failed = await _context.skiPass
                .Where(s => s.valid == 1 && s.is_cancel == -2 && (s.reserve_no == null || s.reserve_no == "")
                    && s.have_refund == null && s.refund_amount == null && !s.resort.Trim().Equals("南山")
                    && s.create_date > now.AddDays(-2)).ToListAsync();
            foreach (Models.SkiPass skipass in failed)
            {
                try
                {
                    await AutoRefundFailedBooking(skipass, "出票失败退款");
                }
                catch (Exception err)
                {
                    Console.WriteLine("[出票失败退款] 处理失败 skipass=" + skipass.id + " " + err.Message);
                }
            }
        }

        // 退款成功后的雪票后续。refundId 是刚成功的那笔退款：购物车订单一单多张票，要写到这笔退款对应的那张票上
        // （退款行的 skipass_id），不能取「订单里最新一张」；没有 skipass_id 的老退款沿用原来的取法。
        // 订单挂的赠券只在这单的票都已处理完（没有还在正常持有的票）时才取消，取消一张票不能让另几张票的赠券作废。
        [NonAction]
        public async Task RefundCallBack(int paymentId, int? refundId = null)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);

            OrderPaymentRefund? refund = null;
            if (refundId != null)
            {
                refund = await _context.orderPaymentRefund.Where(r => r.id == refundId).AsNoTracking().FirstOrDefaultAsync();
            }
            if (refund == null)
            {
                refund = await _context.orderPaymentRefund
                    .Where(p => p.order_id == payment.order_id && p.state == 1)
                    .AsNoTracking().FirstAsync();
            }
            Models.SkiPass skipass = refund.skipass_id != null
                ? await _context.skiPass.FindAsync(refund.skipass_id)
                : await _context.skiPass
                    .Where(s => (s.valid == 1 && s.order_id == payment.order_id))
                    .OrderByDescending(s => s.id).FirstAsync();

            int stillHeld = await _context.skiPass.CountAsync(s => s.order_id == payment.order_id && s.valid == 1
                && s.is_cancel == 0 && s.id != skipass.id);
            if (stillHeld == 0)
            {
                TicketController _ticketHelper = new TicketController(_context, _config);
                await _ticketHelper.Cancel(payment.order_id);
            }

            if (!skipass.resort.Trim().Equals("南山"))
            {
                skipass.refund_amount = refund.amount;
                skipass.update_date = DateTime.Now;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                await LogSkipassEvent(skipass, "退款成功", null, null, "微信确认退款到账，退 " + refund.amount + " 元，退款单 " + refund.out_refund_no);
            }
        }

        // 自我游确认取消后自动退款，不需要店员会话。已发起过退款（payment_refund 有任何一行）的不再自动退。
        // 退款成功后由微信回调的 RefundCallBack 补退款金额。返回是否发起了退款。
        // 进程内同一时间只允许一个自动退款在跑：取消接口、定时轮询、手动触发 RefreshCancel 可能同时处理同一张票，
        // 没有这把锁两边都先查到「没有退款行」再各发一笔
        static readonly SemaphoreSlim AutoRefundLock = new SemaphoreSlim(1, 1);

        [NonAction]
        public async Task<bool> AutoRefundCancelled(Models.SkiPass skipass, string reason, int? staffId)
        {
            await AutoRefundLock.WaitAsync();
            try
            {
                return await AutoRefundCancelledLocked(skipass, reason, staffId);
            }
            finally
            {
                AutoRefundLock.Release();
            }
        }

        // 预订失败（供应商没接单）的票：顾客已经付款，自动退这一张的钱
        [NonAction]
        public async Task<bool> AutoRefundFailedBooking(Models.SkiPass skipass, string reason)
        {
            await AutoRefundLock.WaitAsync();
            try
            {
                bool allowed = SkipassSaleRules.NeedsAutoRefundForFailedBooking(skipass.is_cancel, skipass.valid ?? 0,
                    skipass.resort, skipass.reserve_no, skipass.have_refund, skipass.refund_amount);
                return await RefundSkipassRow(skipass, reason, null, allowed);
            }
            finally
            {
                AutoRefundLock.Release();
            }
        }

        [NonAction]
        private async Task<bool> AutoRefundCancelledLocked(Models.SkiPass skipass, string reason, int? staffId)
        {
            bool allowed = SkipassSaleRules.NeedsAutoRefund(skipass.is_cancel, skipass.valid ?? 0, skipass.resort, skipass.have_refund, skipass.refund_amount);
            return await RefundSkipassRow(skipass, reason, staffId, allowed);
        }

        // 退一张票的款。金额只退这一张的成交价（一单多张票的购物车订单），老的一单一张沿用整笔支付额；
        // 退款行记下 skipass_id，防重复按票判断：一单多张看这张票有没有退款行，一单一张看这笔支付有没有退款行。
        // 调用方负责持有 AutoRefundLock。累计退款不超过支付额由 RefundCore 校验。
        [NonAction]
        private async Task<bool> RefundSkipassRow(Models.SkiPass skipass, string reason, int? staffId, bool allowed)
        {
            if (!allowed)
            {
                return false;
            }
            OrderPayment? payment = await _context.OrderPayment
                .Where(p => p.order_id == skipass.order_id && p.status.Trim().Equals("支付成功"))
                .OrderBy(p => p.id).AsNoTracking().FirstOrDefaultAsync();
            if (payment == null)
            {
                return false;
            }
            int validPasses = await _context.skiPass.CountAsync(s => s.order_id == skipass.order_id && s.valid == 1);
            double amount = SkipassSaleRules.RefundAmountFor(validPasses, skipass.deal_price ?? 0, payment.amount);
            if (amount <= 0)
            {
                return false;
            }
            bool refundExists = validPasses > 1
                ? await _context.orderPaymentRefund.AnyAsync(r => r.skipass_id == skipass.id)
                : await _context.orderPaymentRefund.AnyAsync(r => r.payment_id == payment.id);
            if (refundExists)
            {
                return false;
            }
            OrderPaymentRefund? refund = await _refundHelper.RefundCore(payment, amount, reason, staffId, "系统自动退款", skipass.id);
            if (refund == null || string.IsNullOrEmpty(refund.refund_id))
            {
                await LogSkipassEvent(skipass, "退款未发出", null, staffId,
                    "原因：" + reason + "，应退 " + amount + " 元，" + (refund == null ? "被拒绝（支付状态不对或超过可退金额）" : "没有拿到微信退款号，退款单 " + refund.out_refund_no));
                return false;
            }
            await LogSkipassEvent(skipass, "发起退款", null, staffId,
                "原因：" + reason + "，退 " + amount + " 元，退款单 " + refund.out_refund_no + "，微信退款号 " + refund.refund_id);
            skipass.have_refund = 1;
            skipass.update_date = DateTime.Now;
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return true;
        }

        // poll=1 是后台轮询（SkipassCancelPoller，每分钟一次）：按取消申请后的时间决定这一轮查不查；
        // 不带 poll 是手动或外部调用，全部都查。
        [HttpGet]
        public async Task RefreshCancel(int poll = 0)
        {
            // 取消中（自我游需要审核）的，查到已取消就改成已取消并自动退款；
            // 已取消但没退款的（以前这里只改状态不退款）近两天内的也补退，退款行存在的不会重复退
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.reserve_no != null && !s.resort.Trim().Equals("南山")
                && (s.is_cancel == 2 || (s.is_cancel == 1 && s.have_refund == null && s.refund_amount == null
                    && s.create_date > DateTime.Now.AddDays(-2))))).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                DateTime now = DateTime.Now;
                // 取消申请时间：Cancel 里记在 card_member_return_time，没有就退回到下单时间
                DateTime requestedAt = skipass.card_member_return_time ?? skipass.create_date;
                if (poll == 1 && skipass.is_cancel == 2 && !SkipassSaleRules.ShouldPollCancel(requestedAt, now))
                {
                    continue;
                }
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
                        await LogSkipassEvent(skipass, "供应商确认已取消", null, null,
                            "后台核对供应商订单 " + skipass.reserve_no + " 状态为 3（已取消）");
                        await AutoRefundCancelled(skipass, "退票确认退款", null);
                    }
                    else if (skipass.is_cancel == 2 && SkipassSaleRules.CancelTimedOut(requestedAt, now))
                    {
                        // 等了 24 小时自我游还没确认：标成取消失败，交给店员人工处理，不自动退款
                        skipass.is_cancel = -1;
                        skipass.memo = (skipass.memo ?? "") + " 自我游超过24小时未确认取消，需人工处理";
                        skipass.update_date = now;
                        _context.skiPass.Entry(skipass).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        Console.WriteLine("[雪票取消] 超时未确认 skipass=" + skipass.id + " reserve_no=" + skipass.reserve_no);
                    }
                    
                }
                catch (Exception err)
                {
                    Console.WriteLine("[雪票取消] 处理失败 skipass=" + skipass.id + " " + err.Message);
                }
            }
        }
        // recentDays > 0（后台轮询用）：只查游玩日期是今天、或最近 recentDays 天内的票——游玩日期还没到的票不可能取票，
        // 没必要每次都去供应商一张张查；不带参数是原来的行为，查全部没使用的票。
        [HttpGet]
        public async Task RefreshUsedAll(int recentDays = 0)
        {
            TicketController _tHelper = new TicketController(_context, _config);
            OrderShareController _shareHelper = new OrderShareController(_context, _config, _http);
            DateTime today = DateTime.Now.Date;
            DateTime from = recentDays > 0 ? today.AddDays(-recentDays) : DateTime.MinValue;
            DateTime toExclusive = recentDays > 0 ? today.AddDays(1) : DateTime.MaxValue; // 不能对 MaxValue 再 AddDays，会溢出
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Where(s => (s.valid == 1 && s.is_cancel == 0 
                && s.is_used == 0 && !s.resort.Trim().Equals("南山")
                && (recentDays <= 0 || (s.reserve_date != null && s.reserve_date >= from && s.reserve_date < toExclusive))))
                .OrderByDescending(s => s.id).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass skipass = skipassList[i];
                if (skipass.reserve_no == null)
                {
                    continue;
                }
                string url = "https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrder?orderId=" + skipass.reserve_no.ToString();
                WanlongZiwoyouHelper.ZiwoyouOrder? order = null;
                try
                {
                    string ret = Util.GetWebContent(url);
                    order = JsonConvert.DeserializeObject<WanlongZiwoyouHelper.ZiwoyouOrder>(ret);
                }
                catch
                {
                    // 订单两个账号都查不到或接口报错：跳过这一条，不能中断后面所有订单的核销检查
                    continue;
                }
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
        // hours > 0 只处理最近这么多小时下的单：后台轮询每分钟调一次，不能每分钟都把历史上一直没有取票码的老票再查一遍
        [HttpGet]
        public async Task RefreshAutoReserve(int hours = 0)
        {
            DateTime since = hours > 0 ? DateTime.Now.AddHours(-hours) : DateTime.MinValue;
            List<Models.SkiPass> skipassList = await _context.skiPass
                .Include(s => s.order)
                    .ThenInclude(o => o.payments.Where(p => p.status.Equals("支付成功")))
                .Where(s => (s.valid == 1 && s.reserve_no != null && !s.resort.Trim().Equals("南山")
                && s.card_no == null && s.qr_code_url == null && s.send_content == null && s.is_cancel == 0
                && s.create_date > since
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
        /// <summary>
        /// 会话对应的在职店员；没带会话、会话无效、不是店员都返回 null。
        /// </summary>
        [NonAction]
        private async Task<Staff?> GetStaffOrNull(string? sessionKey, string sessionType)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return null;
            }
            return await Util.GetStaffBySessionKey(_context, sessionKey, sessionType);
        }

        // 向自我游查某个账号的预存款；测试里替换成假的，不发真实请求
        public Func<string, Task<double>>? BalanceProvider { get; set; }

        [NonAction]
        private async Task<double> QuerySourceBalance(string source)
        {
            if (BalanceProvider != null)
            {
                return await BalanceProvider(source);
            }
            return await new WanlongZiwoyouHelper(_context, _config, source).QueryBalance();
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date,
            int count, string cell, string name, string sessionKey, string sessionType = "wechat_mini_openid", string idNo = "", int refereeMemberId = 0, int? staffId = null)
        {
            // 只有在职店员能订票，顾客只能看价格。微信、支付宝小程序共用这个入口，在这里一处拦住；
            // 页面上隐藏支付栏只是体验，不能当作限制。
            if (!SkipassSaleRules.CanReserve(await GetStaffOrNull(sessionKey, sessionType)))
            {
                return BadRequest(SkipassSaleRules.STAFF_ONLY_MESSAGE);
            }
            // 供应商一单只能 1 张：多张按张数展开成多条记录（原来是一条记录写 count 张，会被供应商拒绝，价格还会乘两次）
            var items = new List<SkipassCartItem>
            {
                new SkipassCartItem { productId = productId, date = date, cell = cell ?? "", name = name ?? "", count = count, idNo = idNo }
            };
            return await ReserveTickets(items, staffId, sessionKey, sessionType);
        }

        // 购物车结算：一个订单、一笔付款，每张票一条 ski_pass 记录（张数都是 1）。
        // 总价、可订日期、预存款都在服务端核对，不信客户端传来的金额。
        [HttpPost]
        public async Task<ActionResult<object>> ReserveSkiPassCart([FromBody] SkipassCartRequest request, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            if (!SkipassSaleRules.CanReserve(await GetStaffOrNull(sessionKey, sessionType)))
            {
                return BadRequest(SkipassSaleRules.STAFF_ONLY_MESSAGE);
            }
            if (request == null)
            {
                return BadRequest("购物车是空的");
            }
            return await ReserveTickets(request.items, request.staffId, sessionKey, sessionType);
        }

        [NonAction]
        private async Task<ActionResult<object>> ReserveTickets(List<SkipassCartItem> cartItems, int? staffId, string sessionKey, string sessionType)
        {
            string? invalid = SkipassCartRules.Validate(cartItems);
            if (invalid != null)
            {
                return BadRequest(invalid);
            }
            List<SkipassCartItem> tickets = SkipassCartRules.Expand(cartItems);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest("会话无效");
            }

            // 逐张核对商品和当天价格行
            var rows = new List<(SkipassCartItem item, Models.Product product, Models.SkiPassProduct skipassProduct, SkipassDailyPrice price)>();
            foreach (SkipassCartItem item in tickets)
            {
                Models.Product? product = await _context.product.FindAsync(item.productId);
                Models.SkiPassProduct? skipassProduct = await _context.skiPassProduct.FindAsync(item.productId);
                if (product == null || skipassProduct == null)
                {
                    return BadRequest("票种不存在");
                }
                SkipassDailyPrice? price = await _context.skipassDailyPrice
                    .Where(p => p.product_id == item.productId && p.valid == 1 && p.reserve_date.Date == item.date.Date)
                    .OrderBy(p => p.reserve_date).AsNoTracking().FirstOrDefaultAsync();
                if (price == null || price.deal_price <= 0)
                {
                    return BadRequest(product.name + " " + item.date.ToString("MM-dd") + " 没有可订的价格");
                }
                rows.Add((item, product, skipassProduct, price));
            }

            // 每个自我游账号各自的预存款要够付它名下这些票的合计，不够整单不让付（付了款出不了票）
            foreach (var group in rows.GroupBy(r => (r.skipassProduct.source ?? "").Trim()))
            {
                double need = group.Sum(r => r.price.deal_price);
                double balance;
                try
                {
                    balance = await QuerySourceBalance(group.Key);
                }
                catch
                {
                    return BadRequest("查询" + group.Key + "预存款失败，请稍后再试");
                }
                if (!(need < balance))
                {
                    return BadRequest(group.Key + "预存款不足，暂时不能订票");
                }
            }

            if (member.real_name == null || member.real_name.Trim() == "")
            {
                member.real_name = tickets[0].name;
                _context.member.Entry(member).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                _context.member.Entry(member).State = EntityState.Detached;
                await _context.SaveChangesAsync();
            }

            var passes = new List<Models.SkiPass>();
            foreach (var r in rows)
            {
                passes.Add(new Models.SkiPass()
                {
                    member_id = member.id,
                    wechat_mini_openid = member.wechatMiniOpenId,
                    product_id = r.item.productId,
                    resort = r.skipassProduct.resort.Trim(),
                    product_name = r.product.name,
                    count = 1,
                    deal_price = r.price.deal_price,
                    ticket_price = r.price.settlementPrice,
                    deposit = r.product.deposit,
                    valid = 0,
                    contact_cell = r.item.cell,
                    contact_name = r.item.name,
                    contact_id_no = (r.item.idNo ?? "").Trim(),
                    contact_id_type = "身份证",
                    reserve_date = r.item.date.Date,
                    booking_now = false,
                    reserve_success = false
                });
            }
            double totalAmount = Math.Round(passes.Sum(p => (double)p.deal_price), 2);
            Models.Order order = new Models.Order()
            {
                id = 0,
                member_id = member.id,
                type = "雪票",
                shop = await ResolveProductShopName(rows[0].product),
                total_amount = totalAmount,
                paying_amount = totalAmount,
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
            order.skipasses = passes;
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
        public async Task<ActionResult<List<SkipassWithPrice>>> GetProductsByResort(string resort, int showHidden = 0,
            string? sessionKey = null, string sessionType = "wechat_mini_openid")
        {
            resort = Util.UrlDecode(resort);
            // 万龙自我游、大好河山两个账号的商品都能卖（试营业的票只有大好河山账号有售卖权限）
            string[] sellableSources = SkipassSaleRules.SellableSources;
            var l = await _context.skiPassProduct//.Include(s => s.dailyPrice)
                .Join(_context.product, s=>s.product_id, p=>p.id,
                (s, p)=> new {s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop_id,
                s.commonDayDealPrice, s.weekendDealPrice, 
                //s.dailyPrice, 
                //s.avaliablePriceList,
                p.sale_price, p.market_price, p.cost, p.type, p.hidden })
                .Where(p => p.type.Trim().Equals("雪票") && p.resort.Trim().Equals(resort)
                && p.third_party_no != null && sellableSources.Contains(p.source)
                && ((p.hidden == 0 && showHidden == 0) || (showHidden == 1))
                ).OrderBy(p => p.market_price).AsNoTracking().ToListAsync();
            
            // 门店名以 shop_list 为准；shop_list 只有 7 行，整表拿成字典
            Dictionary<int, string> shopById = (await _context.shop.AsNoTracking().ToListAsync())
                .ToDictionary(x => x.id, x => (x.name ?? "").Trim());
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
                    shop = (p.shop_id != null && shopById.ContainsKey((int)p.shop_id))
                        ? shopById[(int)p.shop_id] : "",
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

            // 顾客能看价格，但看不到进货价：商品 cost、每日结算价和建议售价只给店员（后台页要带 sessionKey）
            if (!SkipassSaleRules.CanSeeCost(await GetStaffOrNull(sessionKey, sessionType)))
            {
                foreach (SkipassWithPrice sp in ret)
                {
                    sp.cost = null;
                    SkipassSaleRules.RedactDailyPrices(sp.dailyPrice);
                }
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
        public async Task<ActionResult<SkipassWithPrice>> GetProduct(int productId, string? sessionKey = null,
            string sessionType = "wechat_mini_openid")
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
                shop = await ResolveProductShopName(p),
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
            if (!SkipassSaleRules.CanSeeCost(await GetStaffOrNull(sessionKey, sessionType)))
            {
                ret.cost = null;
                SkipassSaleRules.RedactDailyPrices(ret.dailyPrice);
            }
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

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
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class SkiPassController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        private MemberController _memberHelper;

        //private WanlongZiwoyouHelper _zwHelper;

        public SkiPassController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
            _memberHelper = new MemberController(context,  config);
            //_zwHelper = new WanlongZiwoyouHelper(context, config);
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
            skipass.card_member_return_time = DateTime.Now;
            skipass.cancel_member_id = member.id;
            try
            {
                Models.Product.SkiPass product = await _context.SkiPass.FindAsync(skipass.product_id);
                WanlongZiwoyouHelper _wlHelper = new WanlongZiwoyouHelper(_context, _config, product.source.Trim());
                WanlongZiwoyouHelper.ZiwoyouQueryResult r = _wlHelper.CancelOrder(int.Parse(skipass.reserve_no));
                WanlongZiwoyouHelper.ZiwoyouCancel cancel = (WanlongZiwoyouHelper.ZiwoyouCancel)r.data;
                skipass.is_cancel = cancel.cancelState;
                skipass.memo += " " + r.msg.Trim();
                
            }
            catch
            {
                skipass.is_cancel = -1;
            }
            _context.skiPass.Entry(skipass).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(skipass);
        }


        /*
        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date, int count, string cell, string name, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Product product = await _context.Product.FindAsync(productId);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || product == null)
            {
                return BadRequest();
            }
            OrderOnline order = (await CreateSkiPassOrder(new Product[] { product }, user, null, date, count));

            order.cell_number = cell.Trim();
            order.name = name.Trim();
            _context.Entry(order).State = EntityState.Modified;




            //MiniAppUser miniUser = await _context.MiniAppUsers.FindAsync(order.open_id.Trim());
            Member miniUser = await _memberHelper.GetMember(order.open_id.Trim(), "wechat_mini_openid");
            if (miniUser != null)
            {
                if (miniUser.real_name.Trim().Length <= 1)
                {
                    miniUser.real_name = name;
                }
                if (miniUser.cell.Length != 11)
                {
                    await _memberHelper.UpdateDetailInfo(miniUser.id, cell.Trim(), "cell", false);
                    //miniUser.cell = cell.Trim();
                }
                _context.Entry(miniUser).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            return order;
        }
        */
        /*
        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> PlaceSkiPassOrderNanshan(int productId, DateTime date, int count, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            Product productTicket = await _context.Product.FindAsync(productId);
            if (user == null || !user.isAdmin || productTicket == null)
            {
                return BadRequest();
            }
            Product productService = await _context.Product.FindAsync(297);
            return (await CreateSkiPassOrder(new Product[] { productTicket, productService }, null, user, date, count));
        }

        */
        /*
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
        */
        
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
                if (!skipass.resort.Trim().Equals("南山"))
                {
                    await AutoReserve(skipass.id);
                }
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
            int count, string cell, string name, string sessionKey, string sessionType = "wechat_mini_openid", string idNo = "")
        {
            Models.Product.Product product = await _context.Product.FindAsync(productId);
            Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(productId);
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
                deal_price = (product.sale_price + product.deposit) * count,
                ticket_price = product.sale_price,
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
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                final_price = (double)skipass.deal_price,
                open_id = member.wechatMiniOpenId,
                staff_open_id = "",
                memo = "",
                pay_method = "微信支付"
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

            return Ok(order);
        }
        [HttpGet]
        public async Task<ActionResult<List<object>>> GetProductsByResort(string resort)
        {
            resort = Util.UrlDecode(resort);
            var l = await _context.SkiPass
                .Join(_context.Product, s=>s.product_id, p=>p.id,
                (s, p)=> new {s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop, p.sale_price, p.market_price, p.cost, p.type})
                .Where(p => p.type.Trim().Equals("雪票") && p.name.IndexOf("【") >= 0 && p.name.IndexOf("】") >= 0 && p.name.IndexOf(resort) >= 0
                && p.third_party_no != null).OrderBy(p => p.sale_price).AsNoTracking().ToListAsync();
            return Ok(l);
        }
    }
}

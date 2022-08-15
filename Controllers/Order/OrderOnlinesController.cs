using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using wechat_miniapp_base.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using System.Web;
using Newtonsoft.Json;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Order;
namespace SnowmeetApi.Controllers
{
    public class SkiPassMemo
    {
        public string use_date;
    }
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OrderOnlinesController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        

        public OrderOnlinesController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }


        [HttpGet]
        public ActionResult<double> GetScoreRate(double orderPrice, double finalPrice)
        {
            return Util.GetScoreRate(finalPrice, orderPrice);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderOnline>>> GetOrdersByStaff(DateTime startDate, DateTime endDate,
            string shop, string status, string staffSessionKey)
        {
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(staffSessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            var list = await _context.OrderOnlines.Where(o => (o.create_date >= startDate && o.create_date <= endDate
             && (shop==null ? true : (o.shop.Trim().Equals(shop.Trim()))))).OrderByDescending(o => o.id).ToListAsync();

            if (status == null || status.Trim().Equals(""))
            {
                return list;
            }
            else
            {
                var newList = new List<OrderOnline>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].status.Trim().Equals(status.Trim()))
                    {
                        newList.Add(list[i]);
                    }
                }
                return newList;
            }
          
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderOnline>> GetWholeOrderByStaff(int orderId, string staffSessionKey)
        {
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(staffSessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (!order.open_id.Trim().Equals(""))
            {
                UnicUser customerUser = UnicUser.GetUnicUser(order.open_id, "snowmeet_mini");
                if (customerUser == null)
                {
                    customerUser = UnicUser.GetUnicUser(order.open_id, "snowmeet_official_account_new");
                }
                if (customerUser == null)
                {
                    customerUser = UnicUser.GetUnicUser(order.open_id, "snowmeet_official_account");
                }
                if (customerUser != null)
                {
                    order.user = await _context.MiniAppUsers.FindAsync(customerUser.miniAppOpenId);
                    
                }
            }
            var mi7Orders = await _context.mi7Order.Where(o => o.order_id == orderId).ToArrayAsync();
            if (mi7Orders != null && mi7Orders.Length > 0)
            {
                order.mi7Orders = mi7Orders;
            }
            if (!order.pay_memo.Trim().Equals("无需支付"))
            {
                var payments = await _context.OrderPayment.Where(p => p.order_id == orderId).ToArrayAsync();
                for (int i = 0; i < payments.Length; i++)
                {
                    var payment = payments[i];
                    if (payment.staff_open_id != null && !payment.staff_open_id.Trim().Equals(""))
                    {
                        var staffUser = await _context.MiniAppUsers.FindAsync(payment.staff_open_id);
                        if (staffUser != null)
                        {
                            payment.staffRealName = staffUser.real_name.Trim();
                        }
                    }
                }
                order.payments = payments;
            }

            string staffRealName = "";
            if (order.staff_open_id != null && !order.staff_open_id.Trim().Equals(""))
            {
                MiniAppUser staffUser = await _context.MiniAppUsers.FindAsync(order.staff_open_id);
                if (staffUser != null)
                {
                    staffRealName = staffUser.real_name.Trim();
                }
            }
            order.staffRealName = staffRealName.Trim();

            if (order.ticket_code != null && !order.ticket_code.Trim().Equals(""))
            {
                order.ticketArray = await _context.Ticket.Where(t => t.code.Trim().Equals(order.ticket_code)).ToArrayAsync();
            }

            return order;
        }

        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderOnline>> SetPaymentSuccess(int paymentId, string staffSessionKey)
        {
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(staffSessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment == null || payment.status.Trim().Equals(""))
            {
                return NotFound();
            }
            if (!payment.staff_open_id.Trim().Equals(user.miniAppOpenId.Trim()) && !payment.staff_open_id.Trim().Equals("待支付"))
            {
                return NoContent();
            }
            payment.status = "支付成功";
            _context.Entry(payment).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var payments =  await _context.OrderPayment.Where(p => (p.status == "支付成功" && p.order_id == payment.order_id)).ToListAsync();

            var paidAmount = payments.Sum(p => p.amount);


            OrderOnline order = await _context.OrderOnlines.FindAsync(payment.order_id);
            if (order != null)
            {
                if (order.final_price <= paidAmount)
                {
                    order.pay_state = 1;
                }
                else
                {
                    order.pay_state = -1;
                }
                order.pay_time = DateTime.Now;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                if (!order.ticket_code.Trim().Equals(""))
                {
                    var ticket = await _context.Ticket.FindAsync(order.ticket_code.Trim());
                    ticket.used = 1;
                    ticket.used_time = DateTime.Now;
                    _context.Entry(ticket).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                return NotFound();
            }
            return await GetWholeOrderByStaff(payment.order_id, staffSessionKey);
        }

        [HttpPost("{orderId}")]
        public async Task<ActionResult<OrderOnline>> ConfirmNonPaymentOrder(int orderId, string staffSessionKey)
        {
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(staffSessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            var order = await _context.OrderOnlines.FindAsync(orderId);
            if (order.pay_state == 0 && order.pay_memo.Trim().Equals("无需付款"))
            {
                order.pay_state = 1;
                order.pay_time = DateTime.Now;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return await GetWholeOrderByStaff(orderId, staffSessionKey);
            }
            else
            {
                return NotFound();
            }
        }


        [HttpPost]
        public async Task<ActionResult<OrderOnline>> PlaceOrderByStaff(OrderOnline order, string staffSessionKey)
        {
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(staffSessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            order.staff_open_id = user.miniAppOpenId.Trim();
            if (order.have_score == 1)
            {
                order.score_rate = Util.GetScoreRate(order.final_price, order.order_price);
                order.generate_score = (int)(order.final_price * order.score_rate);
            }
            else
            {
                order.score_rate = 0;
                order.generate_score = 0;
            }
            order.pay_state = 0;
            await _context.OrderOnlines.AddAsync(order);
            int i = await _context.SaveChangesAsync();
            if (i <= 0)
            {
                return NoContent();
            }
            if (order.mi7Orders != null)
            {
                for (int j = 0; j < order.mi7Orders.Length; j++)
                {
                    order.mi7Orders[j].order_id = order.id;
                    await _context.mi7Order.AddAsync(order.mi7Orders[j]);
                }
                await _context.SaveChangesAsync();
            }
            if (order.payments != null && order.payments.Length == 1 && !(order.pay_memo.Trim().Equals("无需付款") || order.pay_memo.Trim().Equals("暂缓支付")))
            {
                var payment = order.payments[0];
                payment.order_id = order.id;
                payment.status = "待支付";
                payment.staff_open_id = order.staff_open_id;
                await _context.OrderPayment.AddAsync(payment);
                await _context.SaveChangesAsync();
                order.payments[0] = payment;
            }

            if (order.user != null && order.user.open_id != null &&  !order.user.open_id.Trim().Equals(""))
            {
                MiniAppUser customerUser = await _context.MiniAppUsers.FindAsync(order.user.open_id);
                customerUser.real_name = order.user.real_name.Trim();
                customerUser.cell_number = order.user.cell_number.Trim();
                customerUser.gender = order.user.gender.Trim();
                _context.Entry(customerUser).State = EntityState.Modified;
                await _context.SaveChangesAsync();

            }

            return order;
        }

        
        [HttpGet("{orderId}")]
        public async Task<ActionResult<bool>> SetSkiPassCertNo(int orderId, string certNo, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (!order.open_id.Trim().Equals(user.miniAppOpenId)
                && !order.open_id.Trim().Equals(user.officialAccountOpenId.Trim())
                && !order.open_id.Trim().Equals(user.officialAccountOpenIdOld.Trim()))
            {
                return NoContent();
            }
            string code = order.code;
            if (code.Trim().Equals(""))
            {
                return NotFound();
            }
            var card = await _context.Card.FindAsync(code);
            card.use_memo = certNo.Trim();
            _context.Entry(card).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return true;
        }

        [ActionName("GetSkiPassNum")]
        [HttpGet("{type}")]
        public async Task<ActionResult<int>> GetSkiPassNum(int type, string dateStr)
        {
            int num = 0;
            DateTime date = DateTime.Parse(dateStr);
            DateTime startDate = date.AddDays(-6);
            List<OrderOnline> orders = await _context.OrderOnlines
                .Where<OrderOnline>(o => (o.pay_time >= startDate && o.pay_time <= date.AddDays(1) && o.type.Trim().Equals("雪票") && o.shop.Trim().Equals("南山")))
                .ToListAsync<OrderOnline>();
            for (int i = 0; i < orders.Count; i++)
            {
                SkiPassMemo memo = JsonConvert.DeserializeObject<SkiPassMemo>(orders[i].memo.Trim());
                if (DateTime.Parse(memo.use_date).Date == date.Date)
                {
                    List<OrderOnlineDetail> detail = await _context.OrderOnlineDetails
                        .Where<OrderOnlineDetail>(o => (o.OrderOnlineId == orders[i].id)).ToListAsync<OrderOnlineDetail>();
                    Product prodcut = await _context.Product.FindAsync(detail[0].product_id);
                    if (type == 0 && prodcut.name.IndexOf("夜") < 0)
                    {
                        num = num + detail[0].count;
                    }
                    if (type == 1 && prodcut.name.IndexOf("夜") >= 0)
                    {
                        num = num + detail[0].count;
                    }
                }
            }
            return num;
        }


        // GET: api/OrderOnlines
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderOnline>>> GetOrderOnlines()
        {
            return await _context.OrderOnlines.ToListAsync();
        }

        // GET: api/OrderOnlines/5
        [ActionName("GetOrderOnline")]
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderOnline>> GetOrderOnline(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (user == null)
            {
                return NotFound();
            }

            var orderOnline = await _context.OrderOnlines.FindAsync(id);

            if (orderOnline == null)
            {
                return NotFound();
            }

            if (user.isAdmin
                || orderOnline.open_id.Trim().Equals(user.officialAccountOpenId.Trim())
                || orderOnline.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                return orderOnline;
            }

            return NotFound();
        }

        

        
        [HttpGet("{id}")]
        public async Task<ActionResult<WepayOrder>> Pay(int id,string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            int mchid = 3;
            string notify = "http://mini.snowmeet.top/core/WepayOrder/PaymentCallback";
            notify = Util.UrlDecode(notify);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (user == null)
            {
                return NotFound();
            }
            if ((user.miniAppUser != null && user.miniAppUser.is_admin == 1) 
                || (user.officialAccountUser != null && user.officialAccountUser.is_admin == 1))
            {
                isStaff = true;
            }
            else 
            {
                isStaff = false;
            }

            bool canPay = false;

            OrderOnline order = await _context.OrderOnlines.FindAsync(id);

            mchid = GetMchId(order);

            if (order == null)
            {
                return NotFound();
            }
            string timeStamp = Util.getTime13().ToString();
           
            order.open_id = user.miniAppUser.open_id.Trim();
            order.out_trade_no = timeStamp.Trim();
            _context.Entry(order).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            { 
                
            }
            

            if (order != null && (order.open_id.Trim().Equals(user.miniAppOpenId.Trim())
                ||     order.open_id.Trim().Equals(user.officialAccountOpenId)     ))
            {
                canPay = true;
            }
            else if (isStaff)
            {
                canPay = true;
            }
            if (!canPay)
            {
                return NotFound();
            }

            

            WepayKey key = await _context.WepayKeys.FindAsync(mchid);
            if (key == null)
            {
                return NotFound();
            }

            WepayOrder wepayOrder = await _context.WepayOrders.FindAsync(timeStamp.Trim());

            if (wepayOrder != null)
            {
                return NotFound();
            }

            wepayOrder = new WepayOrder();
            wepayOrder.out_trade_no = timeStamp;
            wepayOrder.open_id = user.miniAppOpenId;
            wepayOrder.notify = notify.Trim();
            wepayOrder.order_id = order.id;
            wepayOrder.amount = (int)Math.Round((order.order_real_pay_price * 100),0);
            wepayOrder.app_id = _appId;
            wepayOrder.description = "";
            wepayOrder.mch_id = mchid;
            _context.WepayOrders.Add(wepayOrder);
            await _context.SaveChangesAsync();
            

            var certManager = new InMemoryCertificateManager();
            var options = new WechatTenpayClientOptions()
            {
                MerchantId = key.mch_id.Trim(),
                MerchantV3Secret = "",
                MerchantCertificateSerialNumber = key.key_serial.Trim(),
                MerchantCertificatePrivateKey = key.private_key.Trim(),
                PlatformCertificateManager = certManager
            };
            var client = new WechatTenpayClient(options);
            var request = new CreatePayTransactionJsapiRequest()
            {
                OutTradeNumber = timeStamp,
                AppId = _appId,
                Description = wepayOrder.description.Trim().Equals("")?"测试商品":wepayOrder.description.Trim(),
                ExpireTime = DateTimeOffset.Now.AddMinutes(30),
                NotifyUrl = wepayOrder.notify.Trim() + "/" + mchid.ToString(),
                Amount = new CreatePayTransactionJsapiRequest.Types.Amount()
                { 
                    Total = wepayOrder.amount
                },
                Payer = new CreatePayTransactionJsapiRequest.Types.Payer()
                { 
                    OpenId = wepayOrder.open_id.Trim()
                }
            };
            var response = await client.ExecuteCreatePayTransactionJsapiAsync(request);
            if (response != null && response.PrepayId != null && !response.PrepayId.Trim().Equals(""))
            {
                wepayOrder.prepay_id = response.PrepayId.Trim();
                wepayOrder.state = 1;
                _context.Entry(wepayOrder).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                var paraMap = client.GenerateParametersForJsapiPayRequest(request.AppId, response.PrepayId);
                wepayOrder.timestamp = paraMap["timeStamp"].Trim();
                wepayOrder.nonce = paraMap["nonceStr"].Trim();
                wepayOrder.sign = paraMap["paySign"].Trim();
                return wepayOrder;
            }



            return NotFound();
        }
        

        // PUT: api/OrderOnlines/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderOnline(int id, OrderOnline orderOnline)
        {
            if (id != orderOnline.id)
            {
                return BadRequest();
            }

            _context.Entry(orderOnline).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderOnlineExists(id))
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

        // POST: api/OrderOnlines
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<OrderOnline>> PostOrderOnline(OrderOnline orderOnline)
        {
            _context.OrderOnlines.Add(orderOnline);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetOrderOnline", new { id = orderOnline.id }, orderOnline);
        }

        // DELETE: api/OrderOnlines/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderOnline(int id)
        {
            var orderOnline = await _context.OrderOnlines.FindAsync(id);
            if (orderOnline == null)
            {
                return NotFound();
            }

            _context.OrderOnlines.Remove(orderOnline);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool OrderOnlineExists(int id)
        {
            return _context.OrderOnlines.Any(e => e.id == id);
        }

        private int GetMchId(OrderOnline order)
        {
            int mchId = 3;
            if (order.type == "押金")
            {
                mchId = 5;
            }
            if (order.type != "雪票" && order.shop == "南山")
            {
                mchId = 6;
            }
            if (order.type == "雪票" && order.shop == "南山")
            {
                mchId = 7;
            }
            return mchId;
        }
    }
}

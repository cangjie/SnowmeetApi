﻿using System;
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
namespace SnowmeetApi.Controllers
{
    public class SkiPassMemo
    {
        public string use_date;
    }
    [Route("[controller]/[action]")]
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
                MerchantCertSerialNumber = key.key_serial.Trim(),
                MerchantCertPrivateKey = key.private_key.Trim(),
                CertificateManager = certManager
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

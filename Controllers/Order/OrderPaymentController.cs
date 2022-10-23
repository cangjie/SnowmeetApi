using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models;
using wechat_miniapp_base.Models;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using NuGet.Packaging.Signing;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Controllers.Order
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderPaymentController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public OrderPaymentController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            UnicUser._context = context;
            
        }

        [HttpGet("id")]
        public async Task<ActionResult<TenpaySet>> TenpayRequest(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (user == null)
            {
                return BadRequest();
            }
            OrderPayment payment = await _context.OrderPayment.FindAsync(id);
            if (payment == null)
            {
                return BadRequest();
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(payment.order_id);
            if (order == null)
            {
                return BadRequest();
            }
            if (!payment.pay_method.Trim().Equals("微信支付") || !payment.status.Trim().Equals("待支付"))
            {
                return BadRequest();
            }
            if (order.status.Trim().Equals("支付完成") || order.status.Trim().Equals("订单关闭"))
            {
                return BadRequest();
            }
            string timeStamp = Util.getTime13().ToString();
            int mchid = GetMchId(order);
            WepayKey key = await _context.WepayKeys.FindAsync(mchid);

            var certManager = new InMemoryCertificateManager();
            var options = new WechatTenpayClientOptions()
            {
                MerchantId = key.mch_id.Trim(),
                MerchantV3Secret = "",
                MerchantCertificateSerialNumber = key.key_serial.Trim(),
                MerchantCertificatePrivateKey = key.private_key.Trim(),
                PlatformCertificateManager = certManager
            };

            string desc = "未知商品";

            if (order.type.Trim().StartsWith("店销"))
            {
                desc = order.shop.Trim() + order.type.Trim();
                var mi7Orders = await _context.mi7Order
                    .Where(o => o.order_id == order.id).ToArrayAsync();
                string mi7Nos = "";
                for (int i = 0; i < mi7Orders.Length; i++)
                {
                    mi7Nos = mi7Nos.Trim() + " " + mi7Orders[i].mi7_order_id.Trim();
                }
                desc = desc + mi7Nos.Trim();
            }

            if (order.type.Trim().Equals("服务"))
            {
                string name = "";
                var details = await _context.OrderOnlineDetails
                    .Where(d => d.OrderOnlineId == order.id).ToListAsync();
                for (int i = 0; i < details.Count; i++)
                {
                    Product p = await _context.Product.FindAsync(details[i].product_id);
                    if (p != null)
                    {
                        name = name + " " + p.name.Trim();
                    }
                }
                name = name.Trim();
                if (!name.Equals(""))
                {
                    desc = name;
                }
            }

            string notifyUrl = "https://mini.snowmeet.top/core/OrderPayment/CallBack/" + mchid.ToString();
            string outTradeNo = order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
            var client = new WechatTenpayClient(options);
            var request = new CreatePayTransactionJsapiRequest()
            {
                OutTradeNumber = outTradeNo,
                AppId = _appId,
                Description = desc.Trim(),//wepayOrder.description.Trim().Equals("") ? "测试商品" : wepayOrder.description.Trim(),
                ExpireTime = DateTimeOffset.Now.AddMinutes(30),
                NotifyUrl = notifyUrl,//wepayOrder.notify.Trim() + "/" + mchid.ToString(),
                Amount = new CreatePayTransactionJsapiRequest.Types.Amount()
                {
                    Total = (int)Math.Round(payment.amount * 100, 0)
                },
                Payer = new CreatePayTransactionJsapiRequest.Types.Payer()
                {
                    OpenId = user.miniAppOpenId.Trim()
                }
            };
            var response = await client.ExecuteCreatePayTransactionJsapiAsync(request);
            var paraMap = client.GenerateParametersForJsapiPayRequest(request.AppId, response.PrepayId);
            if (response != null && response.PrepayId != null && !response.PrepayId.Trim().Equals(""))
            {
                TenpaySet set = new TenpaySet()
                {
                    prepay_id = response.PrepayId.Trim(),
                    timeStamp = paraMap["timeStamp"].Trim(),
                    nonce = paraMap["nonceStr"].Trim(),
                    sign = paraMap["paySign"].Trim()

                };

                payment.out_trade_no = order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2,'0') +  timeStamp;
                payment.mch_id = mchid;
                payment.open_id = user.miniAppOpenId.Trim();
                payment.app_id = _appId;
                payment.notify = notifyUrl.Trim();
                payment.nonce = set.nonce.Trim();
                payment.sign = set.sign.Trim();
                payment.out_trade_no = outTradeNo;
                payment.prepay_id = set.prepay_id.Trim();
                payment.timestamp = set.timeStamp.Trim();
                _context.Entry(payment).State = EntityState.Modified;
                await _context.SaveChangesAsync();


                return set;
            }
            return BadRequest();
        }

        [NonAction]
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

        /*

        // GET: api/OrderPayment
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderPayment>>> GetOrderPayment()
        {
            return await _context.OrderPayment.ToListAsync();
        }

        // GET: api/OrderPayment/5
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderPayment>> GetOrderPayment(int id)
        {
            var orderPayment = await _context.OrderPayment.FindAsync(id);

            if (orderPayment == null)
            {
                return NotFound();
            }

            return orderPayment;
        }

        // PUT: api/OrderPayment/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderPayment(int id, OrderPayment orderPayment)
        {
            if (id != orderPayment.id)
            {
                return BadRequest();
            }

            _context.Entry(orderPayment).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderPaymentExists(id))
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

        // POST: api/OrderPayment
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<OrderPayment>> PostOrderPayment(OrderPayment orderPayment)
        {
            _context.OrderPayment.Add(orderPayment);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetOrderPayment", new { id = orderPayment.id }, orderPayment);
        }

        // DELETE: api/OrderPayment/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderPayment(int id)
        {
            var orderPayment = await _context.OrderPayment.FindAsync(id);
            if (orderPayment == null)
            {
                return NotFound();
            }

            _context.OrderPayment.Remove(orderPayment);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool OrderPaymentExists(int id)
        {
            return _context.OrderPayment.Any(e => e.id == id);
        }
    }
}

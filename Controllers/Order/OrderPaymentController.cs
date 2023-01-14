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
//using NuGet.Packaging.Signing;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Users;
//using SnowmeetApi.Controllers.WepayOrderController;
using System.IO;
using System.Net.Http.Headers;

namespace SnowmeetApi.Controllers.Order
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OrderPaymentController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        private IConfiguration _originConfig;

        public string _appId = "";

        private readonly IHttpContextAccessor _httpContextAccessor;

        public class TenpayResource
        {
            public string original_type { get; set; }
            public string algorithm { get; set; }
            public string ciphertext { get; set; }
            public string associated_data { get; set; }
            public string nonce { get; set; }
        }

        public class TenpayCallBackStruct
        {
            public string id { get; set; }
            public DateTimeOffset create_time { get; set; }
            public string resource_type { get; set; }
            public string event_type { get; set; }
            public string summary { get; set; }
            public TenpayResource resource { get; set; }
        }

        public OrderPaymentController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _originConfig = config;
            _context = context;
            _originConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            UnicUser._context = context;

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TenpaySet>> TenpayRequest(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
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

            order.open_id = user.miniAppOpenId.Trim();
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            string notifyUrl = "https://mini.snowmeet.top/core/OrderPayment/TenpayPaymentCallBack/" + mchid.ToString();
            string? outTradeNo = payment.out_trade_no;
            if (outTradeNo == null || outTradeNo.Length != 20)
            { 
                outTradeNo = order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
            }
             //order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
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

                //payment.out_trade_no = order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp;
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
        public async Task<OrderPayment> TenpayRefund(int id, double amount, string sessionKey)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(id);


            return payment;
        }


        [HttpPost("{mchid}")]
        public async Task<ActionResult<string>> TenpayPaymentCallback(int mchid, TenpayCallBackStruct postData)
        {

            string apiKey = "";
            WepayKey key = _context.WepayKeys.Find(mchid);

            if (key == null)
            {
                return NotFound();
            }

            apiKey = key.api_key.Trim();

            if (apiKey == null || apiKey.Trim().Equals(""))
            {
                return NotFound();
            }

            string postJson = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            string path = $"{Environment.CurrentDirectory}";
            string paySign = "no sign";
            string nonce = "no nonce";
            string serial = "no serial";
            string timeStamp = "no time";
            try
            {
                paySign = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Signature"].ToString();
                nonce = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Nonce"].ToString();
                serial = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Serial"].ToString();
                timeStamp = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Timestamp"].ToString();
            }
            catch
            {

            }
            if (path.StartsWith("/"))
            {
                path = path + "/WepayCertificate/";
            }
            else
            {
                path = path + "\\WepayCertificate\\";
            }
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            //path = path + "callback_" +  + ".txt";
            // 此文本只添加到文件一次。
            using (StreamWriter fw = new StreamWriter(path + "callback_origin_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTimeOffset.Now.ToString());
                fw.WriteLine(serial);
                fw.WriteLine(timeStamp);
                fw.WriteLine(nonce);
                fw.WriteLine(paySign);
                fw.WriteLine(postJson);
                fw.WriteLine("");
                fw.WriteLine("--------------------------------------------------------");
                fw.WriteLine("");
                fw.Close();
            }

            try
            {
                string cerStr = "";
                using (StreamReader sr = new StreamReader(path + serial.Trim() + ".pem", true))
                {
                    cerStr = sr.ReadToEnd();
                    sr.Close();
                }



                var certManager = new InMemoryCertificateManager();
               
                CertificateEntry ce = new CertificateEntry(serial, cerStr, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);


                certManager.AddEntry(ce);
                //certManager.SetCertificate(serial, cerStr);
                var options = new WechatTenpayClientOptions()
                {
                    MerchantV3Secret = apiKey,
                    PlatformCertificateManager = certManager
                    
                };
                
                var client = new WechatTenpayClient(options);
                bool valid = client.VerifyEventSignature(timeStamp, nonce, postJson, paySign, serial);
                if (valid)
                {
                    var callbackModel = client.DeserializeEvent(postJson);
                    if ("TRANSACTION.SUCCESS".Equals(callbackModel.EventType))
                    {
                        /* 根据事件类型，解密得到支付通知敏感数据 */

                        var callbackResource = client.DecryptEventResource<SKIT.FlurlHttpClient.Wechat.TenpayV3.Events.TransactionResource>(callbackModel);
                        string outTradeNumber = callbackResource.OutTradeNumber;
                        string transactionId = callbackResource.TransactionId;
                        string callbackStr = Newtonsoft.Json.JsonConvert.SerializeObject(callbackResource);
                        try
                        {
                            using (StreamWriter sw = new StreamWriter(path + "callback_decrypt_" + dateStr + ".txt", true))
                            {
                                sw.WriteLine(DateTimeOffset.Now.ToString());
                                sw.WriteLine(callbackStr);
                                sw.WriteLine("");
                                sw.Close();
                            }
                        }
                        catch
                        {

                        }
                        await SetTenpayPaymentSuccess(outTradeNumber);

                        //Console.WriteLine("订单 {0} 已完成支付，交易单号为 {1}", outTradeNumber, transactionId);
                    }
                }

            }
            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
            }
            return "{ \r\n \"code\": \"SUCCESS\", \r\n \"message\": \"成功\" \r\n}";
        }

        [HttpGet]
        public  async Task<ActionResult<OrderOnline>> SetTenpayPaymentSuccess(string outTradeNumber)
        {
            var paymentList = await _context.OrderPayment.Where(o => o.out_trade_no.Trim().Equals(outTradeNumber.Trim())).ToListAsync();
            if (paymentList == null || paymentList.Count == 0)
            {
                return NotFound();
            }
            OrderPayment payment = paymentList[0];
            payment.status = "支付成功";
            _context.Entry(payment).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            OrderOnline order = await _context.OrderOnlines.FindAsync(payment.order_id);
            if (order == null)
            {
                return NotFound();
            }
            OrderPayment[] paymentsArr = await _context.OrderPayment.Where(p => p.order_id == order.id).ToArrayAsync();
            order.payments = paymentsArr;
            if (order.final_price <= order.paidAmount)
            {
                order.pay_state = 1;
            }
            if (order.open_id.Trim().Equals(""))
            {
                order.open_id = payment.open_id.Trim();
            }
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var pointList = await _context.Point.Where(p => p.memo.Contains("支付赠送龙珠，订单ID：" + order.id.ToString())).ToListAsync();
            int score = (int)Math.Round(order.generate_score, 0);
            if (pointList.Count == 0 && !order.open_id.Trim().Equals("") && score > 0)
            {
                Point p = new Point()
                {
                    memo = "店销现货支付赠送龙珠，订单ID：" + order.id,
                    user_open_id = order.open_id.Trim(),
                    points = score,
                    transact_date = DateTime.Now
                };
                await _context.Point.AddAsync(p);
                await _context.SaveChangesAsync();
            }

            switch (order.type.Trim())
            {
                case "服务":
                    MaintainLiveController maintainHelper = new MaintainLiveController(_context, _originConfig);
                    await maintainHelper.MaitainOrderPaySuccess(order.id);
                    break;
                case "雪票":
                    SkiPassController skiPassHelper = new SkiPassController(_context, _originConfig);
                    await skiPassHelper.CreateSkiPass(order);
                    break;
                default:
                    break;
            }


            return order;
            
        }

        [NonAction]
        private int GetMchId(OrderOnline order)
        {
            int mchId = 3;
            if (order.type == "押金")
            {
                mchId = 5;
                //mchId = 3;
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
        
        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderOnline>> GetWholeOrder(int paymentId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;//.GetUnicUser(sessionKey);
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment == null || (payment.open_id != null && !payment.open_id.Trim().Equals("") 
                && !payment.open_id.Trim().Equals(user.miniAppOpenId.Trim()) && !user.isAdmin ))
            {
                return BadRequest();
            }
            OrderOnlinesController orderController = new OrderOnlinesController(_context, _originConfig);
            OrderOnline order =  (await orderController.GetOrderOnline(payment.order_id, sessionKey)).Value;
            if (order == null)
            {
                return BadRequest();
            }

            //order.payments = await _context.OrderPayment.Where(p => p.order_id == order.id).ToArrayAsync();
            
            return order;
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

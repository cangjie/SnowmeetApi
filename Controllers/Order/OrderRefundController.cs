using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;

using Newtonsoft.Json;
using SnowmeetApi.Models.Maintain;

namespace SnowmeetApi.Controllers.Order
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OrderRefundController : ControllerBase
    {

        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        private IConfiguration _originConfig;
        public string _appId = "";
        private readonly IHttpContextAccessor _httpContextAccessor;

        public OrderRefundController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _originConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;

        }

        [NonAction]
        public async Task<ActionResult<int>> RefreshWepayRefundInfo()
        {
            var refundList = await _db.OrderPaymentRefund
                .Where(r => (!r.refund_id.Trim().Equals("") && r.TransactionId.Trim().Equals("") && r.create_date.Date > DateTime.Parse("2022-9-1")))
                .ToListAsync();
            if (refundList == null)
            {
                return BadRequest();
            }
            int num = 0;
            for (int i = 0; i < refundList.Count; i++)
            {
                OrderPaymentRefund r = refundList[i];
                OrderPayment p = await _db.OrderPayment.FindAsync(r.payment_id);
                if (p == null)
                {
                    continue;
                }
                WepayKey key = await _db.WepayKeys.FindAsync(p.mch_id);
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
                //var req = new GetRefund
                var request = new GetRefundDomesticRefundByOutRefundNumberRequest()
                {
                    OutRefundNumber = r.id.ToString()
                };
                var reponse = await client.ExecuteGetRefundDomesticRefundByOutRefundNumberAsync(request);
                try
                {
                    r.TransactionId = reponse.TransactionId.Trim();
                    r.RefundFee = double.Parse(reponse.Amount.RefundFee.ToString()) / 100;
                    _db.Entry(r).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                    num++;
                }
                catch
                {

                }
            }
            return Ok(num);
        }


        [HttpGet]
        public async Task<ActionResult<int>> GetResult(string outTradeNo)
        {
            var paymentList = await _db.OrderPayment.Where(p => p.out_trade_no.Trim().Equals(outTradeNo.Trim())).ToListAsync();
            if (paymentList == null || paymentList.Count == 0)
            {
                return NotFound();
            }
            int? mchId = paymentList[0].mch_id;
            int paymentId = paymentList[0].id;
            if (mchId == null)
            {
                return NoContent();
            }
            var paymentRefundList = await _db.OrderPaymentRefund.Where(r => r.payment_id == paymentId).ToListAsync();
            if (paymentRefundList == null || paymentRefundList.Count == 0)
            {
                return NotFound();
            }
            WepayKey key = await _db.WepayKeys.FindAsync(mchId);

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
            //var req = new GetRefund
            var request = new GetRefundDomesticRefundByOutRefundNumberRequest()
            {
                OutRefundNumber = paymentRefundList[0].id.ToString()
            };
            var reponse = await client.ExecuteGetRefundDomesticRefundByOutRefundNumberAsync(request);
            return BadRequest();
        }

        

        [NonAction]
        public async Task<OrderPaymentRefund> TenpayRefund(int paymentId, double amount, string memo, string sessionKey)
        {
            
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId); 
            if (!payment.pay_method.Trim().Equals("微信支付"))
            {
                
                return null; 
            }
            

            string notify = payment.notify.Trim().Replace("OrderPayment/TenpayPaymentCallBack", "OrderRefund/TenpayRefundCallback"); 

            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value; 
            double refundedAmount = await _db.OrderPaymentRefund.Where(r => (r.payment_id == paymentId && r.state == 1)).SumAsync(s => s.amount); 
            
            
            if (refundedAmount >= payment.amount || refundedAmount + amount > payment.amount)
            {
                
                return null; 
            }
            

            OrderPaymentRefund refund = new OrderPaymentRefund()
            {
                order_id = payment.order_id,
                payment_id = payment.id,
                amount = amount,
                oper = user.miniAppOpenId.Trim(),
                state = 0,
                memo = memo,
                notify_url = notify.Trim()


            }; 

            await _db.OrderPaymentRefund.AddAsync(refund); 
            await _db.SaveChangesAsync();
            //var client = new WechatTenpayClient(options);
            OrderOnlinesController orderHelper = new OrderOnlinesController(_db, _originConfig); 
            OrderOnline order = (await orderHelper.GetOrderOnline(payment.order_id, sessionKey)).Value; 

            WepayKey key = await _db.WepayKeys.FindAsync(payment.mch_id);

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
            var request = new CreateRefundDomesticRefundRequest()
            {
                OutTradeNumber = payment.out_trade_no.Trim(),
                OutRefundNumber = refund.id.ToString(),
                Amount = new CreateRefundDomesticRefundRequest.Types.Amount()
                {
                    Total = (int)Math.Round(payment.amount * 100, 0),
                    Refund = (int)(Math.Round(amount * 100))
                },
                Reason = user.miniAppOpenId,

                NotifyUrl = refund.notify_url.Trim()
            };
            var response = await client.ExecuteCreateRefundDomesticRefundAsync(request);
            try
            {
                string refundId = response.RefundId.Trim();
                if (refundId == null || refundId.Trim().Equals(""))
                {
                    return refund;
                }
                //refund.status = refundId;
                refund.refund_id = refundId;
                _db.Entry<OrderPaymentRefund>(refund).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                //await Response.WriteAsync("SUCCESS");
                return refund;
            }
            catch
            {
                refund.memo = response.ErrorMessage.Trim();
                _db.Entry<OrderPaymentRefund>(refund).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return refund;
            }

            
        }

        [HttpPost("{mchid}")]
        public async Task<ActionResult<string>> TenpayRefundCallback(int mchid, [FromBody]object postData)
        {
            string paySign = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Signature"].ToString();
            string nonce = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Nonce"].ToString();
            string serial = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Serial"].ToString();
            string timeStamp = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Timestamp"].ToString();


            string path = $"{Environment.CurrentDirectory}";
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
            //string postJson = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            //path = path + "callback_" +  + ".txt";
            // 此文本只添加到文件一次。
            using (StreamWriter fw = new StreamWriter(path + "callback_origin_refund_" + dateStr + ".txt", true))
            {
                await fw.WriteLineAsync(DateTimeOffset.Now.ToString());


                await fw.WriteLineAsync(paySign);
                await fw.WriteLineAsync(nonce);
                await fw.WriteLineAsync(serial);
                await fw.WriteLineAsync(timeStamp);
                await fw.WriteLineAsync(postData.ToString());
                await fw.WriteLineAsync("--------------------------------------------------------");
                await fw.WriteLineAsync("");
                fw.Close();
            }


            

            string cerStr = "";
            using (StreamReader sr = new StreamReader(path + serial.Trim() + ".pem", true))
            {
                cerStr = sr.ReadToEnd();
                sr.Close();
            }


            string apiKey = "";
            WepayKey key = _db.WepayKeys.Find(mchid);

            if (key == null)
            {
                return NotFound();
            }

            apiKey = key.api_key.Trim();

            var certManager = new InMemoryCertificateManager();

            CertificateEntry ce = new CertificateEntry("AEAD_AES_256_GCM", serial, cerStr, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);


            certManager.AddEntry(ce);
            //certManager.SetCertificate(serial, cerStr);
            var options = new WechatTenpayClientOptions()
            {
                MerchantV3Secret = apiKey,
                PlatformCertificateManager = certManager

            };

            var client = new WechatTenpayClient(options);
            bool valid = client.VerifyEventSignature(timeStamp, nonce, postData.ToString(), paySign, serial);

            if (valid)
            {
                var callbackModel = client.DeserializeEvent(postData.ToString());
                if ("REFUND.SUCCESS".Equals(callbackModel.EventType))
                {
                    try
                    {
                        var callbackResource = client.DecryptEventResource<SKIT.FlurlHttpClient.Wechat.TenpayV3.Events.TransactionResource>(callbackModel);
                        OrderPayment payment = await _db.OrderPayment
                            .Where(p => p.out_trade_no.Trim().Equals(callbackResource.OutTradeNumber)
                            && p.pay_method.Trim().Equals("微信支付") && p.status.Trim().Equals("支付成功"))
                            .OrderByDescending(p => p.id).FirstAsync();


                        double refundAmount = Math.Round(((double)callbackResource.Amount.Total) / 100, 2);
                        OrderPaymentRefund refund = await _db.OrderPaymentRefund.Where(r => (r.payment_id == payment.id
                         && r.amount == refundAmount && r.state == 0)).FirstAsync();

                        refund.state = 1;
                        refund.memo = callbackResource.TransactionId;
                        _db.Entry(refund).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                    }
                    catch
                    {

                    }
                    
                        
                    

                }

            }
            return "{ \r\n \"code\": \"SUCCESS\", \r\n \"message\": \"成功\" \r\n}";
        }

        public class TenpayRefundJson
        {
            public string event_type = "";
            public string summary = "";
            public TenpayRefundJsonResource resource; 
        }

        public class TenpayRefundJsonResource
        {
            public string ciphertext = "";
            public string nonce = "";
        }

        /*

        // GET: api/OrderRefund
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderPaymentRefund>>> GetOrderPaymentRefund()
        {
            return await _context.OrderPaymentRefund.ToListAsync();
        }

        // GET: api/OrderRefund/5
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderPaymentRefund>> GetOrderPaymentRefund(int id)
        {
            var orderPaymentRefund = await _context.OrderPaymentRefund.FindAsync(id);

            if (orderPaymentRefund == null)
            {
                return NotFound();
            }

            return orderPaymentRefund;
        }

        // PUT: api/OrderRefund/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderPaymentRefund(int id, OrderPaymentRefund orderPaymentRefund)
        {
            if (id != orderPaymentRefund.id)
            {
                return BadRequest();
            }

            _context.Entry(orderPaymentRefund).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderPaymentRefundExists(id))
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

        // POST: api/OrderRefund
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<OrderPaymentRefund>> PostOrderPaymentRefund(OrderPaymentRefund orderPaymentRefund)
        {
            _context.OrderPaymentRefund.Add(orderPaymentRefund);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetOrderPaymentRefund", new { id = orderPaymentRefund.id }, orderPaymentRefund);
        }

        // DELETE: api/OrderRefund/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderPaymentRefund(int id)
        {
            var orderPaymentRefund = await _context.OrderPaymentRefund.FindAsync(id);
            if (orderPaymentRefund == null)
            {
                return NotFound();
            }

            _context.OrderPaymentRefund.Remove(orderPaymentRefund);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        */

        private bool OrderPaymentRefundExists(int id)
        {
            return _db.OrderPaymentRefund.Any(e => e.id == id);
        }
    }
}

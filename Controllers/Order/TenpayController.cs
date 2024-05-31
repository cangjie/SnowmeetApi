using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class TenpayController : ControllerBase
    {
        public ApplicationDBContext _db;
        public IConfiguration _oriConfig;
        public IHttpContextAccessor _http;
        public OrderPaymentController _orderPaymentHelper;
        public RentController _rentHelper;
        public string _appId = "";
        public TenpayController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _oriConfig = config;
            _http = httpContextAccessor;
            _orderPaymentHelper = new OrderPaymentController(context, config, httpContextAccessor);
            _appId = _oriConfig.GetSection("Settings").GetSection("AppId").Value.Trim();
            _rentHelper = new RentController(_db, _oriConfig, _http);
        }

         [HttpGet("{paymentId}")]
        public async Task<ActionResult<TenpaySet>> TenpayRequest(int paymentId, string sessionKey)
        {

            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
            if (user == null)
            {
                return BadRequest();
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            if (payment == null)
            {
                return BadRequest();
            }
            OrderOnline order = await _db.OrderOnlines.FindAsync(payment.order_id);
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
            int mchid = _orderPaymentHelper.GetMchId(order);
            WepayKey key = await _db.WepayKeys.FindAsync(mchid);

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
                var mi7Orders = await _db.mi7Order
                    .Where(o => o.order_id == order.id).ToArrayAsync();
                string mi7Nos = "";
                for (int i = 0; i < mi7Orders.Length; i++)
                {
                    mi7Nos = mi7Nos.Trim() + " " + mi7Orders[i].mi7_order_id.Trim();
                }
                desc = desc + mi7Nos.Trim();
            }
            else if (order.type.Trim().StartsWith("服务"))
            {
                desc = "养护";
            }
            else
            {
                desc = "租赁";
            }

           

            if (order.type.Trim().Equals("服务"))
            {
                string name = "";
                var details = await _db.OrderOnlineDetails
                    .Where(d => d.OrderOnlineId == order.id).ToListAsync();
                for (int i = 0; i < details.Count; i++)
                {
                    Product p = await _db.Product.FindAsync(details[i].product_id);
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
            _db.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            string notifyUrl = "https://mini.snowmeet.top/core/Tenpay/TenpayPaymentCallBack/" + mchid.ToString();
            string? outTradeNo = payment.out_trade_no;
            if (outTradeNo == null )
            { 
                outTradeNo = order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
            }

            //CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail  dtl = new CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail();
            //dtl.

             //order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
            var client = new WechatTenpayClient(options);
            var request = new CreatePayTransactionJsapiRequest()
            {
                OutTradeNumber = outTradeNo,
                AppId = _appId,
                Description = desc,
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
                _db.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return set;
            }
            return BadRequest();
        }

        
        [HttpPost("{mchid}")]
        public async Task<ActionResult<string>> TenpayPaymentCallback(int mchid,
            [FromHeader(Name = "Wechatpay-Timestamp")] string timeStamp,
            [FromHeader(Name = "Wechatpay-Nonce")] string nonce,
            [FromHeader(Name = "Wechatpay-Signature")] string paySign,
            [FromHeader(Name = "Wechatpay-Serial")] string serial)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            string postJson = await reader.ReadToEndAsync();

            string apiKey = "";
            WepayKey key = _db.WepayKeys.Find(mchid);

            if (key == null)
            {
                return NotFound();
            }

            apiKey = key.api_key.Trim();

            if (apiKey == null || apiKey.Trim().Equals(""))
            {
                return NotFound();
            }
            string path = $"{Environment.CurrentDirectory}";
            /*
            string postJson = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            
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
            */
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
                CertificateEntry ce = new CertificateEntry("RSA", cerStr);

                //CertificateEntry ce = new CertificateEntry()

                var manager = new InMemoryCertificateManager();
                manager.AddEntry(ce);
                var options = new WechatTenpayClientOptions()
                {
                    MerchantId = key.mch_id.Trim(),
                    MerchantV3Secret = apiKey,
                    MerchantCertificateSerialNumber = key.key_serial,
                    MerchantCertificatePrivateKey = key.private_key,
                    PlatformCertificateManager = manager

                };

                var client = new WechatTenpayClient(options);
                Exception? verifyErr;
                bool valid = client.VerifyEventSignature(timeStamp, nonce, postJson, paySign, serial, out verifyErr);
                //valid = client.VerifyEventSignature()
                /*
                bool valid = client.VerifyEventSignature(
                    callbackTimestamp: timeStamp,
                    callbackNonce: nonce,
                    callbackBody: postJson,
                    callbackSignature: paySign,
                    callbackSerialNumber: serial
                );
                */
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
            var paymentList = await _db.OrderPayment.Where(o => o.out_trade_no.Trim().Equals(outTradeNumber.Trim())).ToListAsync();
            if (paymentList == null || paymentList.Count == 0)
            {
                return NotFound();
            }
            OrderPayment payment = paymentList[0];
            payment.status = "支付成功";
            _db.Entry(payment).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            OrderOnline order = await _db.OrderOnlines.FindAsync(payment.order_id);
            if (order == null)
            {
                return NotFound();
            }
            OrderPayment[] paymentsArr = await _db.OrderPayment.Where(p => p.order_id == order.id).ToArrayAsync();
            order.payments = paymentsArr;
            if (order.final_price <= order.paidAmount)
            {
                order.pay_state = 1;
                order.pay_time = DateTime.Now;
            }
            if (order.open_id.Trim().Equals(""))
            {
                order.open_id = payment.open_id.Trim();
            }
            _db.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            var pointList = await _db.Point.Where(p => p.memo.Contains("支付赠送龙珠，订单ID：" + order.id.ToString())).ToListAsync();
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
                await _db.Point.AddAsync(p);
                await _db.SaveChangesAsync();
            }

            switch (order.type.Trim())
            {
                case "服务":
                    MaintainLiveController maintainHelper = new MaintainLiveController(_db, _oriConfig);
                    await maintainHelper.MaitainOrderPaySuccess(order.id);
                    break;
                case "雪票":
                    SkiPassController skiPassHelper = new SkiPassController(_db, _oriConfig);
                    await skiPassHelper.CreateSkiPass(order);
                    break;
                case "押金":
                    List<RentOrder> rentOrderList = await _db.RentOrder
                        .Where(o => o.order_id == order.id).OrderByDescending(o => o.id).ToListAsync();
                    if (rentOrderList != null && rentOrderList.Count > 0)
                    {
                        RentOrder rentOrder = rentOrderList[0];
                        rentOrder.open_id = order.open_id;
                        _db.Entry(rentOrder).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                        await _rentHelper.StartRent(rentOrder.id);
                    }

                    break;
                case "UTV押金":
                    UTVController uCtl = new UTVController(_db, _oriConfig, _http);
                    var utvList = await _db.utvReserve.Where(u => u.order_id == order.id).ToListAsync();
                    if (utvList != null && utvList.Count == 1)
                    {
                        await uCtl.SetReservePaySuccess(utvList[0].id);
                    }
                    //await uCtl.SetReservePaySuccess()
                    break;
                default:
                    break;
            }


            return order;
            
        }

        [HttpGet]
        public async Task<OrderPaymentRefund> Refund(int refundId)
        {

            OrderPaymentRefund refund = await _db.OrderPaymentRefund.FindAsync(refundId);



            OrderPayment payment = await _db.OrderPayment.FindAsync(refund.payment_id);

            string notifyUrl = payment.notify.Trim().Replace("https://", "").Split('/')[0].Trim();
            notifyUrl = "https://" + notifyUrl + "/core/Tenpay/RefundCallback/" + payment.mch_id.ToString();
            refund.notify_url = notifyUrl.Trim();

            _db.OrderPaymentRefund.Entry(refund).State = EntityState.Modified;
            await _db.SaveChangesAsync();


            //var client = new WechatTenpayClient(options);
           
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

            var refunds = await _db.OrderPaymentRefund
                .Where(r => r.payment_id == payment.id)
                .AsNoTracking().ToListAsync();
            
            string outRefundNo = payment.out_trade_no + "_" + DateTime.Now.ToString("yyyyMMdd")
                +"_" + refunds.Count.ToString().PadLeft(2, '0');

            var client = new WechatTenpayClient(options);
            var request = new CreateRefundDomesticRefundRequest()
            {
                OutTradeNumber = payment.out_trade_no.Trim(),
                OutRefundNumber = outRefundNo.Trim(),
                Amount = new CreateRefundDomesticRefundRequest.Types.Amount()
                {
                    Total = (int)Math.Round(payment.amount * 100, 0),
                    Refund = (int)(Math.Round(refund.amount * 100))
                },
                Reason = refund.reason,
                NotifyUrl = refund.notify_url.Trim(),
                
            };
            
            var response = await client.ExecuteCreateRefundDomesticRefundAsync(request);
            try
            {
                string refundStrId = response.RefundId.Trim();
                if (refundStrId == null || refundStrId.Trim().Equals(""))
                {
                    return refund;
                }
                //refund.status = refundId;
                refund.refund_id = refundStrId;
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

            return null;
        }

        [HttpPost("{mchid}")]
        public async Task<ActionResult<string>> RefundCallback(int mchid, [FromBody]object postData)
        {
            string paySign = _http.HttpContext.Request.Headers["Wechatpay-Signature"].ToString();
            string nonce = _http.HttpContext.Request.Headers["Wechatpay-Nonce"].ToString();
            string serial = _http.HttpContext.Request.Headers["Wechatpay-Serial"].ToString();
            string timeStamp = _http.HttpContext.Request.Headers["Wechatpay-Timestamp"].ToString();


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

            CertificateEntry ce = new CertificateEntry("RSA", serial, cerStr, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);


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
    }
}
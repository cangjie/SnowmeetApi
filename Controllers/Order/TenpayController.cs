using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aop.Api.Domain;
using HttpHandlerDemo;
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
using SnowmeetApi.Controllers.User;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class TenpayController : ControllerBase
    {
        private struct DownloadUrl
        {
            public string download_url { get; set; }
        }


        public ApplicationDBContext _db;
        public IConfiguration _oriConfig;
        public IHttpContextAccessor _http;
        public OrderPaymentController _orderPaymentHelper;
        public RentController _rentHelper;
        public string _appId = "";

        public string _domain = "";

        public MemberController _memberHelper;
        public TenpayController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _domain = _http.HttpContext.Request.Host.ToString();
            _db = context;
            _oriConfig = config;
            _http = httpContextAccessor;
            _orderPaymentHelper = new OrderPaymentController(context, config, httpContextAccessor);
            _appId = _oriConfig.GetSection("Settings").GetSection("AppId").Value.Trim();
            _rentHelper = new RentController(_db, _oriConfig, _http);
            _memberHelper = new MemberController(context, config);
            
        }

        [NonAction]
        public async Task<OrderPayment> TenpayRequest(int paymentId, string sessionKey, bool profitShare = false)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (user == null)
            {
                return null;
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            if (payment == null)
            {
                return null;
            }
            OrderOnline order = await _db.OrderOnlines.FindAsync(payment.order_id);
            if (order == null)
            {
                return null;
            }
            if (!payment.pay_method.Trim().Equals("微信支付") || !payment.status.Trim().Equals("待支付"))
            {
                return null;
            }
            if (order.status.Trim().Equals("支付完成") || order.status.Trim().Equals("订单关闭"))
            {
                return null;
            }
            string timeStamp = Util.getTime13().ToString();
            int mchid = _orderPaymentHelper.GetMchId(order);
            

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
                    Models.Product.Product p = await _db.Product.FindAsync(details[i].product_id);
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

            string notifyUrl = "https://" + _domain.Trim() + "/core/Tenpay/TenpayPaymentCallBack/" + mchid.ToString();
            string? outTradeNo = payment.out_trade_no;
            if (outTradeNo == null )
            { 
                outTradeNo = order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
            }

            //CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail  dtl = new CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail();
            //dtl.

             //order.id.ToString().PadLeft(6, '0') + payment.id.ToString().PadLeft(2, '0') + timeStamp.Substring(3, 10);
            var client = await GetClient(mchid);
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
                },
                Settlement = new CreatePayTransactionJsapiRequest.Types.Settlement()
                {
                    IsProfitSharing = profitShare
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
                return payment;
            }
            return null;
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

                        OrderPayment sucPay = await _db.OrderPayment.Where(p => (p.out_trade_no.Trim().Equals(outTradeNumber.Trim()) && p.status.Trim().Equals("支付成功")))
                            .OrderByDescending(p => p.id).FirstAsync();
                        if (sucPay != null)
                        {
                            sucPay.wepay_trans_id = transactionId.Trim();
                            _db.OrderPayment.Entry(sucPay).State = EntityState.Modified;
                            await _db.SaveChangesAsync();
                        }
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
            //payment.ali_trade_no = tradeNo.Trim();
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
                    SkiPassController skiPassHelper = new SkiPassController(_db, _oriConfig, _http);
                    await skiPassHelper.CreateSkiPass(order.id);
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

        

        [NonAction]
        public async Task<OrderPaymentRefund> Refund(int refundId)
        {


            OrderPaymentRefund refund = await _db.OrderPaymentRefund.FindAsync(refundId);



            OrderPayment payment = await _db.OrderPayment.FindAsync(refund.payment_id);


            var refunds = await _db.OrderPaymentRefund
                .Where(r => r.payment_id == payment.id)
                .AsNoTracking().ToListAsync();
            

            string notifyUrl = payment.notify.Trim().Replace("https://", "").Split('/')[0].Trim();
            notifyUrl = "https://" + notifyUrl + "/core/Tenpay/RefundCallback/" + payment.mch_id.ToString();
            refund.notify_url = notifyUrl.Trim();
            //string outRefundNo = payment.out_trade_no + "_REFND_" + DateTime.Now.ToString("yyyyMMdd")
            //    +"_" + refunds.Count.ToString().PadLeft(2, '0');
            //refund.out_refund_no = outRefundNo;


            _db.OrderPaymentRefund.Entry(refund).State = EntityState.Modified;
            await _db.SaveChangesAsync();


            //var client = new WechatTenpayClient(options);
           
            

            
            
            var client = await GetClient((int)payment.mch_id);
            var request = new CreateRefundDomesticRefundRequest()
            {
                OutTradeNumber = payment.out_trade_no.Trim(),
                OutRefundNumber = refund.out_refund_no.Trim(),  
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
                        var callbackResource = client.DecryptEventResource<SKIT.FlurlHttpClient.Wechat.TenpayV3.Events.RefundResource>(callbackModel);
                        OrderPayment payment = await _db.OrderPayment
                            .Where(p => p.out_trade_no.Trim().Equals(callbackResource.OutTradeNumber)
                            && p.pay_method.Trim().Equals("微信支付") && p.status.Trim().Equals("支付成功"))
                            .OrderByDescending(p => p.id).FirstAsync();


                        double refundAmount = Math.Round(((double)callbackResource.Amount.Total) / 100, 2);
                        OrderPaymentRefund refund = await _db.OrderPaymentRefund
                            .Where(r => (r.refund_id.Trim().Equals(callbackResource.RefundId.Trim()))).FirstAsync();

                        refund.state = 1;
                        refund.memo = callbackResource.TransactionId;
                        refund.TransactionId = callbackResource.TransactionId.Trim();
                        _db.Entry(refund).State = EntityState.Modified;
                        await _db.SaveChangesAsync();

                        await _orderPaymentHelper.SetPaymentRefundSuccess(payment.id);
                    }
                    catch
                    {

                    }
                    
                        
                    

                }

            }
            return "{ \r\n \"code\": \"SUCCESS\", \r\n \"message\": \"成功\" \r\n}";
        }

        [HttpGet("{mchId}")]
        public async Task<string> BindKol(int mchId, int kolId)
        {
            string ret = "";
            
            Kol kol = await _db.kol.FindAsync(kolId);
            
            var req = new AddProfitSharingReceiverRequest()
            {
                AppId = _appId,
                Type = "PERSONAL_OPENID",
                Account = kol.wechat_open_id.Trim(),
                RelationType = "USER"
            };
            var client = await GetClient(mchId);
            var res = await client.ExecuteAddProfitSharingReceiverAsync(req);
            ret = res.IsSuccessful().ToString().ToLower();
            if (ret.Equals("true"))
            {
                kol.wechat_bind = 1;
                _db.kol.Entry(kol).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return ret;
        }

        [NonAction]
        public async Task UnFreezeAll()
        {
            DateTime startDate = DateTime.Parse("2024-10-15");
            List<OrderPayment> pList = await _db.OrderPayment
                .Where(p => p.create_date > startDate && p.status.Trim().Equals("支付成功") )
                .OrderByDescending(p=>p.id).AsNoTracking().ToListAsync();
            for(int i = 0; i < pList.Count; i++)
            {
                try
                {
                    await ShareFinish(pList[i].id, "");
                }
                catch
                {

                }
            }
        }

        [NonAction]
        public async Task ShareFinish(int paymentId, string description)
        {
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            WechatTenpayClient client = await GetClient((int)payment.mch_id);
            WepayKey key = await _db.WepayKeys.FindAsync(payment.mch_id);
            //var req = CreateProfitSharingReturnOrderRequest
            var req = new SetProfitSharingOrderUnfrozenRequest()
            {
                TransactionId = payment.wepay_trans_id,
                OutOrderNumber = payment.out_trade_no.Trim(),
                Description = description
            };
            var res = await client.ExecuteSetProfitSharingOrderUnfrozenAsync(req);

        }

        [NonAction]
        public async Task<PaymentShare> Share(int paymentShareId)
        {
            PaymentShare share = await _db.paymentShare.FindAsync(paymentShareId);
            if (share == null)
            {
                return null;
            }
            Kol kol = await _db.kol.FindAsync(share.kol_id);
            OrderPayment payment = await _db.OrderPayment.FindAsync(share.payment_id);
            string bindRet = await BindKol((int)payment.mch_id, share.kol_id);
            if (!bindRet.Trim().Equals("true"))
            {
                return null;
            }
            
            
            WechatTenpayClient client = await GetClient((int)payment.mch_id);
            List<CreateProfitSharingOrderRequest.Types.Receiver> rl = new List<CreateProfitSharingOrderRequest.Types.Receiver>();
            CreateProfitSharingOrderRequest.Types.Receiver r = new CreateProfitSharingOrderRequest.Types.Receiver()
            {
                Type = "PERSONAL_OPENID",
                Account = kol.wechat_open_id,
                Amount =  (int)Math.Round(share.amount * 100),
                Description = share.memo.Trim()
            };
            rl.Add(r);
            WepayKey key = await _db.WepayKeys.FindAsync(payment.mch_id);
            var req = new CreateProfitSharingOrderRequest()
            {
                AppId = _appId,
                TransactionId = (string)payment.wepay_trans_id,
                OutOrderNumber = share.out_trade_no.Trim(),
                ReceiverList = rl,
                WechatpayCertificateSerialNumber = key.key_serial.Trim()
            };
            share.submit_date = DateTime.Now;
            var res = await client.ExecuteCreateProfitSharingOrderAsync(req);
            if (res.IsSuccessful())
            {
                share.state = 1;
            }
            else
            {
                share.state = -1;
                share.ret_msg = res.ErrorMessage.Trim();
            }
            _db.paymentShare.Entry(share).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return share; 
        }

        [NonAction]
        private async Task<WechatTenpayClient> GetClient(int mchId)
        {
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
            return new WechatTenpayClient(options);
        }

        [HttpGet]
        public async Task DownloadToday()
        {
            int[] mchId = new int[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 15, 17 };
            for (int j = 0; j < mchId.Length; j++)
            {
                await RequestTradeBill(mchId[j], DateTime.Now.Date.AddDays(-1));
                await RequestFlowBill(mchId[j], DateTime.Now.Date.AddDays(-1));
            }

        }

        [HttpGet]
        public async Task DownloadHistoryData(DateTime date)
        {
            int[] mchId = new int[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 15, 17 };
            for (int j = 0; j < mchId.Length; j++)
            {
                await RequestTradeBill(mchId[j], date);
                await RequestFlowBill(mchId[j], date);
            }

        }

        [HttpGet]
        public async Task<ActionResult<int>> ImportTrans(string path = "/Users/cangjie/Desktop/trans.csv")
        {
            System.IO.StreamReader sr = new StreamReader(path);
            string content = sr.ReadToEnd().Trim();
            sr.Close();
            string[] lineArr = content.Split('\n');
            string[] summaryFields = lineArr[lineArr.Length - 1].Split(',');
            int count = 0;
            for (int i = 1; i < lineArr.Length - 1; i++)
            {
                string[] fieldArr = lineArr[i].Split(',');
                string transDateStr = fieldArr[1].Trim().Replace("`", "");
                if (transDateStr.Equals(""))
                {
                    continue;
                }
                try
                {
                    DateTime transDate = DateTime.Parse(transDateStr);
                        

                }
                catch
                {
                    continue;
                }
                WepayBalance b = new WepayBalance();
                for (int j = 0; j < fieldArr.Length-1; j++)
                {

                    string v = fieldArr[j+1].Trim().Replace("`", "");
                    
                    switch (j)
                    {
                        case 0:
                            b.trans_date = DateTime.Parse(v);
                            break;
                        case 1:
                            b.app_id = v.Trim();
                            break;
                        case 2:
                            b.mch_id = v.Trim();
                            break;
                        case 3:
                            b.spc_mch_id = v.Trim();
                            break;
                        case 4:
                            b.device_id = v.Trim();
                            break;
                        case 5:
                            b.wepay_order_num = v.Trim();
                            break;
                        case 6:
                            b.out_trade_no = v.Trim();
                            break;
                        case 7:
                            b.open_id = v.Trim();
                            break;
                        case 8:
                            b.trans_type = v.Trim();
                            break;
                        case 9:
                            b.pay_status = v.Trim();
                            break;
                        case 10:
                            b.bank = v.Trim();
                            break;
                        case 11:
                            b.currency = v.Trim();
                            break;
                        case 12:
                            b.settle_amount = double.Parse(v.Trim());
                            break;
                        case 13:
                            b.coupon_amount = double.Parse(v.Trim());
                            break;
                        case 14:
                            b.refund_no = v.Trim();
                            break;
                        case 15:
                            b.out_refund_no = v.Trim();
                            break;
                        case 16:
                            b.refund_amount = double.Parse(v.Trim());
                            break;
                        case 17:
                            b.coupon_refund_amount = double.Parse(v.Trim());
                            break;
                        case 18:
                            b.refund_type = v.Trim();
                            break;
                        case 19:
                            b.refund_status = v.Trim();
                            break;
                        case 20:
                            b.product_name = v.Trim();
                            break;
                        case 21:
                            b.product_package = v.Trim();
                            break;
                        case 22:
                            b.fee = double.Parse(v.Trim());
                            break;
                        case 23:
                            b.fee_rate = v.Trim();
                            break;
                        case 24:
                            b.order_amount = double.Parse(v.Trim());
                            break;
                        case 25:
                            try
                            {
                                if (v.Trim().Equals(""))
                                {
                                    b.request_refund_amount = 0;
                                }
                                else
                                {
                                    b.request_refund_amount = double.Parse(v.Trim());
                                }
                            }
                            catch(Exception err)
                            {
                                Console.WriteLine(err.ToString());
                            }
                            break;
                        case 26:
                            b.fee_rate_memo = v.Trim();
                            break;
                        default:
                            break;
                    }
                    
                    //count += await _db.SaveChangesAsync();
                }
                await _db.wepayBalance.AddAsync(b);
                count++;

            }
            int rowsCount = await _db.SaveChangesAsync();

            return Ok(count);
        }

        [HttpGet("{mchId}")]
        public async Task  RequestTradeBill(int mchId, DateTime billDate)
        {
            Console.WriteLine(mchId.ToString() + "\t" + billDate.ToShortDateString());
            var summaryList = await _db.wepaySummary.Where(s => s.trans_date.Date == billDate.Date && s.mch_id == mchId)
                .AsNoTracking().ToListAsync();
            if (summaryList != null && summaryList.Count > 0)
            {
                return;
            }

            WepayKey k = await _db.WepayKeys.FindAsync(mchId);

            string getUrl = "https://api.mch.weixin.qq.com/v3/bill/tradebill?bill_date=" + billDate.ToString("yyyy-MM-dd");

            HttpHandler handle = new HttpHandler(k.mch_id.Trim(), k.key_serial.Trim(), k.private_key.Trim());

            HttpRequestMessage req = new HttpRequestMessage();
            req.RequestUri = new Uri(getUrl);
            CancellationToken cancel = new CancellationToken();
            HttpResponseMessage res = await handle.GetWebContent(req, cancel);
            StreamReader sr = new StreamReader(await res.Content.ReadAsStreamAsync());
            string str = await sr.ReadToEndAsync();
            sr.Close();
            DownloadUrl downloadUrl = Newtonsoft.Json.JsonConvert.DeserializeObject<DownloadUrl>(str);

            if (downloadUrl.download_url == null)
            {
                return;
            }
            Console.WriteLine(downloadUrl.download_url.Trim());
            handle = new HttpHandler(k.mch_id.Trim(), k.key_serial.Trim(), k.private_key.Trim());
            req = new HttpRequestMessage();
            req.RequestUri = new Uri(downloadUrl.download_url.Trim());
            res = await handle.GetWebContent(req, cancel);
            sr = new StreamReader(await res.Content.ReadAsStreamAsync());
            str = (await sr.ReadToEndAsync()).Trim();
            sr.Close();
            Console.WriteLine(str);

            string[] lineArr = str.Split('\n');
            string[] summaryFields = lineArr[lineArr.Length - 1].Split(',');


            WepaySummary summary = new WepaySummary()
            {
                id = 0,
                mch_id = mchId,
                trans_date = billDate.Date,
                trans_num = int.Parse(summaryFields[0].Replace("`", "")),
                total_settle_amount = double.Parse(summaryFields[1].Replace("`", "")),
                total_refund_amount = double.Parse(summaryFields[2].Replace("`", "")),
                coupon_refund_amount = double.Parse(summaryFields[3].Replace("`", "")),
                total_fee = double.Parse(summaryFields[4].Replace("`", "")),
                total_order_amount = double.Parse(summaryFields[5].Replace("`", "")),
                total_request_refund_amount = double.Parse(summaryFields[6].Replace("`", ""))
            };


            WepayBalance[] balanceArr = new WepayBalance[lineArr.Length - 3];


            for (int i = 1; i < lineArr.Length - 2; i++)
            {
                WepayBalance b = new WepayBalance();
                string[] fieldArr = lineArr[i].Split(',');
                for (int j = 0; j < fieldArr.Length; j++)
                {
                    string v = fieldArr[j].Trim().Replace("`", "");
                    switch (j)
                    {
                        case 0:
                            b.trans_date = DateTime.Parse(v);
                            break;
                        case 1:
                            b.app_id = v.Trim();
                            break;
                        case 2:
                            b.mch_id = v.Trim();
                            break;
                        case 3:
                            b.spc_mch_id = v.Trim();
                            break;
                        case 4:
                            b.device_id = v.Trim();
                            break;
                        case 5:
                            b.wepay_order_num = v.Trim();
                            break;
                        case 6:
                            b.out_trade_no = v.Trim();
                            break;
                        case 7:
                            b.open_id = v.Trim();
                            break;
                        case 8:
                            b.trans_type = v.Trim();
                            break;
                        case 9:
                            b.pay_status = v.Trim();
                            break;
                        case 10:
                            b.bank = v.Trim();
                            break;
                        case 11:
                            b.currency = v.Trim();
                            break;
                        case 12:
                            b.settle_amount = double.Parse(v.Trim());
                            break;
                        case 13:
                            b.coupon_amount = double.Parse(v.Trim());
                            break;
                        case 14:
                            b.refund_no = v.Trim();
                            break;
                        case 15:
                            b.out_refund_no = v.Trim();
                            break;
                        case 16:
                            b.refund_amount = double.Parse(v.Trim());
                            break;
                        case 17:
                            b.coupon_refund_amount = double.Parse(v.Trim());
                            break;
                        case 18:
                            b.refund_type = v.Trim();
                            break;
                        case 19:
                            b.refund_status = v.Trim();
                            break;
                        case 20:
                            b.product_name = v.Trim();
                            break;
                        case 21:
                            b.product_package = v.Trim();
                            break;
                        case 22:
                            b.fee = double.Parse(v.Trim());
                            break;
                        case 23:
                            b.fee_rate = v.Trim();
                            break;
                        case 24:
                            b.order_amount = double.Parse(v.Trim());
                            break;
                        case 25:
                            b.request_refund_amount = double.Parse(v.Trim());
                            break;
                        case 26:
                            b.fee_rate_memo = v.Trim();
                            break;
                        default:
                            break;
                    }
                }
                balanceArr[i-1] = b;
            }

            if (summary.trans_num == balanceArr.Length)
            {
                await _db.wepaySummary.AddAsync(summary);
                await _db.SaveChangesAsync();
                if (summary.id > 0)
                {
                    try
                    {
                        for (int i = 0; i < balanceArr.Length; i++)
                        {
                            balanceArr[i].summary_id = summary.id;
                            await _db.wepayBalance.AddAsync(balanceArr[i]);
                        }
                        await _db.SaveChangesAsync();
                    }
                    catch
                    {
                        _db.wepaySummary.Remove(summary);
                        await _db.SaveChangesAsync();
                    }
                }

            }
        }

        [HttpGet]
        public async Task<ActionResult<int>> ImportFlow(string mchId = "1636404775", string path = "/Users/cangjie/Desktop/flow.csv")
        {
            System.IO.StreamReader sr = new StreamReader(path);
            string content = sr.ReadToEnd().Trim();
            sr.Close();
            string[] lineArr = content.Split('\r');
            int count = 0;
            for (int i = 1; i < lineArr.Length - 1; i++)
            {
                string[] fields = lineArr[i].Trim().Split(',');
                string billDateStr = fields[1].Trim().Replace("`", "");
                if (billDateStr.Equals(""))
                {
                    continue;
                }
                try
                {
                    DateTime.Parse(billDateStr);
                }
                catch
                {
                    continue;
                }
                //string[] fields = lineArr[i].Trim().Split(',');
                for (int j = 0; j < fields.Length; j++)
                {
                    fields[j] = fields[j].Replace("`", "");
                }
                WepayFlowBill bill = new WepayFlowBill()
                {
                    mch_id = mchId,
                    bill_date_time = DateTime.Parse(fields[1]),
                    biz_no = fields[2].Trim(),
                    flow_no = fields[3].Trim(),
                    biz_name = fields[4].Trim(),
                    biz_type = fields[5].Trim(),
                    bill_type = fields[6].Trim(),
                    amount = double.Parse(fields[7].Trim()),
                    surplus = double.Parse(fields[8].Trim()),
                    oper = fields[9].Trim(),
                    memo = fields[10].Trim(),
                    invoice_id = fields[11].Trim()
                };
                await _db.wepayFlowBill.AddAsync(bill);
                count++;
            }
            count = await _db.SaveChangesAsync();
            return Ok(count);
        }

        [HttpGet]
        public async Task RequestFlowBill(int mchId, DateTime billDate)
        {
            WepayKey k = await _db.WepayKeys.FindAsync(mchId);

            string getUrl = "https://api.mch.weixin.qq.com/v3/bill/fundflowbill?bill_date=" + billDate.ToString("yyyy-MM-dd");

            HttpHandler handle = new HttpHandler(k.mch_id.Trim(), k.key_serial.Trim(), k.private_key.Trim());

            HttpRequestMessage req = new HttpRequestMessage();
            req.RequestUri = new Uri(getUrl);
            CancellationToken cancel = new CancellationToken();
            HttpResponseMessage res = await handle.GetWebContent(req, cancel);
            StreamReader sr = new StreamReader(await res.Content.ReadAsStreamAsync());
            string str = await sr.ReadToEndAsync();
            sr.Close();
            DownloadUrl downloadUrl = Newtonsoft.Json.JsonConvert.DeserializeObject<DownloadUrl>(str);

            if (downloadUrl.download_url == null)
            {
                return;
            }
            Console.WriteLine(downloadUrl.download_url.Trim());
            handle = new HttpHandler(k.mch_id.Trim(), k.key_serial.Trim(), k.private_key.Trim());
            req = new HttpRequestMessage();
            req.RequestUri = new Uri(downloadUrl.download_url.Trim());
            res = await handle.GetWebContent(req, cancel);
            sr = new StreamReader(await res.Content.ReadAsStreamAsync());
            str = (await sr.ReadToEndAsync()).Trim();
            sr.Close();
            Console.WriteLine(str);
            string[] lineArr = str.Split('\r');
            for (int i = 1; i < lineArr.Length - 2; i++)
            {
                Console.WriteLine(lineArr[i].Trim());
                string[] fields = lineArr[i].Trim().Split(',');
                for (int j = 0; j < fields.Length; j++)
                {
                    fields[j] = fields[j].Replace("`", "");
                }
                WepayFlowBill bill = new WepayFlowBill()
                {
                    mch_id = k.mch_id,
                    bill_date_time = DateTime.Parse(fields[0]),
                    biz_no = fields[1].Trim(),
                    flow_no = fields[2].Trim(),
                    biz_name = fields[3].Trim(),
                    biz_type = fields[4].Trim(),
                    bill_type = fields[5].Trim(),
                    amount = double.Parse(fields[6].Trim()),
                    surplus = double.Parse(fields[7].Trim()),
                    oper = fields[8].Trim(),
                    memo = fields[9].Trim(),
                    invoice_id = fields[10].Trim()
                };
                try
                {
                    await _db.wepayFlowBill.AddAsync(bill);
                }
                catch
                {

                }
            }
            try
            {
                await _db.SaveChangesAsync();
            }
            catch
            {

            }
        }

        [HttpGet]
        public async Task<ActionResult<WepayReport>> GetWepayBalance(DateTime startDate, DateTime endDate, string sessionKey, string mchId = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            mchId = Util.UrlDecode(mchId);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            if (user == null || !user.isAdmin)
            {
                return NotFound();
            }

            var paidArr = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date
                && b.trans_date.Date <= endDate.Date && b.pay_status.Trim().Equals("SUCCESS")
                && (mchId.Equals("") || b.mch_id.Trim().Equals(mchId)))
                .OrderByDescending(b => b.trans_date).AsNoTracking().ToListAsync();

            
            
            
            var wepayKeyList = await _db.WepayKeys.ToListAsync();

            List<WepayBalance> retList = new List<WepayBalance>();
            int maxLen = 0;
            for (int i = 0; i < paidArr.Count; i++)
            {
                WepayBalance b = paidArr[i];
                string orderId = "";
                int orderOnlineId = 0;
                string shop = "";
                string orderType = "";
                string mchName = "";
                string cell = "";
                string realName = "";
                string gender = "";

                var pList = await _db.OrderPayment.Where(p => p.status.Equals("支付成功")
                    && p.out_trade_no.Trim().Equals(b.out_trade_no.Trim())).ToListAsync();
                if (pList != null && pList.Count > 0)
                {
                    orderOnlineId = pList[0].order_id;
                }

                OrderOnline? orderOnline = await _db.OrderOnlines.FindAsync(orderOnlineId);
                if (orderOnline != null)
                {
                    shop = orderOnline.shop.Trim();
                    switch (orderOnline.type.Trim())
                    {
                        case "服务":
                            orderType = "养护";
                            var maintailList = await _db.MaintainLives.Where(m => m.order_id == orderOnlineId)
                                .AsNoTracking().ToListAsync();
                            for (int j = 0; j < maintailList.Count; j++)
                            {
                                orderId = orderId + (j == 0 ? "" : ",") + maintailList[j].task_flow_num.Trim();
                            }
                            break;
                        case "押金":
                            orderType = "租赁";
                            var rentOrderList = await _db.RentOrder.Where(r => r.order_id == orderOnlineId)
                                .AsNoTracking().ToListAsync();
                            if (rentOrderList != null && rentOrderList.Count > 0)
                            {
                                orderId = rentOrderList[0].id.ToString();
                                
                            }
                            break;
                        case "店销现货":
                            orderType = "店销现货";
                            orderId = orderOnlineId.ToString();
                            break;
                        case "雪票":
                            orderType = "雪票";
                            var skiPassList = await _db.OrderOnlineDetails
                                .Where(d => d.OrderOnlineId == orderOnlineId)
                                .AsNoTracking().ToListAsync();
                            for (int j = 0; skiPassList != null && j < skiPassList.Count; j++)
                            {
                                orderId = orderId + (j == 0 ? "" : ",") + skiPassList[j].product_name
                                    + "x" + skiPassList[j].count.ToString();
                            }
                            break;
                        default:
                            break;
                    }
                    //MiniAppUser mUser = await _db.MiniAppUsers.FindAsync(orderOnline.open_id);
                    SnowmeetApi.Models.Users.Member mUser = await _memberHelper.GetMember(orderOnline.open_id, "wechat_mini_openid");
                    if (mUser != null)
                    {
                        cell = mUser.cell.Trim();
                        realName = mUser.real_name.Trim();
                        gender = mUser.gender.Trim();
                    }
                }

                for (int j = 0; j < wepayKeyList.Count; j++)
                {
                    if (wepayKeyList[j].mch_id.Trim().Equals(b.mch_id))
                    {
                        mchName = wepayKeyList[j].mch_name.Trim();
                        break;
                    }
                }
                b.orderId = orderId.Trim();
                b.refunds = await _db.wepayBalance.Where(c =>  c.pay_status.Trim().Equals("REFUND")
                    && c.out_trade_no.Trim().Equals(b.out_trade_no)
                    && c.refund_status.Trim().Equals("SUCCESS"))
                    .OrderBy(b => b.id).AsNoTracking().ToListAsync();
                maxLen = Math.Max(maxLen, b.refunds.Count);
                for (int j = 0; j < b.refunds.Count; j++)
                {
                    b.totalRefundAmount += b.refunds[j].refund_amount;
                    b.totalRefundAmountReal += b.refunds[j].real_refund_amount;
                    b.totalRefundFee += Math.Abs(b.refunds[j].fee);
                }
                b.netAmount = b.receiveable_amount - b.totalRefundAmountReal ;
                b.shop = shop;
                b.orderType = orderType;
                b.real_name = realName;
                b.cell = cell;
                b.gender = gender;
                b.mchName = mchName;
                b.dayOfWeek = Util.GetDayOfWeek(b.trans_date.Date);
                //b.fee = Math.Abs(b.fee);
                //b.receiveable_amount = b.settle_amount - b.fee;
                retList.Add(b);

            }

            var withDraw = await _db.wepayFlowBill.Where(b => (b.bill_date_time.Date >= startDate.Date
                && b.bill_date_time.Date <= endDate.Date && b.biz_type.Trim().Equals("提现")
                && b.bill_type.Trim().Equals("支出")
                && (mchId.Trim().Equals("") || b.mch_id.Trim().Equals(mchId))))
                .AsNoTracking().ToListAsync();

            for (int i = 0; withDraw != null && i < withDraw.Count; i++)
            {
                WepayBalance b = new WepayBalance();
                b.trans_date = withDraw[i].bill_date_time;
                b.orderType = "提现";
                b.mch_id = withDraw[i].mch_id;
                b.mchName = "";
                for (int j = 0; j < wepayKeyList.Count; j++)
                {
                    if (wepayKeyList[j].mch_id.Trim().Equals(b.mch_id))
                    {
                        b.mchName = wepayKeyList[j].mch_name.Trim();
                        break;
                    }
                }
                b.drawAmount = withDraw[i].amount;
                b.dayOfWeek = Util.GetDayOfWeek(b.trans_date);
                retList.Add(b);

            }

            List<WepayBalance> ret = retList.OrderByDescending(b => b.trans_date).ToList();



            WepayReport report = new WepayReport();
            report.maxRefundLength = maxLen;
            report.items = ret;

            return Ok(report);
        }

        [HttpGet]
        public async Task<ActionResult<int>> CreateStatement(string mchId = "1636404775")
        {
            var bList = await _db.wepayBalance.Where(b => (b.statement_id == 0 && b.mch_id.Trim().Equals(mchId.Trim()) ))
                .OrderBy(b => b.trans_date).ToListAsync();
            for(int i = 0; i < bList.Count; i++)
            {
                
                switch(bList[i].pay_status.Trim())
                {
                    case "SUCCESS":
                        var flowList = await _db.wepayFlowBill
                            .Where(b => b.biz_no.Trim().Equals(bList[i].wepay_order_num.Trim()))
                            .ToListAsync();
                        DateTime billDate = DateTime.MinValue;
                        string flow_no = "";
                        string opType = "";
                        string oper = "";
                        string oper_account = "";
                        double platFormRemain = 0;
                        for(int j = 0; j < flowList.Count; j++)
                        {
                            if (flowList[j].biz_name.Trim().Equals("交易") && flowList[j].biz_type.Trim().Equals("交易")
                                && flowList[j].bill_type.Trim().Equals("收入"))
                            {
                                flow_no = flowList[j].flow_no.Trim();
                                opType = "系统";
                                billDate = flowList[j].bill_date_time;
                            }
                            if (flowList[j].biz_name.Trim().Equals("交易") && flowList[j].biz_type.Trim().Equals("扣除交易手续费")
                                && flowList[j].bill_type.Trim().Equals("支出"))
                            {
                                platFormRemain = flowList[j].surplus;
                            }
                                
                        }
                        FinancialStatement s = new FinancialStatement()
                        {
                            id = 0,
                            pay_type = "微信支付",
                            mch_id = mchId.Trim(),
                            season = Util.GetSeason(bList[i].trans_date),
                            month = bList[i].trans_date.Year.ToString() + "-" + bList[i].trans_date.Month.ToString().PadLeft(2, '0'),
                            order_type = "收款",
                            day_of_week = Util.GetDayOfWeek(bList[i].trans_date),
                            trans_date = bList[i].trans_date,
                            bill_date = billDate,
                            platform_order_no = bList[i].wepay_order_num.Trim(),
                            out_trade_no = bList[i].out_trade_no.Trim(),
                            flow_no = flow_no,
                            open_id = bList[i].open_id.Trim(),
                            op_type = opType,
                            oper = oper.Trim(),
                            oper_account = oper_account,
                            amount = bList[i].settle_amount,
                            fee = bList[i].fee,
                            settle = bList[i].settle_amount - bList[i].fee,
                            amount_sum = 0,
                            fee_sum = 0,
                            settle_sum = 0,
                            platform_remain = platFormRemain,
                            coming = 0,
                            bank_remain = 0,
                            can_withdraw = 0,
                            withdraw = null,
                            refund_amount = null,
                            refund_fee = null,
                            refund_settle = null,
                            charge = null

                        };  
                        await _db.financialStatement.AddAsync(s);
                        await _db.SaveChangesAsync();
                        bList[i].statement_id = s.id;
                        _db.wepayBalance.Entry(bList[i]).State = EntityState.Modified;
                        for(int j = 0; j < flowList.Count; j++)
                        {
                            flowList[j].statement_id = s.id;
                            _db.wepayFlowBill.Entry(flowList[j]).State = EntityState.Modified;

                        }
                        await _db.SaveChangesAsync();
                        break;
                    case "REFUND":
                        var fList = await _db.wepayFlowBill
                            .Where(f => f.biz_no.Trim().Equals(bList[i].wepay_order_num))
                            .ToListAsync();
                        DateTime rBillDate = DateTime.MinValue;
                        string rFlowNo = "";
                        for(int j = 0; j < fList.Count; j++)
                        {
                            rFlowNo = fList[j].flow_no.Trim();
                            if (!rFlowNo.Trim().Equals(""))
                            {
                                break;
                            }
                        }

                        fList = await _db.wepayFlowBill
                            .Where(f => f.flow_no.Trim().Equals(rFlowNo) && f.biz_name.Equals("退款") && f.biz_type.Trim().Equals("退款") && f.bill_type.Trim().Equals("支出"))
                            .ToListAsync();
                        




                        string rOpType = "系统";
                        string rOperName = "";
                        string rOperAccount = "";
                        double rPlatformRemain = 0;
                        double rRealRefund = 0;
                        string rPlatformNo = "";
                        for(int j = 0; j < fList.Count; j++)
                        {
                            rBillDate = fList[j].bill_date_time;
                            rFlowNo = fList[j].flow_no.Trim();
                            string rOper = fList[j].oper;
                            rPlatformRemain = fList[j].surplus;
                            rRealRefund = fList[j].amount;
                            rPlatformNo = fList[j].biz_no;
                            if (rOper.IndexOf("(")>=0 || rOper.IndexOf("@")>=0)
                            {
                                rOpType = "人工";
                                if (rOper.ToLower().Trim().IndexOf("kou")>=0)
                                {
                                    rOperName = "寇芳";
                                    rOperAccount = rOper;
                                }
                                else if (rOper.ToLower().Trim().IndexOf("buguai")>=0)
                                {
                                    rOperName = "舒娟";
                                    rOperAccount = rOper;
                                }
                                else
                                {
                                    rOperName = "管理员";
                                    rOperAccount = rOper;
                                }

                            }
                            else if (rOper.ToUpper().IndexOf("API") >= 0)
                            {
                                rOperName = "API";
                                rOperAccount = rOper.Trim();
                            }
                            else
                            {

                            }


                        }
                        FinancialStatement r = new FinancialStatement()
                        {
                            id = 0,
                            pay_type = "微信支付",
                            mch_id = mchId.Trim(),
                            season = Util.GetSeason(bList[i].trans_date),
                            month = bList[i].trans_date.Year.ToString() + "-" + bList[i].trans_date.Month.ToString().PadLeft(2, '0'),
                            order_type = "退款",
                            day_of_week = Util.GetDayOfWeek(bList[i].trans_date),
                            trans_date = bList[i].trans_date,
                            bill_date = rBillDate,
                            platform_order_no = rPlatformNo,
                            out_trade_no = bList[i].out_refund_no,
                            flow_no = rFlowNo,
                            open_id = bList[i].open_id.Trim(),
                            op_type =  rOpType,
                            oper = rOperName,
                            oper_account = rOperAccount,
                            amount = null,
                            fee = null,
                            settle = null,
                            amount_sum = 0,
                            fee_sum = 0,
                            settle_sum = 0,
                            platform_remain = rPlatformRemain,
                            coming = 0,
                            bank_remain = 0,
                            can_withdraw = 0,
                            withdraw = null,
                            refund_amount = rRealRefund,
                            refund_fee = -1 * bList[i].fee,
                            refund_settle = bList[i].request_refund_amount,
                            charge = null

                        };  
                        await _db.financialStatement.AddAsync(r);
                        await _db.SaveChangesAsync();
                        for(int j = 0; j < fList.Count; j++)
                        {
                            fList[j].statement_id = r.id;
                            _db.wepayFlowBill.Entry(fList[j]).State = EntityState.Modified;

                        }
                        bList[i].statement_id = r.id;
                        _db.wepayBalance.Entry(bList[i]).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                        break;
                    default:
                        break;
                }

                
                

            }


            var asFlowList = await _db.wepayFlowBill
                .Where(f => (f.biz_name.Trim().Equals("充值/提现") && f.statement_id == 0 && f.mch_id.Trim().Equals(mchId.Trim())))
                .ToListAsync();
            for(int i = 0; i < asFlowList.Count; i++)
            {
                string opName = "";
                string opAccount = asFlowList[i].oper.Trim();
                if (opAccount.ToLower().IndexOf("buguai") >= 0)
                {
                    opName = "舒娟";
                }
                else if (opAccount.ToLower().IndexOf("kou") >= 0)
                {
                    opName = "寇芳";
                }
                else
                {
                    opName = "管理员";
                }
                FinancialStatement fs = new FinancialStatement()
                {
                    id = 0,
                    pay_type = "微信支付",
                    mch_id = mchId.Trim(),
                    season = Util.GetSeason(asFlowList[i].bill_date_time),
                    month = asFlowList[i].bill_date_time.Year.ToString() + "-" + asFlowList[i].bill_date_time.Month.ToString().PadLeft(2, '0'),
                    order_type = asFlowList[i].biz_type,
                    day_of_week = Util.GetDayOfWeek(asFlowList[i].bill_date_time),
                    trans_date = asFlowList[i].bill_date_time,
                    bill_date = asFlowList[i].bill_date_time,
                    platform_order_no = asFlowList[i].biz_no,
                    out_trade_no = "",
                    flow_no = asFlowList[i].flow_no,
                    open_id = "",
                    op_type =  "人工",
                    oper = opName.Trim(),
                    oper_account = asFlowList[i].oper,
                    amount = null,
                    fee = null,
                    settle = null,
                    amount_sum = 0,
                    fee_sum = 0,
                    settle_sum = 0,
                    platform_remain = asFlowList[i].surplus,
                    coming = 0,
                    bank_remain = 0,
                    can_withdraw = 0,
                    withdraw = asFlowList[i].biz_type.Trim().Equals("提现")? asFlowList[i].amount: null,
                    refund_amount = null,
                    refund_fee = null,
                    refund_settle = null,
                    charge = asFlowList[i].biz_type.Trim().Equals("网银充值")? asFlowList[i].amount: null
                };
                await _db.financialStatement.AddAsync(fs);
                await _db.SaveChangesAsync();
                asFlowList[i].statement_id = fs.id;
                _db.wepayFlowBill.Entry(asFlowList[i]).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }



            return Ok(0);
        }
    }
}
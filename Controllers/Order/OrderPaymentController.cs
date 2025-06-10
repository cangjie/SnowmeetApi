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
using SnowmeetApi.Models.Rent;
using System.Text;
using Microsoft.CodeAnalysis.Elfie.Model.Strings;
using SnowmeetApi.Controllers.User;

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

        private readonly RentController _rentHelper;
        private readonly MemberController _memberHelper;
        public string _domain = "";
        //private SkiPassController _skipassHelper;

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

            _domain = _httpContextAccessor.HttpContext.Request.Host.ToString();
            _rentHelper = new RentController(context, config, httpContextAccessor);
            _memberHelper = new MemberController(context, config);
            //_skipassHelper = new SkiPassController(context, config, httpContextAccessor);
            //UnicUser._context = context;

        }



       
        /*
        [NonAction]
        public async Task<OrderPayment> TenpayRefund(int id, double amount, string sessionKey)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(id);


            return payment;
        }
        */

        /*
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
            order.paymentList = paymentsArr.ToList();
            if (order.final_price <= order.paidAmount)
            {
                order.pay_state = 1;
                order.pay_time = DateTime.Now;
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
                    SkiPassController skiPassHelper = new SkiPassController(_context, _originConfig, _httpContextAccessor);
                    await skiPassHelper.CreateSkiPass(order.id);
                    break;
                case "押金":
                    List<RentOrder> rentOrderList = await _context.RentOrder
                        .Where(o => o.order_id == order.id).OrderByDescending(o => o.id).ToListAsync();
                    if (rentOrderList != null && rentOrderList.Count > 0)
                    {
                        RentOrder rentOrder = rentOrderList[0];
                        rentOrder.open_id = order.open_id;
                        _context.Entry(rentOrder).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        await _rentHelper.StartRent(rentOrder.id);
                    }

                    break;
                case "UTV押金":
                    UTVController uCtl = new UTVController(_context, _originConfig, _httpContextAccessor);
                    var utvList = await _context.utvReserve.Where(u => u.order_id == order.id).ToListAsync();
                    if (utvList != null && utvList.Count == 1)
                    {
                        await uCtl.SetReservePaySuccess(utvList[0].id);
                    }
                    break;
                default:
                    break;
            }
            return order;
        }
*/
        [NonAction]
        public int GetMchId(OrderOnline order)
        {
            int mchId = 3;
            if (order.shop.Trim().IndexOf("南山") >= 0)
            {
                switch (order.type.Trim())
                {
                    case "店销现货":
                        mchId = 6;
                        break;
                    case "雪票":
                        mchId = 7;
                        break;
                    case "押金":
                        mchId = 17;
                        break;
                    case "服务":
                        mchId = 15;
                        break;
                    default:
                        mchId = 6;
                        break;

                }
            }
            else if (order.shop.Trim().IndexOf("万龙") >= 0)
            {
                switch (order.type.Trim())
                {
                    case "服务":
                        mchId = 3;
                        break;
                    case "押金":
                        mchId = 5;
                        break;
                    case "店销现货":
                        mchId = 12;
                        break;
                    default:
                        mchId = 12;
                        break;

                }
            }
            else
            {
                switch (order.type.Trim())
                {
                    case "服务":
                        mchId = 8;
                        break;
                    case "押金":
                        mchId = 10;
                        break;
                    case "店销现货":
                        mchId = 9;
                        break;
                    case "雪票":
                        mchId = 11;
                        break;
                    default:
                        mchId = 9;
                        break;

                }
            }


            return mchId;
        }
        
        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderOnline>> GetWholeOrder(int paymentId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);//.GetUnicUser(sessionKey);
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

        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderOnline>> ModPayMethod(int paymentId, string payMethod, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            payMethod = Util.UrlDecode(payMethod.Trim());

            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment != null)
            {
                payment.pay_method = payMethod;
                _context.Entry(payment).State = EntityState.Modified;
                await _context.SaveChangesAsync();

            }
            OrderOnlinesController orderController = new OrderOnlinesController(_context, _originConfig);
            OrderOnline order = (await orderController.GetOrderOnline(payment.order_id, sessionKey)).Value;
            order.pay_method = payMethod;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(order);
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderOnline>> CancelOrder(int orderId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            
           

            OrderOnlinesController orderController = new OrderOnlinesController(_context, _originConfig);

            bool canCancel = true;
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (order == null)
            {
                return NotFound();
            }
            if (!user.isAdmin && !order.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                return BadRequest();
            }


            if (order.pay_state != 0)
            {
                canCancel = false;
            }
            var paymentList = await _context.OrderPayment.Where(p => p.order_id == orderId).ToListAsync();
            for (int i = 0; i < paymentList.Count; i++)
            {
                if (!paymentList[i].status.Trim().Equals("待支付"))
                {
                    canCancel = false;
                    break;
                }
            }

            if (!canCancel)
            {
                OrderOnline orderNew = (await orderController.GetOrderOnline(orderId, sessionKey)).Value;
                return Ok(orderNew);
            }

            order.pay_state = -1;
            _context.Entry(order).State = EntityState.Modified;
            for (int i = 0; i < paymentList.Count; i++)
            {
                paymentList[i].status = "已取消";
                _context.Entry(paymentList[i]).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();
            //OrderOnline orderNew1 = (await orderController.GetOrderOnline(orderId, sessionKey)).Value;
            return Ok((await orderController.GetOrderOnline(orderId, sessionKey)).Value);

        }

        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderPayment>> Pay(int paymentId, string sessionKey)
        {
            
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment == null)
            {
                return NotFound();
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(payment.order_id);
            if (order == null)
            {
                return NotFound();
            }
            //var shareList = await _context.paymentShare.Where(s => s.payment_id == payment.id && s.state == 0)
            //    .AsNoTracking().ToListAsync();

            bool share = false;
            if (order.referee_member_id > 0)
            {
                share = true;
            }
            else
            {
                Member member = await _memberHelper.GetMember(order.open_id.Trim(), "wechat_mini_openid");
                if (member != null)
                {
                    List<Referee> refList = await _context.referee
                        .Where(r => r.member_id == member.id && r.consume_type.Trim().Equals("雪票"))
                        .AsNoTracking().ToListAsync();
                    if (refList != null && refList.Count > 0)
                    {
                        share = true;
                    }
                }

            }

            switch(payment.pay_method.Trim())
            {
                case "微信支付":
                    sessionKey = Util.UrlDecode(sessionKey);            
                    UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
                    payment.open_id = user.miniAppOpenId.Trim();
                    _context.OrderPayment.Entry(payment).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    TenpayController tenpayHelper = new TenpayController(_context, _originConfig, _httpContextAccessor);
                    payment = await tenpayHelper.TenpayRequest(paymentId, sessionKey, share);
                    break;
                case "支付宝":
                    AliController aliHelper = new AliController(_context, _originConfig, _httpContextAccessor);
                    //payment = await aliHelper.CreateOrder(payment.id);
                    payment.ali_qr_code = await aliHelper.GetPaymentQrCodeUrl(payment.id);
                    break;
                default:
                    payment.status = "支付成功";
                    break;

            }

            payment.open_id = "";
            return Ok(payment);
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderPayment>> CreatePayment(int orderId, string payMethod, double amount = 0)
        {
            payMethod = Util.UrlDecode(payMethod).Trim();
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (order == null)
            {
                return NotFound();
            }
            if (amount == 0)
            {
                amount = order.final_price;
            }
            if (amount > order.final_price)
            {
                return BadRequest();
            }
            bool find = false;
            var paymentList = await _context.OrderPayment.Where(p => p.order_id == orderId).ToListAsync();
            OrderPayment payment = new OrderPayment();
            for (int i = 0; paymentList != null && i < paymentList.Count; i++)
            {
                payment = paymentList[i];
                if (Math.Round(payment.amount * 100, 0) == Math.Round(amount * 100, 0) && payment.status.Trim().Equals("待支付"))
                {
                    payment.pay_method = payMethod.Trim();
                    find = true;
                    _context.Entry(payment).State = EntityState.Modified;
                    
                    break;
                }
            }
            if (!find)
            {
                payment.order_id = orderId;
                payment.pay_method = payMethod.Trim();
                payment.amount = amount;
                payment.status = "待支付";
                payment.out_trade_no = await GetOutTradeNo(payment.order_id);
                await _context.OrderPayment.AddAsync(payment);
            }
            else if (payment.out_trade_no.Trim().Equals(""))
            {
                payment.out_trade_no = await GetOutTradeNo(payment.order_id);
                _context.OrderPayment.Entry(payment).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            return Ok(payment);
        }


        [NonAction]
        public async Task<string> GetOutTradeNo(int orderId)
        {
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (order == null)
            {
                return "";
            }
            
            var shops = await _context.shop.Where(s=>s.name.Trim().Equals(order.shop.Trim()))
                .AsNoTracking().ToListAsync();
            if (shops == null || shops.Count == 0)
            {
                return "";
            }
            string shopCode = shops[0].code.Trim();
            string bizCode = "";
            switch(order.type.Trim())
            {
                case "雪票":
                    bizCode = "XP";
                    break;
                case "店销现货":
                    bizCode = "XS";
                    break;
                case "押金":
                    bizCode = "ZL";
                    break;
                case "服务":
                    bizCode = "YH";
                    break;
                default:
                    bizCode = "QT";    
                    break;            

            }
            if (shopCode.Equals("WF") && bizCode.Equals("ZL"))
            {
                shopCode = "WT";
            }
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            var payments = await _context.OrderPayment.Where(o => o.order_id == orderId)
                .AsNoTracking().ToListAsync();
            if (payments == null && payments.Count >= 100)
            {
                return "";
            }
            string outTradeNo = shopCode + "_" + bizCode + "_" + dateStr + "_" + order.id.ToString().PadLeft(6, '0')  + "_ZF_" + (payments.Count + 1).ToString().PadLeft(2,'0');
            var paymentDepList = await _context.OrderPayment.Where(p=>p.out_trade_no.Trim().Equals(outTradeNo))
                .AsNoTracking().ToListAsync();
            if (!_domain.Trim().Equals("mini.snowmeet.top"))
            {
                outTradeNo = "TEST_" + outTradeNo.Trim();
            }
            if (paymentDepList == null || paymentDepList.Count == 0)
            {
                return outTradeNo;
            }
            return "";
        }

        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderPaymentRefund>> Refund(int paymentId, double amount, 
            string reason, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            reason = Util.UrlDecode(reason);
            sessionKey = Util.UrlDecode(sessionKey);

            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);


            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);        

            if (member.is_manager == 0 && member.is_admin == 0 && member.is_staff == 0
                && !payment.open_id.Trim().Equals(member.wechatMiniOpenId.Trim()))
            {
                return BadRequest();
            }
            if (!payment.status.Equals("支付成功"))
            {
                return BadRequest();
            }
            var refunds = await _context.OrderPaymentRefund
                .Where(r => r.payment_id == paymentId && ((!r.refund_id.Trim().Equals("") && r.refund_id != null ) || r.state == 1))
                .AsNoTracking().ToListAsync();
            double totalRefundAmount = 0;
            for(int i = 0; refunds != null && i < refunds.Count; i++)
            {
                totalRefundAmount += refunds[i].amount;
            }
            if (Math.Round(amount, 2) > Math.Round(payment.amount - totalRefundAmount, 2))
            {
                return BadRequest();
            }
            string outRefundNo = payment.out_trade_no + "_TK_" + (refunds.Count + 1).ToString().PadLeft(2, '0') 
                + "_" + DateTime.Now.ToString("yyyyMMdd");
            OrderPaymentRefund refund = new OrderPaymentRefund()
            {
                order_id = payment.order_id,
                payment_id = paymentId,
                reason = reason,
                refund_id = "",
                state = 0,
                amount = amount,
                TransactionId = "",
                RefundFee = 0,
                oper = member.wechatMiniOpenId,
                memo = "",
                notify_url = "",
                out_refund_no = outRefundNo.Trim()
            };
            await _context.OrderPaymentRefund.AddAsync(refund);
            await _context.SaveChangesAsync();

            switch(payment.pay_method.Trim())
            {
                case "微信支付":
                    TenpayController tenpayHelper = new TenpayController(_context, _originConfig, _httpContextAccessor);
                    refund = await tenpayHelper.Refund(refund.id);
                    break;
                /*
                case "支付宝":
                    AliController aliHelper = new AliController(_context, _originConfig, _httpContextAccessor);
                    refund = await aliHelper.Refund(refund.id);
                    break;
                */
                default:
                    refund.state = 1;
                    _context.OrderPaymentRefund.Entry(refund).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    break;
            }
            //refund.oper = "";
            return Ok(refund);

        }


        [HttpGet]
        public async Task SetPaymentRefundSuccess(int paymentId)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment == null)
            {
                return;
            } 
            OrderOnline order = await _context.OrderOnlines.FindAsync(payment.order_id);
            if (order == null)
            {
                return;
            }
            
            
            if (order.type.Trim().Equals("雪票"))
            {
                SkiPassController _skipassHelper = new SkiPassController(_context, _originConfig, _httpContextAccessor);
                await _skipassHelper.RefundCallBack(paymentId);
            }
            
        }






        /// <summary>
        /// ///////////////////////////////////
        /// </summary>
        /// <param name="mchid"></param>
        /// <param name="timeStamp"></param>
        /// <param name="nonce"></param>
        /// <param name="paySign"></param>
        /// <param name="serial"></param>
        /// <returns></returns>
        //Ready to remove

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
                        //await SetTenpayPaymentSuccess(outTradeNumber);

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

        [HttpGet("{id}")]
        public async Task<ActionResult<TenpaySet>> TenpayRequest(int id, string sessionKey)
        {



            
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
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
            else if (order.type.Trim().StartsWith("服务"))
            {
                desc = "养护";
            }
            else
            {
                desc = "租赁";
            }

            CreatePayTransactionJsapiRequest.Types.Detail dtl = new CreatePayTransactionJsapiRequest.Types.Detail();

            CreatePayTransactionJsapiRequest.Types.Detail.Types.GoodsDetail goodDtl
                = new CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail();
            goodDtl.GoodsName = "测试商品明细1";
            goodDtl.Quantity = 1;
            goodDtl.UnitPrice = 10000;
            goodDtl.MerchantGoodsId = "unknown";

            dtl.GoodsList = new List<CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail>();
            dtl.GoodsList.Add(goodDtl);

            goodDtl = new CreatePayTransactionAppRequest.Types.Detail.Types.GoodsDetail();
            goodDtl.GoodsName = "测试商品明细2";
            goodDtl.Quantity = 2;
            goodDtl.UnitPrice = 20000;
            goodDtl.MerchantGoodsId = "unknown";

            dtl.GoodsList.Add(goodDtl);

            //goodDtl

            if (order.type.Trim().Equals("服务"))
            {
                string name = "";
                var details = await _context.OrderOnlineDetails
                    .Where(d => d.OrderOnlineId == order.id).ToListAsync();
                for (int i = 0; i < details.Count; i++)
                {
                    SnowmeetApi.Models.Product.Product p = await _context.Product.FindAsync(details[i].product_id);
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
                },
                GoodsTag = "testing goods tag",
                Detail = dtl
                
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
        public async Task<PaymentShare> CreateShare(int paymentId, double amount, string memo, int kolId)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            
            var shareList = await _context.paymentShare.Where(s => s.payment_id == paymentId).AsNoTracking().ToListAsync();
            int shareCount = 0;
            if (shareList != null)
            {
                shareCount = shareList.Count;
            }
            string outTradeNo = payment.out_trade_no.Trim() + "_SHARE_" + (shareCount + 1).ToString().PadLeft(2, '0') 
                + "_" + DateTime.Now.ToString("yyyyMMdd") + "_"+ kolId.ToString().PadLeft(3, '0');// + "_";
            //outTradeNo = outTradeNo + shareCount.ToString().PadLeft(2, '0');
            int orderId = payment.order_id;
            PaymentShare share = new PaymentShare()
            {
                id = 0,
                order_id = orderId,
                payment_id = payment.id,
                kol_id = kolId,
                amount = amount,
                memo = memo,
                out_trade_no = outTradeNo.Trim()
            };
            await _context.paymentShare.AddAsync(share);
            await _context.SaveChangesAsync();
            return share;
        }

        [HttpGet]
        public async Task<PaymentShare> SubmitShare(int shareId)
        {
            PaymentShare share = await _context.paymentShare.FindAsync(shareId);
            if (share == null)
            {
                return null;
            }
            OrderPayment payment = await _context.OrderPayment.FindAsync(share.payment_id);
            //PaymentShare share = new PaymentShare();
            switch(payment.pay_method.Trim())
            {
                case "支付宝":
                    AliController aliHelper = new AliController(_context, _originConfig, _httpContextAccessor);
                    share = await aliHelper.Share(shareId);
                    break;
                case "微信支付":
                    TenpayController tenHelper = new TenpayController(_context, _originConfig, _httpContextAccessor);
                    share = await tenHelper.Share(shareId);
                    break;
                default:
                    break;
            }
            return share;
        }
        [NonAction]
        public async Task ShareFinish(int paymentId, string description)
        {
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment == null)
            {
                return;
            }
            switch(payment.pay_method)
            {
                case "微信支付":
                    TenpayController _tenHelper = new TenpayController(_context, _originConfig, _httpContextAccessor);
                    await _tenHelper.ShareFinish(paymentId, description);
                    break;
                default:
                    break;
            }
        }

       
        private bool OrderPaymentExists(int id)
        {
            return _context.OrderPayment.Any(e => e.id == id);
        }
    }
}

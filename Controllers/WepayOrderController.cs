using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using wechat_miniapp_base.Models;
using Microsoft.Extensions.Configuration;
using System.IO;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Utilities;
using HttpHandlerDemo;
using System.Net.Http;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;
using System.Text;
using System.Net.Http.Headers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Ticket;
using SnowmeetApi.Models.Card;
using SnowmeetApi.Models.Order;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using System.Reflection.PortableExecutable;
using System.Threading;
using Newtonsoft.Json.Converters;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class WepayOrderController : ControllerBase
    {

        public class Resource
        {
            public string original_type { get; set; }
            public string algorithm { get; set; }
            public string ciphertext { get; set; }
            public string associated_data { get; set; }
            public string nonce { get; set; }
        }

        public class CallBackStruct
        {
            public string id { get; set; }
            public DateTimeOffset create_time { get; set; }
            public string resource_type { get; set; }
            public string event_type { get; set; }
            public string summary { get; set; }
            public Resource resource { get; set; }
        }

        public class Cipher
        {
            public string algorithm { get; set; }
            public string associated_data { get; set; }
            public string ciphertext { get; set; }
        }

        public class CerStruct
        {
            public DateTimeOffset effective_time { get; set; }
            public Cipher encrypt_certificate { get; set; }
            public DateTimeOffset expire_time { get; set; }
            public string serial_no { get; set; }

        }

        public class CerList
        {
            public CerStruct[] data { get; set; }
        }

        

        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        private IConfiguration _originConfig;

        public string _appId = "";

        private readonly IHttpContextAccessor _httpContextAccessor;

        public WepayOrderController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            _originConfig = config;
        }

       

        [HttpGet("{outTradeNo}")]
        public async Task<ActionResult<string>> Refund(string outTradeNo, int amount, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (user == null || !user.isAdmin)
            {
                return NotFound();
            }
            string operName = "管理员申请退款";
            
            WepayOrder wepayOrder = _context.WepayOrders.Find(outTradeNo);
            if (wepayOrder == null)
            {
                return NotFound();
            }
            int parseResult = 0;
            var wepayOrderRefundArr =
                _context.WePayOrderRefund.Where(r => r.wepay_out_trade_no == outTradeNo).ToList<WepayOrderRefund>();
            int totalRefundAmount = 0;
            for (int i = 0; i < wepayOrderRefundArr.Count; i++)
            {
                WepayOrderRefund currentRefund = wepayOrderRefundArr[i];
                if (int.TryParse(currentRefund.status, out parseResult))
                    totalRefundAmount = wepayOrderRefundArr[i].amount + totalRefundAmount;
            }
            if (totalRefundAmount + amount <= wepayOrder.amount)
            {
                WepayOrderRefund refund = new WepayOrderRefund();
                refund.amount = amount;
                refund.oper_open_id = user.miniAppOpenId.Trim();
                refund.status = "";
                refund.wepay_out_trade_no = wepayOrder.out_trade_no.Trim();
                _context.WePayOrderRefund.Add(refund);
                _context.SaveChanges();

                int mchid = wepayOrder.mch_id;
                WepayKey key =  _context.WepayKeys.Find(mchid);
                if (key == null)
                {
                    return NotFound();
                }
                var certManager = new InMemoryCertificateManager();
                var options = new WechatTenpayClientOptions()
                {
                    MerchantId = key.mch_id.Trim(),
                    MerchantV3Secret = "",
                    MerchantCertificateSerialNumber = key.key_serial.Trim(),
                    MerchantCertificatePrivateKey = key.private_key.Trim(),
                    PlatformCertificateManager = certManager
                };
                string refundTransId = refund.wepay_out_trade_no + refund.id.ToString().PadLeft(2, '0');
                var client = new WechatTenpayClient(options);
                var request = new CreateRefundDomesticRefundRequest()
                {
                    OutTradeNumber = wepayOrder.out_trade_no.Trim(),
                    OutRefundNumber = refundTransId.Trim(),
                    Amount = new CreateRefundDomesticRefundRequest.Types.Amount()
                    {
                        Total = wepayOrder.amount,
                        Refund = amount
                    },
                    Reason = operName,
                    NotifyUrl = wepayOrder.notify.Replace("PaymentCallback", "RefundCallback")
                };
                var response = await client.ExecuteCreateRefundDomesticRefundAsync(request);
                try
                {
                    string refundId = response.RefundId.Trim();
                    if (refundId == null || refundId.Trim().Equals(""))
                    {
                        return NotFound();
                    }
                    refund.status = refundId;
                    _context.Entry<WepayOrderRefund>(refund).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    await Response.WriteAsync("SUCCESS");
                }
                catch
                {
                    refund.status = response.ErrorMessage.Trim();
                    _context.Entry<WepayOrderRefund>(refund).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    return NotFound();
                }
                


            }
            return NotFound();
        }
        
        [HttpPost]
        public ActionResult<string> RefundCallback([FromBody] object postData)
        {
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
                fw.WriteLine(DateTimeOffset.Now.ToString());
                
                fw.WriteLine(postData.ToString());
                fw.WriteLine("--------------------------------------------------------");
                fw.WriteLine("");
                fw.Close();
            }
            return "{ \r\n \"code\": \"SUCCESS\", \r\n \"message\": \"成功\" \r\n}";
        }
        
        
        [HttpPost("{mchid}")]
        public ActionResult<string> PaymentCallback(int mchid, CallBackStruct postData)
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
                using (StreamReader sr = new StreamReader(path + serial.Trim() +".pem", true))
                {
                    cerStr = sr.ReadToEnd();
                    sr.Close();
                }

                

                var certManager = new InMemoryCertificateManager();
                //CertificateEntry ce = new CertificateEntry(serial, cerStr, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

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
                        SetWepayOrderSuccess(outTradeNumber);

                        //Console.WriteLine("订单 {0} 已完成支付，交易单号为 {1}", outTradeNumber, transactionId);
                    }
                }

            }
            catch(Exception err)
            {
                Console.WriteLine(err.ToString());
            }
            return "{ \r\n \"code\": \"SUCCESS\", \r\n \"message\": \"成功\" \r\n}";
        }
        
        
        [HttpGet]
        public ActionResult<string> Test(string outTradeNo)
        {
            SetWepayOrderSuccess(outTradeNo);
            return "";
        }
        
        private void SetWepayOrderSuccess(string outTradeNo)
        {
            WepayOrder wePayOrder = _context.WepayOrders.Find(outTradeNo);
            if (wePayOrder != null)
            {
                try
                {
                    OrderOnline orderOnline = _context.OrderOnlines.Find(wePayOrder.order_id);
                    orderOnline.pay_state = 1;
                    orderOnline.pay_time = DateTime.Now;
                    wePayOrder.state = 2;
                    _context.Entry<OrderOnline>(orderOnline).State = EntityState.Modified;
                    _context.Entry<WepayOrder>(wePayOrder).State = EntityState.Modified;
                    _context.SaveChanges();
                    try
                    {
                        if (orderOnline.ticket_code != null && !orderOnline.ticket_code.Trim().Equals(""))
                        {
                            Ticket ticket = _context.Ticket.Find(orderOnline.ticket_code.Trim());
                            ticket.used = 1;
                            ticket.used_time = DateTime.Now;
                            _context.Entry<Ticket>(ticket).State = EntityState.Modified;
                            _context.SaveChanges();

                        }
                    }
                    catch
                    {

                    }

                    try
                    {
                        if (orderOnline.type.Trim().Equals("雪票"))
                        {
                            CardController cardController = new CardController(_context, _originConfig);
                            string code = cardController.CreateCard("雪票");
                            if (!code.Trim().Equals(""))
                            {
                                orderOnline.code = code; 
                            }
                            _context.Entry<OrderOnline>(orderOnline).State = EntityState.Modified;
                            _context.SaveChanges();
                        }
                        if (orderOnline.type.Trim().Equals("服务卡"))
                        {
                            List<OrderOnlineDetail> detailList = _context.OrderOnlineDetails.Where(d => d.OrderOnlineId == orderOnline.id).ToList();
                            int productId = 0;
                            foreach (var d in detailList)
                            {

                                if (((OrderOnlineDetail)d).product_id == 144 || ((OrderOnlineDetail)d).product_id == 145)
                                {
                                    productId = ((OrderOnlineDetail)d).product_id;
                                    break;
                                }
                            }
                            if (productId == 144 || productId == 145)
                            {
                                CardController cardController = new CardController(_context, _originConfig);
                                string code = cardController.CreateCard(orderOnline.type.Trim());
                                Card card = _context.Card.Find(code);
                                card.product_id = productId;
                                card.is_package = 0;
                                card.is_ticket = 0;
                                card.owner_open_id = orderOnline.open_id.Trim();
                                card.use_memo = "";
                                _context.Entry<Card>(card).State = EntityState.Modified;
                                _context.SaveChanges();

                                var summerList = _context.SummerMaintain.Where(s => s.order_id == orderOnline.id).ToList();
                                if (summerList.Count > 0)
                                {
                                    SummerMaintain sm = (SummerMaintain)summerList[0];
                                    sm.code = code.Trim();
                                    if (sm.send_item.Trim().Equals("现场交付"))
                                    {
                                        sm.state = "养护中";
                                    }
                                    else
                                    {
                                        sm.state = "未填快递单号";
                                    }
                                    
                                    _context.Entry<SummerMaintain>(sm).State = EntityState.Modified;
                                    _context.SaveChanges();
                                }

                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                    //if (orderOnline.ticket_code!=null && orderOnline.ticket_code.Trim().Equals(""))

                }
                catch
                {

                }
            }
        }

        private struct DownloadUrl
        {
            public string download_url { get; set; }
        }

        [HttpGet("{mchId}")]
        public async Task  RequestTradeBill(int mchId, DateTime billDate)
        {
            var summaryList = await _context.wepaySummary.Where(s => s.trans_date.Date == billDate.Date && s.mch_id == mchId)
                .AsNoTracking().ToListAsync();
            if (summaryList != null && summaryList.Count > 0)
            {
                return;
            }

            WepayKey k = await _context.WepayKeys.FindAsync(mchId);

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
                await _context.wepaySummary.AddAsync(summary);
                await _context.SaveChangesAsync();
                if (summary.id > 0)
                {
                    try
                    {
                        for (int i = 0; i < balanceArr.Length; i++)
                        {
                            balanceArr[i].summary_id = summary.id;
                            await _context.wepayBalance.AddAsync(balanceArr[i]);
                        }
                        await _context.SaveChangesAsync();
                    }
                    catch
                    {
                        _context.wepaySummary.Remove(summary);
                        await _context.SaveChangesAsync();
                    }
                }

            }

            
        }
       
    }
}

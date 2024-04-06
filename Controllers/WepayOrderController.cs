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
using SnowmeetApi.Models.Rent;

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

        /*
        [HttpGet]
        public async Task DownloadCurrentSeason()
        {
            int[] mchId = new int[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 15, 17 };

            for (DateTime i = DateTime.Now.Date.AddDays(-11); i <= DateTime.Now.Date.AddDays(-1); i = i.AddDays(1))
            {
                for (int j = 0; j < mchId.Length; j++)
                {
                    await RequestFlowBill(mchId[j], i);
                }
            }
        }
        */

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





        [HttpGet("{mchId}")]
        public async Task  RequestTradeBill(int mchId, DateTime billDate)
        {
            Console.WriteLine(mchId.ToString() + "\t" + billDate.ToShortDateString());
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

        [HttpGet]
        public async Task RequestFlowBill(int mchId, DateTime billDate)
        {
            WepayKey k = await _context.WepayKeys.FindAsync(mchId);

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
                    await _context.wepayFlowBill.AddAsync(bill);
                }
                catch
                {

                }
            }
            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {

            }
        }


        [HttpGet]
        public async Task<ActionResult<WepayReport>> GetWepayBalance(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (user == null || !user.isAdmin)
            {
                return NotFound();
            }

            var paidArr = await _context.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date
                && b.trans_date.Date <= endDate.Date && b.pay_status.Trim().Equals("SUCCESS"))
                .OrderByDescending(b => b.trans_date).AsNoTracking().ToListAsync();
            
            var wepayKeyList = await _context.WepayKeys.ToListAsync();

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

                var pList = await _context.OrderPayment.Where(p => p.status.Equals("支付成功")
                    && p.out_trade_no.Trim().Equals(b.out_trade_no.Trim())).ToListAsync();
                if (pList != null && pList.Count > 0)
                {
                    orderOnlineId = pList[0].order_id;
                }

                OrderOnline? orderOnline = await _context.OrderOnlines.FindAsync(orderOnlineId);
                if (orderOnline != null)
                {
                    shop = orderOnline.shop.Trim();
                    switch (orderOnline.type.Trim())
                    {
                        case "服务":
                            orderType = "养护";
                            var maintailList = await _context.MaintainLives.Where(m => m.order_id == orderOnlineId)
                                .AsNoTracking().ToListAsync();
                            for (int j = 0; j < maintailList.Count; j++)
                            {
                                orderId = orderId + (j == 0 ? "" : ",") + maintailList[j].task_flow_num.Trim();
                            }
                            break;
                        case "押金":
                            orderType = "租赁";
                            var rentOrderList = await _context.RentOrder.Where(r => r.order_id == orderOnlineId)
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
                            var skiPassList = await _context.OrderOnlineDetails
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
                    MiniAppUser mUser = await _context.MiniAppUsers.FindAsync(orderOnline.open_id);
                    if (mUser != null)
                    {
                        cell = mUser.cell_number.Trim();
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
                b.refunds = await _context.wepayBalance.Where(c =>  c.pay_status.Trim().Equals("REFUND")
                    && c.out_trade_no.Trim().Equals(b.out_trade_no)
                    && c.refund_status.Trim().Equals("SUCCESS"))
                    .OrderBy(b => b.id).AsNoTracking().ToListAsync();
                maxLen = Math.Max(maxLen, b.refunds.Count);
                for (int j = 0; j < b.refunds.Count; j++)
                {
                    b.totalRefundAmount += b.refunds[j].refund_amount;
                }
                b.netAmount = b.settle_amount - b.totalRefundAmount;
                b.shop = shop;
                b.orderType = orderType;
                b.real_name = realName;
                b.cell = cell;
                b.gender = gender;
                b.mchName = mchName;
                b.dayOfWeek = Util.GetDayOfWeek(b.trans_date.Date);
                retList.Add(b);

            }


            WepayReport report = new WepayReport();
            report.maxRefundLength = maxLen;
            report.items = retList;

            return Ok(report);
        }

        [HttpGet]
        public async Task<ActionResult<EPaymentDailyReport>> GetReport(DateTime reportDate, int mchId)
        {
            return Ok(await CreateEPaymentDailyReport(reportDate, mchId.ToString()));
        }

        [HttpGet]
        public async Task<ActionResult<int>> CreateAllReport()
        {
            int i = 0;
            int[] mchId = new int[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 15, 17 };
            for (DateTime currentDate = DateTime.Parse("2023-12-27"); currentDate < DateTime.Now.AddDays(-1).Date; currentDate = currentDate.AddDays(1))
            {
                for (int j = 0; j < mchId.Length; j++)
                {
                    EPaymentDailyReport report = await CreateEPaymentDailyReport(currentDate.Date, mchId[j].ToString());
                    await SaveReport(report);
                    i++;
                }
            }
            return Ok(i);
        }

        [NonAction]
        public async Task SaveReport(EPaymentDailyReport report)
        {
            EPaymentDailyReport? oriReport = await _context.ePaymentDailyReport.FindAsync(report.biz_date, report.mch_id, report.pay_method);
            if (oriReport != null)
            {
                _context.ePaymentDailyReport.Remove((EPaymentDailyReport)report);
                await _context.SaveChangesAsync();
            }
            await _context.ePaymentDailyReport.AddAsync(report);
            await _context.SaveChangesAsync();
        }


        [NonAction]
        public async Task<EPaymentDailyReport> CreateEPaymentDailyReport(DateTime reportDate, string mchId, string payMehtod = "微信支付")
        {
            reportDate = reportDate.Date;
            EPaymentDailyReport report = new EPaymentDailyReport();
            payMehtod = payMehtod.Trim();
            report.pay_method = payMehtod;
            report.biz_date = reportDate.Date;
            if (payMehtod.Trim().Equals("微信支付"))
            {
                WepayKey key = await _context.WepayKeys.FindAsync(int.Parse(mchId));
                mchId = key.mch_id.Trim();
                report.mch_id = mchId.Trim();

                //get last surplus
                var flowList = await _context.wepayFlowBill.Where(b => b.bill_date_time.Date < reportDate.Date
                    && b.mch_id.Trim().Equals(mchId))
                    .OrderByDescending(b => b.id).Take(1).ToListAsync();

                
                if (flowList == null || flowList.Count == 0)
                {
                    report.last_surplus = 0;
                }
                else
                {
                    report.last_surplus = flowList[0].surplus;
                }

                //get current surplus
                flowList = await _context.wepayFlowBill.Where(b => b.bill_date_time.Date == reportDate.Date
                    && b.mch_id.Trim().Equals(mchId))
                    .OrderByDescending(b => b.id).Take(1).ToListAsync();
                if (flowList == null || flowList.Count == 0)
                {
                    report.current_surplus = report.last_surplus;
                }
                else
                {
                    report.current_surplus = flowList[0].surplus;
                }

                //计算提现金额
                flowList = await _context.wepayFlowBill.Where(b => b.mch_id.Trim().Equals(mchId)
                    && b.bill_date_time.Date == reportDate.Date
                    && b.biz_name.Trim().Equals("充值/提现") && b.biz_type.Trim().Equals("提现")
                    && b.bill_type.Trim().Equals("支出")).ToListAsync();
                report.withdraw = 0;
                for (int i = 0; flowList != null && i < flowList.Count; i++)
                {
                    report.withdraw += flowList[i].amount;
                }
                
                //收款
                var balanceList = await _context.wepayBalance.Where(b => (b.mch_id.Trim().Equals(mchId)
                    && b.pay_status.Trim().Equals("SUCCESS") && b.trans_date.Date == reportDate.Date)).ToListAsync();
                report.biz_count = 0;
                report.order_amount = 0;
                report.order_fee = 0;
                for (int i = 0; balanceList != null && i < balanceList.Count; i++)
                {
                    report.biz_count++;
                    report.order_amount += balanceList[i].order_amount;
                    report.order_fee += balanceList[i].fee;
                    
                }
                report.order_balance = report.order_amount - report.order_fee;

                //退款
                balanceList = await _context.wepayBalance.Where(b => (b.mch_id.Trim().Equals(mchId)
                    && b.pay_status.Trim().Equals("REFUND") && b.refund_status.Trim().Equals("SUCCESS")
                    && b.trans_date.Date == reportDate.Date)).ToListAsync();
                report.refund_count = 0;
                report.refund_amount = 0;
                report.refund_fee = 0;

                for (int i = 0; balanceList != null && i < balanceList.Count; i++)
                {
                    report.refund_count++;
                    report.refund_amount += balanceList[i].refund_amount;
                    report.refund_fee += -1 * balanceList[i].fee;
                }
                report.refund_balance = report.refund_amount - report.refund_fee;

                RentController rentHelper = new RentController(_context, _originConfig, _httpContextAccessor);
                Models.Rent.RentOrderCollection rc =
                    (Models.Rent.RentOrderCollection)
                    ((OkObjectResult)((await rentHelper.GetUnSettledOrderBefore(report.biz_date.Date, "SystemInvoke")).Result)).Value;

                double totalDeposit = 0;
                double totalReceiveable = 0;
                
                for (int i = 0; i < rc.orders.Length; i++)
                {
                    double deposit = 0;
                    double rental = 0;
                    RentOrder rOrder = rc.orders[i];
                    for (int j = 0; j < rOrder.order.payments.Length; j++)
                    {
                        OrderPayment p = rOrder.order.payments[j];
                        if (p.pay_method.Trim().Equals("微信支付") && p.status.Trim().Equals("支付成功")
                            && p.mch_id == key.id)
                        {
                            deposit += rOrder.order.paidAmount;
                            break;
                        }
                    }
                    
                    totalDeposit += deposit;
                    totalReceiveable += rental;
                }

                double totalRentalToday = 0;
                double totalRentalBefore = 0;
                RentOrderCollection sameDaySettledOrder = (RentOrderCollection)
                    ((OkObjectResult)
                    (await rentHelper.GetCurrentSameDaySettled(report.biz_date.Date, "SystemInvoke")).Result).Value;
                for (int i = 0; i < sameDaySettledOrder.orders.Length; i++)
                {
                    for (int j = 0; j < sameDaySettledOrder.orders[i].order.payments.Length; j++)
                    {
                        OrderPayment p = sameDaySettledOrder.orders[i].order.payments[j];
                        if (p.pay_method.Trim().Equals("微信支付") && p.status.Trim().Equals("支付成功")
                            && p.mch_id == key.id)
                        {
                            double refund = 0;
                            for (int k = 0; k < sameDaySettledOrder.orders[i].order.refunds.Length; k++)
                            {
                                OrderPaymentRefund refundObj = sameDaySettledOrder.orders[i].order.refunds[k];
                                if (refundObj.state == 1 || !refundObj.refund_id.Trim().Equals(""))
                                {
                                    refund += refundObj.amount;
                                }
                            }


                            totalRentalToday += (sameDaySettledOrder.orders[i].order.paidAmount - refund);
                            break;
                        }
                    }
                }

                RentOrderCollection diffDaySettledOrder = (RentOrderCollection)
                    ((OkObjectResult)
                    (await rentHelper.GetCurrentDaySettledPlacedBefore(report.biz_date.Date, "SystemInvoke")).Result).Value;

                for (int i = 0; i < diffDaySettledOrder.orders.Length; i++)
                {
                    for (int j = 0; j < diffDaySettledOrder.orders[i].order.payments.Length; j++)
                    {
                        OrderPayment p = diffDaySettledOrder.orders[i].order.payments[j];
                        if (p.pay_method.Trim().Equals("微信支付") && p.status.Trim().Equals("支付成功")
                            && p.mch_id == key.id)
                        {
                            double refund = 0;
                            for (int k = 0; k < diffDaySettledOrder.orders[i].order.refunds.Length; k++)
                            {
                                OrderPaymentRefund refundObj = diffDaySettledOrder.orders[i].order.refunds[k];
                                if (refundObj.state == 1 || !refundObj.refund_id.Trim().Equals(""))
                                {
                                    refund += refundObj.amount;
                                }
                            }
                            totalRentalBefore += (diffDaySettledOrder.orders[i].order.paidAmount - refund);
                            break;
                        }
                    }
                }

                OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _originConfig);
                var commonOrderList = await _context.OrderOnlines
                    .Where(o => (o.pay_state == 1 && o.pay_time != null
                    && ((DateTime)o.pay_time).Date == report.biz_date.Date
                    && (o.type.Trim().Equals("店销现货") || o.type.Trim().Equals("服务"))))
                    .AsNoTracking().ToListAsync();
                double sale = 0;
                double maintain = 0;
                for (int i = 0; commonOrderList != null && i < commonOrderList.Count; i++)
                {
                    OrderOnline order = (OrderOnline)((OkObjectResult)
                        (await orderHelper.GetWholeOrderByStaff(commonOrderList[i].id, "SystemInvoke")).Result).Value;
                    for (int j = 0; j < order.payments.Length; j++)
                    {
                        OrderPayment p = order.payments[j];
                        if (p.pay_method.Trim().Equals("微信支付") && p.mch_id == key.id
                            && p.status.Trim().Equals("支付成功"))
                        {
                            switch (order.type.Trim())
                            {
                                case "店销现货":
                                    sale += order.paidAmount;
                                    break;
                                case "服务":
                                    maintain += order.paidAmount;
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    
                }

                report.last_deposit = totalDeposit;
                report.last_receiveable = totalReceiveable;
                report.rental = totalRentalBefore + totalRentalToday;
                report.sale = sale;
                report.maintain = maintain;

            }
            else
            {
                return null;
            }

            report.computed_surplus = report.last_surplus + report.order_amount - report.order_fee - report.refund_amount + report.refund_fee - report.withdraw;
            if (Math.Round(report.computed_surplus, 2) != Math.Round(report.current_surplus, 2))
            {
                report.isCorrect = false;
            }
            else
            {
                report.isCorrect = true;
            }

            return report;
        }
       

    }
}

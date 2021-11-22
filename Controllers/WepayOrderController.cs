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
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;

namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
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

        public string _appId = "";

        private readonly IHttpContextAccessor _httpContextAccessor;

        public WepayOrderController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }

        
        protected async Task<ActionResult<CerList>> GetCer()
        {
            WepayKey key = _context.WepayKeys.Find(1);
            HttpClient client = new HttpClient(new HttpHandler(key.mch_id, key.key_serial, key.private_key));
            var response = await client.GetAsync("https://api.mch.weixin.qq.com/v3/certificates");

            HttpRequestHeaders h = response.RequestMessage.Headers;

            string authStr = h.Authorization.Parameter.Trim();
            //mchid=\"1604184933\",nonce_str=\"b4235bhc.nhv\",timestamp=\"1634307680\",serial_no=\"4FD9EF150B9F31A8A2D4CB3251BCE189A65BDADF\",signature=\"RQOyiQyGaGP/wWiidPoyI0sXxgDtgUk8BAfi0z1JjREI4FTsbT6HJQtN1RfYeB4vVRShKXeSBu/vvDTdY5GVNpfopfKDVPsg7YhneCCNywAgLtQk2/iratzbvPrGgTzdma/HgCyXwqyv6oEoYyjdN26DKlB/7OhNqxfyYTQ5wj4oHxAtHKAfAHyUKoCBIWBPVN0Q2kilyYY7be9ucks6DQuH+xeN7zkd3OSj5Ltq42uX0kGFjd7K15I7uokJWisnDXeNAKnp/jTNYEWbuihE9MAuwdEYA1wFm8s5zf4gQlry++G+oP+hYR4sgv83HKvpSOM9u6F8+FOeElcaZ3q7Ow==\""


            authStr = authStr.Substring(authStr.IndexOf("nonce_str"), authStr.Length - authStr.IndexOf("nonce_str"));

            authStr = authStr.Substring(0, authStr.IndexOf(","));

            string nonce = authStr.Replace("nonce_str=", "").Replace("\"", "");

            Stream s = response.Content.ReadAsStream();
            StreamReader sr = new StreamReader(s);
            string cerStr = sr.ReadToEnd();
            sr.Close();
            s.Close();
            
            CerList cList = Newtonsoft.Json.JsonConvert.DeserializeObject<CerList>(cerStr);
            var certManager = new InMemoryCertificateManager();
            certManager.SetCertificate("7F5ACDBE4382FD3F831184B33FAA6E1D35BEE383", cList.data[0].encrypt_certificate.ciphertext);
                            
            var options = new WechatTenpayClientOptions()
            {
                CertificateManager = certManager,
                SignAlgorithm = cList.data[0].encrypt_certificate.algorithm
            };
            var verifyClient = new WechatTenpayClient(options);
            bool valid = verifyClient.VerifyEventSignature(
                callbackTimestamp: "1634265418",
                callbackNonce: "r3lQS3Zc1j87PXlsRwKTRu0DmUvrUQxe",
                callbackBody: "{\"id\":\"74b7445b-e1c6-56cd-9dcf-f9de8d4400e2\",\"create_time\":\"2021-10-15T10:36:58+08:00\",\"resource_type\":\"encrypt-resource\",\"event_type\":\"TRANSACTION.SUCCESS\",\"summary\":\"支付成功\",\"resource\":{\"original_type\":\"transaction\",\"algorithm\":\"AEAD_AES_256_GCM\",\"ciphertext\":\"5njznsCzxzmaQfgoTDIpfoOAcbJ2FtSOOKVi9zJJ1qTR9WNZo1FQDSzX0DL10JTXq+sUoQkk+xuK9Cj2pQIOVj8yWy2PoIU9Nsqt8Pp5c+APiQNnqiC0r1h9uOVLhyfKr4unmisb73bugswGmutBcS3xWDFObl1K2iDAHCv/LnCgH3IHomavO4HuUBg0CQXLBStV1SEbkGaoUDTVLexveCNg+GqcPtutYA7zTrWB2c7TD4ypoZx315RcchpJYH8laWInbGZUtORRjyswLosZjjQpEL1MH5fe+g9vcwRzp0loIQU7V2OjTMqrvBIabsHizSdX+EmoCWSNiZ0e+h/MtQaBlmc9scCin/AJVtHiJaZnNtIxXFr+XaSrdrkzWiA6kfYEKR1rIZT06hnbkxHxN1olOcCdLdboqdsPrySME7fIi/5ponxg199KC3cIuv8zMslnYIUFcJCjB3U34eziDzPrt4LY3JRE5YWYXc9cx/nvXa22VZgtqH12P+XDrSoo7rMbROObw2zWox8BGwZYTwjOyIwuo/HJk1NmnmcjUCLxC2XEmDkkQ/iwAbQ14ZK6\",\"associated_data\":\"transaction\",\"nonce\":\"LRxoOYFpmJAn\"}}",
                callbackSignature: "DuBPYfdtZ9RDfwM49DQBBUj4xfHlXc+MC90HSCp9vKhBfTEsJSAqt+/r78l6nmXLSJxkqWwiWKCazRQL40mWERrHF/J0j4OJtXLb5vcKasm2th8YBIJThzpQCfU9WRJl2vSWKYyy++ubEc3IOLR+VMTVGt3g6yPs16bgMVMs7npmCkALutEaKESfV9F2f5mFgLmaWiM//ojYWzBB1NZUSDlsA57JG8dvrwwilvw2s14npxJ3I+Dgr70w9yWvjOEGkTB3lBrkz0sc0qB8q1/Smw9ApyqhbQDXJqK+v2leS/96231JCUQZR7YACIYWQx8ihu5Sbl2z09GnWUmu2IVpFw==",
                callbackSerialNumber: "7F5ACDBE4382FD3F831184B33FAA6E1D35BEE383"
            );

            string path = $"{Environment.CurrentDirectory}";

            if(path.StartsWith("/"))
            {
                path = path + "/";
            }
            else
            {
                path = path + "\\";
            }
            path = path + "wepay_platform_cert.pem";

            using (StreamWriter fw = new StreamWriter(path, true))
            {
                fw.WriteLine(cList.data[0].encrypt_certificate.ciphertext.Trim());
                fw.Close();
            }
            return cList;

            
        }


        protected void ValidSign()
        {
            string cert = "-----BEGIN CERTIFICATE-----MIID8DCCAtigAwIBAgIUT9nvFQufMaii1MsyUbzhiaZb2t8wDQYJKoZIhvcNAQELBQAwXjELMAkGA1UEBhMCQ04xEzARBgNVBAoTClRlbnBheS5jb20xHTAbBgNVBAsTFFRlbnBheS5jb20gQ0EgQ2VudGVyMRswGQYDVQQDExJUZW5wYXkuY29tIFJvb3QgQ0EwHhcNMjExMDEwMDIyMTM1WhcNMjYxMDA5MDIyMTM1WjCBgTETMBEGA1UEAwwKMTYwNDE4NDkzMzEbMBkGA1UECgwS5b6u5L+h5ZWG5oi357O757ufMS0wKwYDVQQLDCTljJfkuqzmmJPpvpnpm6rogZrllYbotLjmnInpmZDlhazlj7gxCzAJBgNVBAYMAkNOMREwDwYDVQQHDAhTaGVuWmhlbjCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAMjjqeQg2/oiG9P4ai77tBqK/ml1qWAYu0dJTSrHhkkq6FfKd6Ol2tF+OKVogGnpYEt1Y7d/CLVu4fBDzj+//PgazUINsExfCPg4xyYj4J0dfdzInRyn6nLLUEnCVQalUzNvDOSHN8OdEF6SapdMygZJYBos91ynH8FqViIIfsPQqsZO6tm+IwFJqZkyjFftKApsbujsJALg4ecIM732wQb0R6T0NGSjbpfN6fyNC9k9bPSAIjgl+YMkqlDDElDHfq+k8vDxLq1meLqff8CtuTBojTcFz379CpHqV0FCH5lx9Ot683ZJDo/c62+WHYzuPYUiTljk28i2c1bMDdk/NqsCAwEAAaOBgTB/MAkGA1UdEwQCMAAwCwYDVR0PBAQDAgTwMGUGA1UdHwReMFwwWqBYoFaGVGh0dHA6Ly9ldmNhLml0cnVzLmNvbS5jbi9wdWJsaWMvaXRydXNjcmw/Q0E9MUJENDIyMEU1MERCQzA0QjA2QUQzOTc1NDk4NDZDMDFDM0U4RUJEMjANBgkqhkiG9w0BAQsFAAOCAQEAGhs0xdUgbJBqCcg4wgsZUB6RPh3tlo5W+L7W8Ds6IOWufDy/KQScj3xSVWmGbzxS+kZaDnu23bTcqpbR+6mXcfTyPdg2VJ5nle2PJKsJsd9TVksXofNNv4b+dd2g29kSmyaJsJhSuQYYCFsJYyvOma5RoAjiUs/LMVK1TYAwSodZMoq2DmDJjv8NMOMRqiwR+vXpWnR2W+4kIG4nwi8Z+epuxehrZOW6fHKHOoaEsZ0JrxQYky2HjFNfvcCmUIX1HP+BzXZmfq+BfeemDCt5VtHNCOHzisehPs640A0S5aIjXrx9GtlYOWqAJlrNERnGJ7KXSq+7nfD0JsFY/flKcg==-----END CERTIFICATE-----";
            WepayKey key = _context.WepayKeys.Find(1);
            var certManager = new InMemoryCertificateManager();
            certManager.SetCertificate("7F5ACDBE4382FD3F831184B33FAA6E1D35BEE383", cert);
            var options = new WechatTenpayClientOptions()
            {
                MerchantId = key.mch_id.Trim(),
                CertificateManager = certManager
            };
            var client = new WechatTenpayClient(options);
            bool valid = client.VerifyEventSignature(
                callbackTimestamp: "1634265418",
                callbackNonce: "r3lQS3Zc1j87PXlsRwKTRu0DmUvrUQxe",
                callbackBody: "{\"id\":\"74b7445b-e1c6-56cd-9dcf-f9de8d4400e2\",\"create_time\":\"2021-10-15T10:36:58+08:00\",\"resource_type\":\"encrypt-resource\",\"event_type\":\"TRANSACTION.SUCCESS\",\"summary\":\"支付成功\",\"resource\":{\"original_type\":\"transaction\",\"algorithm\":\"AEAD_AES_256_GCM\",\"ciphertext\":\"5njznsCzxzmaQfgoTDIpfoOAcbJ2FtSOOKVi9zJJ1qTR9WNZo1FQDSzX0DL10JTXq+sUoQkk+xuK9Cj2pQIOVj8yWy2PoIU9Nsqt8Pp5c+APiQNnqiC0r1h9uOVLhyfKr4unmisb73bugswGmutBcS3xWDFObl1K2iDAHCv/LnCgH3IHomavO4HuUBg0CQXLBStV1SEbkGaoUDTVLexveCNg+GqcPtutYA7zTrWB2c7TD4ypoZx315RcchpJYH8laWInbGZUtORRjyswLosZjjQpEL1MH5fe+g9vcwRzp0loIQU7V2OjTMqrvBIabsHizSdX+EmoCWSNiZ0e+h/MtQaBlmc9scCin/AJVtHiJaZnNtIxXFr+XaSrdrkzWiA6kfYEKR1rIZT06hnbkxHxN1olOcCdLdboqdsPrySME7fIi/5ponxg199KC3cIuv8zMslnYIUFcJCjB3U34eziDzPrt4LY3JRE5YWYXc9cx/nvXa22VZgtqH12P+XDrSoo7rMbROObw2zWox8BGwZYTwjOyIwuo/HJk1NmnmcjUCLxC2XEmDkkQ/iwAbQ14ZK6\",\"associated_data\":\"transaction\",\"nonce\":\"LRxoOYFpmJAn\"}}",
                callbackSignature: "DuBPYfdtZ9RDfwM49DQBBUj4xfHlXc+MC90HSCp9vKhBfTEsJSAqt+/r78l6nmXLSJxkqWwiWKCazRQL40mWERrHF/J0j4OJtXLb5vcKasm2th8YBIJThzpQCfU9WRJl2vSWKYyy++ubEc3IOLR+VMTVGt3g6yPs16bgMVMs7npmCkALutEaKESfV9F2f5mFgLmaWiM//ojYWzBB1NZUSDlsA57JG8dvrwwilvw2s14npxJ3I+Dgr70w9yWvjOEGkTB3lBrkz0sc0qB8q1/Smw9ApyqhbQDXJqK+v2leS/96231JCUQZR7YACIYWQx8ihu5Sbl2z09GnWUmu2IVpFw==",
                callbackSerialNumber: "7F5ACDBE4382FD3F831184B33FAA6E1D35BEE383"
            );
        }

        protected void DecodeSign()
        {
            string callbackJson = "{\"id\":\"74b7445b-e1c6-56cd-9dcf-f9de8d4400e2\",\"create_time\":\"2021-10-15T10:36:58+08:00\",\"resource_type\":\"encrypt-resource\",\"event_type\":\"TRANSACTION.SUCCESS\",\"summary\":\"支付成功\",\"resource\":{\"original_type\":\"transaction\",\"algorithm\":\"AEAD_AES_256_GCM\",\"ciphertext\":\"5njznsCzxzmaQfgoTDIpfoOAcbJ2FtSOOKVi9zJJ1qTR9WNZo1FQDSzX0DL10JTXq+sUoQkk+xuK9Cj2pQIOVj8yWy2PoIU9Nsqt8Pp5c+APiQNnqiC0r1h9uOVLhyfKr4unmisb73bugswGmutBcS3xWDFObl1K2iDAHCv/LnCgH3IHomavO4HuUBg0CQXLBStV1SEbkGaoUDTVLexveCNg+GqcPtutYA7zTrWB2c7TD4ypoZx315RcchpJYH8laWInbGZUtORRjyswLosZjjQpEL1MH5fe+g9vcwRzp0loIQU7V2OjTMqrvBIabsHizSdX+EmoCWSNiZ0e+h/MtQaBlmc9scCin/AJVtHiJaZnNtIxXFr+XaSrdrkzWiA6kfYEKR1rIZT06hnbkxHxN1olOcCdLdboqdsPrySME7fIi/5ponxg199KC3cIuv8zMslnYIUFcJCjB3U34eziDzPrt4LY3JRE5YWYXc9cx/nvXa22VZgtqH12P+XDrSoo7rMbROObw2zWox8BGwZYTwjOyIwuo/HJk1NmnmcjUCLxC2XEmDkkQ/iwAbQ14ZK6\",\"associated_data\":\"transaction\",\"nonce\":\"LRxoOYFpmJAn\"}}";
            string callbackTimestamp = "1634265418";
            string callbackNonce = "r3lQS3Zc1j87PXlsRwKTRu0DmUvrUQxe";
            string callbackSignature = "DuBPYfdtZ9RDfwM49DQBBUj4xfHlXc+MC90HSCp9vKhBfTEsJSAqt+/r78l6nmXLSJxkqWwiWKCazRQL40mWERrHF/J0j4OJtXLb5vcKasm2th8YBIJThzpQCfU9WRJl2vSWKYyy++ubEc3IOLR+VMTVGt3g6yPs16bgMVMs7npmCkALutEaKESfV9F2f5mFgLmaWiM//ojYWzBB1NZUSDlsA57JG8dvrwwilvw2s14npxJ3I+Dgr70w9yWvjOEGkTB3lBrkz0sc0qB8q1/Smw9ApyqhbQDXJqK+v2leS/96231JCUQZR7YACIYWQx8ihu5Sbl2z09GnWUmu2IVpFw==";
            string callbackSerialNumber = "7F5ACDBE4382FD3F831184B33FAA6E1D35BEE383";



            string path = $"{Environment.CurrentDirectory}";

            if (path.StartsWith("/"))
            {
                path = path + "/";
            }
            else
            {
                path = path + "\\";
            }
            //path = path + "wechatpay_" + callbackSerialNumber + "_key.pem";
            string keyStr = "";
            using (StreamReader sr = new StreamReader(path + "wechatpay_" + callbackSerialNumber + "_key.pem", true))
            {
                keyStr = sr.ReadToEnd();
                sr.Close();
            }

            string cerStr = "";

            using (StreamReader sr = new StreamReader(path + "wechatpay_" + callbackSerialNumber + ".pem", true))
            {
                cerStr = sr.ReadToEnd();
                sr.Close();
            }

            var certManager = new InMemoryCertificateManager();
            certManager.SetCertificate(callbackSerialNumber, cerStr);
            var options = new WechatTenpayClientOptions()
            {
                CertificateManager = certManager
            };
            var client = new WechatTenpayClient(options);
            bool valid = client.VerifyEventSignature(callbackTimestamp, callbackNonce, callbackJson, callbackSignature, callbackSerialNumber);
            if (valid)
            {
                /* 将 JSON 反序列化得到通知对象 */
                /* 你也可以将 WechatTenpayEvent 类型直接绑定到 MVC 模型上，这样就不再需要手动反序列化 */
                var callbackModel = client.DeserializeEvent(callbackJson);
                if ("TRANSACTION.SUCCESS".Equals(callbackModel.EventType))
                {
                    /* 根据事件类型，解密得到支付通知敏感数据 */
                    var callbackResource = client.DecryptEventResource<SKIT.FlurlHttpClient.Wechat.TenpayV3.Events.TransactionResource>(callbackModel);
                    string outTradeNumber = callbackResource.OutTradeNumber;
                    string transactionId = callbackResource.TransactionId;
                    Console.WriteLine("订单 {0} 已完成支付，交易单号为 {1}", outTradeNumber, transactionId);
                }
            }
        }
        
        [HttpGet("{outTradeNo}")]
        public async Task<ActionResult<string>> Refund(string outTradeNo, int amount, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
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
                    MerchantCertSerialNumber = key.key_serial.Trim(),
                    MerchantCertPrivateKey = key.private_key.Trim(),
                    CertificateManager = certManager
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
                certManager.SetCertificate(serial, cerStr);
                var options = new WechatTenpayClientOptions()
                {
                    MerchantV3Secret = apiKey,
                    CertificateManager = certManager
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
                    //if (orderOnline.ticket_code!=null && orderOnline.ticket_code.Trim().Equals(""))

                }
                catch
                {

                }
            }
        }

        /*
        // GET: api/WepayOrder
        [HttpGet]
        public async Task<ActionResult<IEnumerable<WepayOrder>>> GetWepayOrders()
        {
            return await _context.WepayOrders.ToListAsync();
        }

        // GET: api/WepayOrder/5
        [HttpGet("{id}")]
        public async Task<ActionResult<WepayOrder>> GetWepayOrder(string id)
        {
            var wepayOrder = await _context.WepayOrders.FindAsync(id);

            if (wepayOrder == null)
            {
                return NotFound();
            }

            return wepayOrder;
        }

        // PUT: api/WepayOrder/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutWepayOrder(string id, WepayOrder wepayOrder)
        {
            if (id != wepayOrder.out_trade_no)
            {
                return BadRequest();
            }

            _context.Entry(wepayOrder).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!WepayOrderExists(id))
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

        // POST: api/WepayOrder
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<WepayOrder>> PostWepayOrder(WepayOrder wepayOrder)
        {
            _context.WepayOrders.Add(wepayOrder);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (WepayOrderExists(wepayOrder.out_trade_no))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetWepayOrder", new { id = wepayOrder.out_trade_no }, wepayOrder);
        }

        // DELETE: api/WepayOrder/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWepayOrder(string id)
        {
            var wepayOrder = await _context.WepayOrders.FindAsync(id);
            if (wepayOrder == null)
            {
                return NotFound();
            }

            _context.WepayOrders.Remove(wepayOrder);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool WepayOrderExists(string id)
        {
            return _context.WepayOrders.Any(e => e.out_trade_no == id);
        }
        */
    }
}

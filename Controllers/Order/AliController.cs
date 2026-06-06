using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aop.Api;
using Aop.Api.Domain;
using Aop.Api.Request;
using Aop.Api.Response;
using Aop.Api.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;
using Org.BouncyCastle.X509;
using System.IO.Compression;
using System.Net;
using Flurl.Http;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AliController : ControllerBase
    {
        public class AlipayRequestResult
        {
            public AlipayTradeResponse alipay_trade_precreate_response { get; set; }
            public AlipayOrderCreateResponse alipay_trade_create_response { get; set; }

            //public AlipayDataDataserviceBillDownloadurlQueryResponse alipay_data_dataservice_bill_downloadurl_query_response {get; set;}

            public AlipayTradeOrderOnsettleQueryResponse alipay_trade_order_onsettle_query_response { get; set; }

            public AlipayDataDataserviceBillDownloadurlQueryResponseObj alipay_data_dataservice_bill_downloadurl_query_response { get; set; }

            public string sign { get; set; }
            public string alipay_cert_sn { get; set; }
        }

        public class AlipayDataDataserviceBillDownloadurlQueryResponseObj
        {
            public string code { get; set; }
            public string msg { get; set; }
            public string bill_download_url { get; set; }
        }


        public class AlipayTradeResponse
        {
            public string code { get; set; }
            public string msg { get; set; }
            public string out_trade_no { get; set; }
            public string qr_code { get; set; }
        }

        public class AlipayOrderCreateResponse
        {
            public string code { get; set; }
            public string msg { get; set; }
            public string out_trade_no { get; set; }
            public string trade_no { get; set; }
        }
        public ApplicationDBContext _db;
        public IConfiguration _oriConfig;
        public IHttpContextAccessor _http;
        public const string ALIPAY_MINI_APP_ID = "2021006157624571";
        public string appId = "2021004143665722";
        public IAopClient client;
        public AliController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _oriConfig = config;
            _http = httpContextAccessor;
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            string appCertPublicKeyPath = certPath + "/appCertPublicKey_" + appId + ".crt";
            string privateKey = System.IO.File.OpenText(certPath + "/private_key_" + appId + ".txt").ReadToEnd().Trim();
            CertParams certParams = new CertParams
            {
                AlipayPublicCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayCertPublicKey_RSA2.crt",
                AppCertPath = appCertPublicKeyPath,
                RootCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayRootCert.crt"
            };
            client = new DefaultAopClient("https://openapi.alipay.com/gateway.do", appId, privateKey, "json", "1.0", "RSA2", "utf-8", false, certParams);
        }
        [NonAction]
        public IAopClient GetClient(string appId)
        {
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            string appCertPublicKeyPath = certPath + "/appCertPublicKey_" + appId + ".crt";
            string privateKey = System.IO.File.OpenText(certPath + "/private_key_" + appId + ".txt").ReadToEnd().Trim();
            CertParams certParams = new CertParams
            {
                AlipayPublicCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayCertPublicKey_RSA2.crt",
                AppCertPath = appCertPublicKeyPath,
                RootCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayRootCert.crt"
            };
            return new DefaultAopClient("https://openapi.alipay.com/gateway.do", appId, privateKey, "json", "1.0", "RSA2", "utf-8", false, certParams);
        }
        [NonAction]
        public async Task<AlipayMchId> GetMch(OrderOnline order)
        {
            int mchId = 2;
            AlipayMchId mch = await _db.alipayMchId.FindAsync(mchId);
            return mch;
        }

        [HttpGet]
        public async Task BindRoyaltiRelation()
        {
            AlipayTradeRoyaltyRelationBindRequest req = new AlipayTradeRoyaltyRelationBindRequest();
            AlipayTradeRoyaltyRelationBindModel model = new AlipayTradeRoyaltyRelationBindModel();
            model.OutRequestNo = Util.GetLongTimeStamp(DateTime.Now);
            List<RoyaltyEntity> receiverList = new List<RoyaltyEntity>();
            RoyaltyEntity receiverList0 = new RoyaltyEntity();
            receiverList0.Type = "userId";
            //receiverList0.bin = "2088111161955442 ";
            //receiverList0.LoginName = login;
            //receiverList0.Name = "";
            //receiverList0.Memo = "测试";
            receiverList0.Account = "2088111161955442";
            receiverList.Add(receiverList0);
            model.ReceiverList = receiverList;
            req.SetBizModel(model);
            AlipayTradeRoyaltyRelationBindResponse response = client.CertificateExecute(req);
            if (!response.IsError)
            {
                Console.WriteLine("调用成功");
                /*
                kol.ali_bind = 1;
                _db.kol.Entry(kol).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                */
                //return "true";
            }
            else
            {
                Console.WriteLine("调用失败");
                //return "false";
            }
        }




        [HttpGet]
        //public async Task BindRoyaltiRelation(string login, string name, string memo)
        public async Task<string> BindRoyaltiRelationTest(int kolId)
        {
            Kol kol = await _db.kol.FindAsync(kolId);
            string login = Util.UrlDecode(kol.ali_login_name);
            string name = Util.UrlDecode(kol.real_name);
            string memo = Util.UrlDecode(kol.memo);
            AlipayTradeRoyaltyRelationBindRequest req = new AlipayTradeRoyaltyRelationBindRequest();
            AlipayTradeRoyaltyRelationBindModel model = new AlipayTradeRoyaltyRelationBindModel();
            model.OutRequestNo = Util.GetLongTimeStamp(DateTime.Now);
            List<RoyaltyEntity> receiverList = new List<RoyaltyEntity>();
            RoyaltyEntity receiverList0 = new RoyaltyEntity();
            receiverList0.Type = "loginName";
            receiverList0.BindLoginName = login;
            //receiverList0.LoginName = login;
            receiverList0.Name = name;
            receiverList0.Memo = memo.Trim();
            receiverList0.Account = login;
            receiverList.Add(receiverList0);
            model.ReceiverList = receiverList;
            req.SetBizModel(model);
            AlipayTradeRoyaltyRelationBindResponse response = client.CertificateExecute(req);
            if (!response.IsError)
            {
                Console.WriteLine("调用成功");
                kol.ali_bind = 1;
                _db.kol.Entry(kol).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return "true";
            }
            else
            {
                Console.WriteLine("调用失败");
                return "false";
            }
        }
        [HttpGet]
        public ActionResult<double> GetUnSettledAmount(string tradeNo)
        {
            double ret = 0;
            AlipayTradeOrderOnsettleQueryRequest request = new AlipayTradeOrderOnsettleQueryRequest();
            request.BizContent = "{ \"trade_no\":\"" + tradeNo + "\" }";
            AlipayTradeOrderOnsettleQueryResponse response = client.CertificateExecute(request);
            if (!response.IsError)
                ret = double.Parse(response.UnsettledAmount);
            return Ok(ret);
        }
        /*
        [NonAction]
        public async Task<PaymentShare> Share(int shareId)
        {
            PaymentShare share = await _db.paymentShare.FindAsync(shareId);
            if (share == null)
            {
                return null;
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(share.payment_id);
            if (payment == null)
            {
                return null;
            }
            Kol kol = await _db.kol.FindAsync(share.kol_id);
            share.submit_date = DateTime.Now;
            AlipayTradeOrderSettleResponse settle = Settle(payment.ali_trade_no.Trim(), share.amount, kol.ali_login_name, kol.real_name, share.memo, share.out_trade_no.Trim());
            if (settle.IsError)
            {
                share.ret_msg = settle.SubMsg.Trim();
                share.state = -1;
            }
            else
            {
                share.state = 1;
            }
            _db.paymentShare.Entry(share).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return share;
        }
        */
        [HttpGet]
        public ActionResult<AlipayTradeOrderSettleResponse> SettleTest()
        {
            AlipayTradeOrderSettleRequest req = new AlipayTradeOrderSettleRequest();
            req.BizContent = "{" +

                /** 结算请求流水号 开发者自行生成并保证唯一性  **/
                "\"out_request_no\":\"WT_ZL_251128_00016_ZF_01_FZ_01\"," +

                /** 支付宝订单号  **/
                "\"trade_no\":\"2025112822001485891427772153\"," +

                /** 操作员id  **/
                "\"operator_id\":\"\"," +

                /** 分账明细信息，单次传入最多20个，一次分账请求中，有任意一个收入方分账失败，则这次分账请求的全部分账处理均会失败  **/
                "\"royalty_parameters\":[" +

                    /** 分账收入方信息  **/
                    "{" +
                          /** 分账类型.普通分账为：transfer;  **/
                          "\"royalty_type\":\"transfer\"," +

                           /** 支出方账户  **/
                           //"\"trans_out\":\"2088***335\"," +

                           /** 支出方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号  **/
                           //"\"trans_out_type\":\"userId\"," +

                           /** 收入方账户  **/
                           // "\"trans_in\":\"" + login + "\"," +

                           /** 收入方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号   **/
                           "\"trans_in_type\":\"userId\"," +

                           "\"trans_in\":\"2088111161955442\"," +

                          /** 分账的金额，单位为元  **/
                          "\"amount\":0.01, " +

                          /** 设分账描述  **/
                          "\"desc\":\"测试\"" +
                        "}" +
                    "]" +
                "}";

            AlipayTradeOrderSettleResponse res = client.CertificateExecute(req);
            return Ok(res);
        }
        [NonAction]
        public async Task<PaymentShare> Settle(PaymentShare share)
        {
            if (!share.valid && share.submit_time != null)
            {
                return null;
            }
            OrderShareRelation relation = share.orderShare.relation;
            share.submit_time = DateTime.Now;
            share.update_date = DateTime.Now;
            _db.paymentShare.Entry(share).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            AlipayTradeOrderSettleRequest req = new AlipayTradeOrderSettleRequest();
            req.BizContent = "{" +
                /** 结算请求流水号 开发者自行生成并保证唯一性  **/
                "\"out_request_no\":\"" + share.out_trade_no.Trim() + "\"," +
                /** 支付宝订单号  **/
                "\"trade_no\":\"" + share.payment.ali_trade_no + "\"," +
                /** 操作员id  **/
                "\"operator_id\":\"\"," +
                /** 分账明细信息，单次传入最多20个，一次分账请求中，有任意一个收入方分账失败，则这次分账请求的全部分账处理均会失败  **/
                "\"royalty_parameters\":[" +
                    /** 分账收入方信息  **/
                    "{" +
                        /** 分账类型.普通分账为：transfer;  **/
                        "\"royalty_type\":\"transfer\"," +
                        /** 支出方账户  **/
                        //"\"trans_out\":\"2088***335\"," +
                        /** 支出方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号  **/
                        //"\"trans_out_type\":\"userId\"," +
                        /** 收入方账户  **/
                        "\"trans_in\":\"" + relation.ali_account_num + "\"," +
                        /** 收入方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号   **/
                        "\"trans_in_type\":\"" + relation.ali_account_type + "\"," +
                        // "\"trans_in_name\":\"" + name + "\"," +
                        /** 分账的金额，单位为元  **/
                        "\"amount\":" + share.amount.ToString() + ", " +
                        /** 设分账描述  **/
                        "\"desc\":\"" + ((share.memo == null) ? "" : share.memo.Trim()) + "\"" +
                    "}" +
                "]" +
            "}";
            AlipayTradeOrderSettleResponse res = client.CertificateExecute(req);
            if (res.IsError)
            {
                share.success = false;
                share.response_content = res.Code.Trim() + ":" + res.Msg.Trim() + " " + res.SubCode.Trim() + ":" + res.SubMsg.Trim();
                share.response_time = DateTime.Now;
                share.update_date = DateTime.Now;
            }
            else
            {
                share.success = true;
                share.update_date = DateTime.Now;
                share.response_time = DateTime.Now;
                share.response_content = res.SettleNo.Trim();
            }
            _db.paymentShare.Entry(share).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return share;
        }
        [NonAction]
        public AlipayTradeOrderSettleResponse Settle1(string tradeNo, double amount, string login, string name, string memo, string outTradeNo)
        {
            login = Util.UrlDecode(login);
            memo = Util.UrlDecode(memo);
            name = Util.UrlDecode(name);
            AlipayTradeOrderSettleRequest req = new AlipayTradeOrderSettleRequest();


            req.BizContent = "{" +
                /** 结算请求流水号 开发者自行生成并保证唯一性  **/
                "\"out_request_no\":\"" + outTradeNo.Trim() + "\"," +
                /** 支付宝订单号  **/
                "\"trade_no\":\"" + tradeNo.Trim() + "\"," +
                /** 操作员id  **/
                "\"operator_id\":\"\"," +
                /** 分账明细信息，单次传入最多20个，一次分账请求中，有任意一个收入方分账失败，则这次分账请求的全部分账处理均会失败  **/
                "\"royalty_parameters\":[" +
                    /** 分账收入方信息  **/
                    "{" +
                          /** 分账类型.普通分账为：transfer;  **/
                          "\"royalty_type\":\"transfer\"," +
                          /** 支出方账户  **/
                          //"\"trans_out\":\"2088***335\"," +
                          /** 支出方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号  **/
                          //"\"trans_out_type\":\"userId\"," +
                          /** 收入方账户  **/
                          "\"trans_in\":\"" + login + "\"," +
                           /** 收入方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号   **/
                           "\"trans_in_type\":\"loginName\"," +
                           "\"trans_in_name\":\"" + name + "\"," +
                          /** 分账的金额，单位为元  **/
                          "\"amount\":" + amount.ToString() + ", " +
                          /** 设分账描述  **/
                          "\"desc\":\"" + memo + "\"" +
                        "}" +
                    "]" +
                "}";
            return client.CertificateExecute(req);
            /*
            if(!response.IsError)
            {
                Console.WriteLine("调用成功");
            }
            else
            {
                Console.WriteLine("调用失败");
            }
            */
        }

        [HttpGet]
        public async Task<OrderPayment> CreateOrder(int paymentId)
        {
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            string notify = "https://mini.snowmeet.top/core/Ali/Callback";
            AlipayTradeCreateRequest req = new AlipayTradeCreateRequest();
            req.SetNotifyUrl(notify);
            AlipayTradeCreateModel model = new AlipayTradeCreateModel();
            model.OutTradeNo = payment.out_trade_no.Trim();
            model.ProductCode = "JSAPI_PAY";
            model.OpAppId = appId.Trim();
            ////////////////////////////////////
            //////等待从订单获取//////////////////
            model.Subject = "test";
            model.Body = "test1";
            /////////////////////////////////////
            model.TotalAmount = payment.amount.ToString();
            model.ExtendParams = new ExtendParams { RoyaltyFreeze = "true" };
            model.BuyerId = "2088002319285895";
            req.SetBizModel(model);

            AlipayTradeCreateResponse res = client.CertificateExecute(req);
            if (res.IsError)
            {
                return null;

            }
            else
            {
                AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(res.Body.Trim());
                payment.ali_trade_no = respObj.alipay_trade_create_response.trade_no.Trim();
                _db.OrderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return payment;
            }

        }
        [HttpGet]
        public ActionResult<string> TestUniPay()
        {
            DateTime now = DateTime.Now;
            string outTradeNo = "Test_UniPay_" + now.ToString("yyyyMMddhhmmss");
            AlipayTradePrecreateRequest request = new AlipayTradePrecreateRequest();
            request.SetNotifyUrl("https://snowmeet.wanlonghuaxue.com/ali/callback");
            //payment.notify = notify;
            List<Aop.Api.Domain.GoodsDetail> gList = new List<GoodsDetail>();
            //for (int i = 0; i < order.fdOrders.Count; i++)
            //{
                Aop.Api.Domain.GoodsDetail detail = new GoodsDetail()
                {
                    AlipayGoodsId = "",
                    Body = "",
                    CategoriesTree = "",
                    GoodsCategory = "",
                    GoodsId = "test",
                    GoodsName = "unipay test",
                    OutItemId = "",
                    OutSkuId = "",
                    Price = "0.01",
                    Quantity = 1,
                    ShowUrl = ""
                };
                gList.Add(detail);
            //}
            AlipayTradePrecreateModel model = new AlipayTradePrecreateModel();
            model.OutTradeNo = outTradeNo.Trim();
            model.Subject = "聚合支付测试";
            model.Body = "支付1分钱";
            model.TotalAmount = "0.01";
            model.ExtendParams = new ExtendParams { RoyaltyFreeze = "false" };
            model.QrCodeTimeoutExpress = "60m";
            //model.ProductCode = order.id.ToString();
            model.GoodsDetail = gList;
            request.SetBizModel(model);
            AlipayTradePrecreateResponse response = client.CertificateExecute(request);
            string responseStr = response.Body.Trim();
            Console.WriteLine(responseStr);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(responseStr);
            return Ok(outTradeNo);
        }
        [NonAction]
        public async Task<OrderPayment> GetPaymentQrCodeUrl(Models.OrderPayment payment, Models.Order order)
        {
            client = GetClient(appId.Trim());
            if (order == null)
            {
                return null;
            }
            string notify = "https://" + _http.HttpContext.Request.Host.Value + "/api/Ali/CallBack";
            AlipayTradePrecreateRequest request = new AlipayTradePrecreateRequest();
            request.SetNotifyUrl(notify);
            payment.notify = notify;
            List<Aop.Api.Domain.GoodsDetail> gList = new List<GoodsDetail>();
            for (int i = 0; i < order.fdOrders.Count; i++)
            {
                Aop.Api.Domain.GoodsDetail detail = new GoodsDetail()
                {
                    AlipayGoodsId = "",
                    Body = "",
                    CategoriesTree = "",
                    GoodsCategory = "",
                    GoodsId = order.fdOrders[i].product_id.ToString(),
                    GoodsName = order.fdOrders[i].product_name.Trim(),
                    OutItemId = "",
                    OutSkuId = "",
                    Price = order.fdOrders[i].unit_price.ToString(),
                    Quantity = order.fdOrders[i].count,
                    ShowUrl = ""
                };
                gList.Add(detail);
            }
            AlipayTradePrecreateModel model = new AlipayTradePrecreateModel();
            model.OutTradeNo = payment.out_trade_no.Trim();
            model.Subject = order.subject.Trim();
            model.Body = order.description.Trim();
            model.TotalAmount = Math.Round(payment.amount, 2).ToString();
            model.ExtendParams = new ExtendParams { RoyaltyFreeze = "false" };
            model.QrCodeTimeoutExpress = "60m";
            //model.ProductCode = order.id.ToString();
            model.GoodsDetail = gList;
            request.SetBizModel(model);
            AlipayTradePrecreateResponse response = client.CertificateExecute(request);
            string responseStr = response.Body.Trim();
            Console.WriteLine(responseStr);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(responseStr);
            payment.submit_time = DateTime.Now;
            try
            {
                payment.ali_qr_code = respObj.alipay_trade_precreate_response.qr_code.Trim();
            }
            catch
            {
                payment.request_failed = 1;
            }
            payment.response_data = JsonConvert.SerializeObject(respObj);
            payment.update_date = DateTime.Now;
            //order.waiting_for_pay = 1;
            _db.orderPayment.Entry(payment).State = EntityState.Modified;
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return payment;
        }
        public class AliCallBackModel
        {
            public string tradeStatus { get; set; } = "";
            public string tradeNo { get; set; } = "";
            public string appId { get; set; } = "";
            public string sellerId { get; set; } = "";
            public string buyerId { get; set; } = "";
            public string outTradeNo { get; set; } = "";
            public string notifyId { get; set; } = "";
            public string notifyType { get; set; } = "";
        }
        [NonAction]
        public AliCallBackModel ParseCallBack(string notifyString)
        {
            //string notifyString = "gmt_create=2025-07-31+18%3A42%3A51&charset=utf-8&seller_email=13501177897%40139.com&subject=%E9%A4%90%E9%A5%AE%E8%AE%A2%E5%8D%95&sign=K9pIB1jmtRyjaggc3o7kH2hTByrSTE%2FBQzQu2Ht%2BjENjOtvWbk%2BL5R1eIGEwsCRjcm846hZJ2OLalCC4pLzfXmX15l2ckDoVG%2BIx441uWkSjleL59FwK%2F5fRhqFoErm7f5SJ76bpkbVPd4F4sh30NG1sybQRJRAD8Sdg1uZXc70nPSwiipP6Ev%2FlvXbk8Q6rr2DxlCFtnsu%2F9Re3N%2FRTTDfFOw0GtJTtUjLWjafMGP2upiueSo1xkxTFZxUFpmc9GGXvKnRdvkX5L2VAyPAsMYLy1y3c%2FmmAVJ%2FYumFhZNkzUMhFYkHRt5Kv41AVxDoNuhlsrm1cv2CxVGaEj9iVHA%3D%3D&buyer_id=2088002319285895&body=%E5%86%B0%E6%91%A9%E5%8D%A1%E6%BC%82%E6%B5%AE+x+1%3B&invoice_amount=0.01&notify_id=2025073101222184300085891463989777&fund_bill_list=%5B%7B%22amount%22%3A%220.01%22%2C%22fundChannel%22%3A%22ALIPAYACCOUNT%22%7D%5D&notify_type=trade_status_sync&trade_status=TRADE_SUCCESS&receipt_amount=0.01&buyer_pay_amount=0.01&app_id=2021004143665722&sign_type=RSA2&seller_id=2088640272285174&gmt_payment=2025-07-31+18%3A43%3A00&notify_time=2025-07-31+18%3A43%3A01&version=1.0&out_trade_no=QJ_CY_250731_00005_ZF_01&total_amount=0.01&trade_no=2025073122001485891444588212&auth_app_id=2021004143665722&buyer_logon_id=can***%40vip.sina.com&point_amount=0.00";
            Console.WriteLine(Util.UrlDecode(notifyString));
            string[] notifyStrArr = notifyString.Split('&');
            AliCallBackModel callback = new AliCallBackModel();
            foreach (string nPair in notifyStrArr)
            {
                string[] pairArr = nPair.Split('=');
                string key = pairArr[0].Trim();
                string value = pairArr.Length > 1 ? pairArr[1].Trim() : "";
                switch (key)
                {
                    case "seller_id":
                        callback.sellerId = value.Trim();
                        break;
                    case "app_id":
                        callback.appId = value.Trim();
                        break;
                    case "buyer_id":
                        callback.buyerId = value.Trim();
                        break;
                    case "trade_status":
                        callback.tradeStatus = value.Trim();
                        break;
                    case "out_trade_no":
                        callback.outTradeNo = value.Trim();
                        break;
                    case "trade_no":
                        callback.tradeNo = value.Trim();
                        break;
                    case "notify_id":
                        callback.notifyId = value.Trim();
                        break;
                    case "notify_type":
                        callback.notifyType = value.Trim();
                        break;
                    default:
                        break;
                }
            }
            return callback;
        }

        [HttpPost]
        public async Task<ActionResult<string>> CallBack()
        {
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            StreamReader sr = new StreamReader(Request.Body);
            string postStr = await sr.ReadToEndAsync();
            sr.Close();
            System.IO.File.AppendAllText(certPath + "/alipay_callback_" + DateTime.Now.ToString("yyyyMMdd") + ".txt", DateTime.Now.ToString() + "\t" + postStr + "\r\n");
            AliCallBackModel callback = ParseCallBack(postStr);
            switch (callback.notifyType.Trim())
            {
                case "trade_status_sync":
                    if (callback.tradeStatus.ToUpper().Trim().Equals("TRADE_SUCCESS"))
                    {
                        OrderPayment payment = await _db.orderPayment.Where(p => p.pay_method.Trim().Equals("支付宝")
                            && p.out_trade_no.Trim().Equals(callback.outTradeNo.Trim())).OrderByDescending(p => p.id).FirstOrDefaultAsync();
                        if (payment.status.Trim().Equals(OrderPayment.PaymentStatus.待支付.ToString()))
                        {
                            payment.valid = 1;
                            payment.ali_trade_no = callback.tradeNo;
                            payment.notify_id = callback.notifyId;
                            payment.paid_date = DateTime.Now;
                            payment.update_date = DateTime.Now;
                            payment.ali_buyer_id = callback.buyerId;
                            payment.status = OrderPayment.PaymentStatus.支付成功.ToString();
                            payment.update_date = DateTime.Now;
                            _db.orderPayment.Entry(payment).State = EntityState.Modified;
                            await _db.SaveChangesAsync();
                            CoreDataModLog log = new CoreDataModLog()
                            {
                                table_name = "Order",
                                field_name = "OrderState",
                                key_value = payment.order_id,
                                prev_value = null,
                                current_value = Models.Order.OrderStatus.支付成功.ToString(),
                                staff_id = null,
                                is_manual = 1,
                                scene = "支付宝支付成功",
                                create_date = DateTime.Now

                            };
                            await _db.coreDataModLog.AddAsync(log);
                            await _db.SaveChangesAsync();

                            // 2026-06-03 用户原则: alipay 路径会员推迟到支付成功后建。
                            // PaymentIdentity / AlipayPayByOrderPayment 都允许 OP.member_id 为 null,
                            // 这里收到 trade_success 后,按 buyer_id → MSA(alipay_payerid) → 命中即用;
                            // 没命中再从最新 alipay 会话拿 cell → MSA(cell) 命中即用 + 补 MSA(alipay_payerid);
                            // 都没命中 → 建新 Member + MSA(alipay_payerid)[+ MSA(cell) 若有], 最后写回 payment.member_id, 再调 DSP。
                            if (payment.member_id == null && !string.IsNullOrEmpty(callback.buyerId))
                            {
                                payment.member_id = await _materializeAlipayMemberOnPaid(payment, callback.buyerId);
                                if (payment.member_id != null)
                                {
                                    payment.update_date = DateTime.Now;
                                    _db.orderPayment.Entry(payment).State = EntityState.Modified;
                                    await _db.SaveChangesAsync();
                                }
                            }

                            OrderController _orderHelper = new OrderController(_db, _oriConfig, _http);
                            await _orderHelper.DealSuccessPaidOrder(payment.order_id, payment.id);
                        }
                    }
                    break;
                default:
                    break;
            }
            return Ok("success");
        }

        [HttpGet]
        [HttpPost]
        public async Task<ActionResult<string>> AppGateway()
        {
            string queryText = Request.QueryString.HasValue ? Request.QueryString.Value : "";
            string formText = "";
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                formText = string.Join("&", form.Select(kvp =>
                    kvp.Key + "=" + string.Join(",", kvp.Value.Select(v => v ?? ""))));
            }

            Request.EnableBuffering();
            string bodyText;
            using (var sr = new StreamReader(Request.Body, Encoding.UTF8, true, 1024, true))
            {
                bodyText = await sr.ReadToEndAsync();
            }
            Request.Body.Position = 0;

            string miniCertPath = Util.workingPath + "/AlipayCertificate/" + ALIPAY_MINI_APP_ID;
            string certPath = Directory.Exists(miniCertPath)
                ? miniCertPath
                : (Util.workingPath + "/AlipayCertificate/" + appId);
            string logPath = certPath + "/alipay_app_gateway_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + "\tmethod=" + (Request.Method ?? "")
                + "\tpath=" + (Request.Path.HasValue ? Request.Path.Value : "")
                + "\tquery=" + queryText
                + "\tform=" + formText
                + "\tbody=" + (bodyText ?? "")
                + "\r\n";
            System.IO.File.AppendAllText(logPath, line);

            string challenge = Request.Query["challenge"].ToString();
            if (string.IsNullOrEmpty(challenge) && Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                challenge = form["challenge"].ToString();
            }

            if (!string.IsNullOrEmpty(challenge))
            {
                return Ok(challenge);
            }

            return Ok("success");
        }

        // 2026-06-03: alipay 支付成功后兜底建会员/查会员, 与「PaymentIdentity 推迟建会员」原则配套。
        // 返回 memberId (可能是已有的 / 新建的); 永远不返 null (保证后续 DSP 能 sync Order.member_id)。
        // 调用前提: payment.member_id == null, buyerId 非空。
        private async Task<int?> _materializeAlipayMemberOnPaid(OrderPayment payment, string buyerId)
        {
            string buyerIdTrim = buyerId.Trim();

            // 1. 用 alipay_payerid 反查 MSA 命中已有会员
            var alipayMsa = await _db.memberSocialAccount
                .Where(m => m.valid == 1
                    && m.type.Trim().Equals("alipay_payerid")
                    && m.num.Trim().Equals(buyerIdTrim))
                .OrderByDescending(m => m.id).AsNoTracking().FirstOrDefaultAsync();
            if (alipayMsa != null)
            {
                return alipayMsa.member_id;
            }

            // 2. 没命中 → 从最新一个 alipay session 拿 cell, 用 cell 反查 MSA
            //    session 必须是「这位 alipay 顾客的 session」即 alipay_payerid == buyerId
            var sess = await _db.miniSession
                .Where(s => s.session_type.Trim().Equals("alipay_payerid")
                    && s.alipay_payerid != null
                    && s.alipay_payerid.Trim().Equals(buyerIdTrim))
                .OrderByDescending(s => s.expire_date)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            string sessCell = sess?.cell?.Trim();
            int? memberId = null;

            if (!string.IsNullOrEmpty(sessCell))
            {
                var cellMsa = await _db.memberSocialAccount
                    .Where(m => m.valid == 1
                        && m.type.Trim().Equals("cell")
                        && m.num.Trim().Equals(sessCell))
                    .OrderByDescending(m => m.id).AsNoTracking().FirstOrDefaultAsync();
                if (cellMsa != null)
                {
                    memberId = cellMsa.member_id;
                }
            }

            // 3. 仍未命中 → 建新 Member (无 cell 也建; cell 有则一并写 MSA)
            if (memberId == null)
            {
                var newMember = new SnowmeetApi.Models.Member
                {
                    real_name = "",
                    gender = "",
                    source = "支付宝支付成功",
                    valid = 1
                };
                await _db.member.AddAsync(newMember);
                await _db.SaveChangesAsync();
                memberId = newMember.id;

                if (!string.IsNullOrEmpty(sessCell))
                {
                    await _db.memberSocialAccount.AddAsync(new MemberSocialAccount
                    {
                        id = 0,
                        member_id = newMember.id,
                        type = "cell",
                        num = sessCell,
                        valid = 1
                    });
                    await _db.SaveChangesAsync();
                }
            }

            // 4. 不论是建新会员还是命中已有 cell 会员, 都补一条 MSA(alipay_payerid) 把 payerId 绑到该会员
            await _db.memberSocialAccount.AddAsync(new MemberSocialAccount
            {
                id = 0,
                member_id = (int)memberId,
                type = "alipay_payerid",
                num = buyerIdTrim,
                valid = 1
            });
            await _db.SaveChangesAsync();

            // 5. session.member_id 也回填 (若该 session 还没绑会员), 下次同一 session 走 PaymentIdentity 不用再走兜底
            if (sess != null && sess.member_id == null)
            {
                // 重新 attach (我们用的是 AsNoTracking 拿的)
                var sessTracked = await _db.miniSession.FindAsync(sess.session_key, sess.session_type);
                if (sessTracked != null)
                {
                    sessTracked.member_id = memberId;
                    _db.miniSession.Entry(sessTracked).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }

            return memberId;
        }

        [NonAction]
        public async Task<OrderPaymentRefund> Refund(int refundId)
        {
            OrderPaymentRefund refund = await _db.orderPaymentRefund.FindAsync(refundId);
            if (refund == null)
            {
                return null;
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(refund.payment_id);
            if (payment == null)
            {
                return null;
            }
            var refunds = await _db.orderPaymentRefund.Where(r => r.payment_id == payment.id)
                .AsNoTracking().ToListAsync();
            try
            {
                AlipayTradeRefundResponse res = Refund(payment.out_trade_no.Trim(), refund.out_refund_no.Trim(), refund.amount, refund.reason.Trim());
                if (res.FundChange != null && res.FundChange.ToUpper() == "Y")
                {
                    refund.state = 1;
                }
                refund.memo = res.Msg.Trim();
                refund.refund_id = res.TradeNo;
            }
            catch (Exception ex)
            {
                refund.memo = ex.ToString().Length > 500 ? ex.ToString().Substring(0, 500) : ex.ToString();
            }

            _db.orderPaymentRefund.Entry(refund).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return refund;
        }


        [HttpGet]
        public AlipayTradeRefundResponse Refund(string outTradeNo, string outRefundNo, double amount, string reason = "")
        {

            AlipayTradeRefundRequest req = new AlipayTradeRefundRequest();
            AlipayTradeRefundModel model = new AlipayTradeRefundModel();
            model.OutTradeNo = outTradeNo;
            model.OutRequestNo = outRefundNo;
            model.RefundAmount = amount.ToString();
            model.RefundReason = reason.Trim();
            req.SetBizModel(model);

            AlipayTradeRefundResponse res = client.CertificateExecute(req);
            Console.WriteLine(res);

            return res;


        }
        /*
        [HttpGet("{appId}")]
        public async Task GetBill(string appId, DateTime billDate)
        {
            IAopClient alipayClient = GetClient(appId);
            AlipayDataDataserviceBillDownloadurlQueryRequest request = new AlipayDataDataserviceBillDownloadurlQueryRequest();
            AlipayDataDataserviceBillDownloadurlQueryModel model = new AlipayDataDataserviceBillDownloadurlQueryModel();
            //model.Smid = "2088123412341234";
            model.BillType = "trade";
            model.BillDate = billDate.ToString("yyyy-MM-dd");
            request.SetBizModel(model);
            AlipayDataDataserviceBillDownloadurlQueryResponse response = alipayClient.CertificateExecute(request);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(response.Body.Trim());
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            //string billStr = Util.GetWebContent(respObj.alipay_data_dataservice_bill_downloadurl_query_response.bill_download_url.Trim(), Encoding.GetEncoding("GB2312"));
            string downloadPath = Util.workingPath + "/AlipayCertificate/" + appId + "/downloads";
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }
            string tempFileName = billDate.ToString("yyyyMMdd") + "_" + Util.GetLongTimeStamp(DateTime.Now).ToString() + ".zip";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(respObj.alipay_data_dataservice_bill_downloadurl_query_response.bill_download_url.Trim());
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            Stream s = res.GetResponseStream();
            if (System.IO.File.Exists(downloadPath + "/" + tempFileName))
            {
                System.IO.File.Delete(downloadPath + "/" + tempFileName);

            }
            using (var zipFileStream = System.IO.File.Create(downloadPath + "/" + tempFileName))
            {
                await s.CopyToAsync(zipFileStream);
            }
            s.Close();
            res.Close();
            req.Abort();
            using (var zip = ZipFile.Open(downloadPath + "/" + tempFileName, ZipArchiveMode.Read, Encoding.GetEncoding("GB2312")))
            {

                foreach (var entry in zip.Entries)
                {

                    string fileName = entry.FullName.Trim();
                    fileName = Util.UrlDecode(fileName);

                    Console.WriteLine("文件名：{0}", entry.FullName);
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream, Encoding.GetEncoding("GB2312")))
                    {
                        var str = reader.ReadToEnd();
                        Console.WriteLine(str);
                    }
                }
            }
            if (!response.IsError)
            {
                Console.WriteLine("调用成功");
            }
            else
            {
                Console.WriteLine("调用失败");
            }
        }
        */
        [HttpGet]
        public void GetFlow(DateTime startDate, DateTime endDate)
        {
            IAopClient client = GetClient(appId);
            AlipayDataBillAccountlogQueryRequest req = new AlipayDataBillAccountlogQueryRequest();
            req.BizContent = "{" +
                "  \"start_time\":\"" + startDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"end_time\":\"" + endDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"page_no\":\"1\"," +
                "  \"page_size\":\"2000\" }";
            AlipayDataBillAccountlogQueryResponse response = client.CertificateExecute(req);
            Console.WriteLine(response.Body);
        }

        [HttpGet]
        public void GetBalance(string type, DateTime startDate, DateTime endDate)
        {

            IAopClient client = GetClient(appId);
            AlipayDataBillTransferQueryRequest req = new AlipayDataBillTransferQueryRequest();
            req.BizContent = "{" +
                "  \"start_time\":\"" + startDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"end_time\":\"" + endDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"type\":\"" + type + "\"," +
                "  \"page_no\":\"1\"," +
                "  \"page_size\":\"2000\" }";
            AlipayDataBillTransferQueryResponse response = client.CertificateExecute(req);
            Console.WriteLine(response.Body);
        }

        [HttpGet]
        public async Task DataAll(DateTime? billDate = null, string type = "signcustomer")
        {
            DateTime currentDate = billDate == null? DateTime.Now.Date.AddDays(-1) : (DateTime)billDate;
            IAopClient client = GetClient(appId);
            AlipayDataDataserviceBillDownloadurlQueryModel model = new AlipayDataDataserviceBillDownloadurlQueryModel();
            //model.setSmid("2088123412341234");
            model.BillType = type;
            model.BillDate =  currentDate.ToString("yyyy-MM-dd");
            AlipayDataDataserviceBillDownloadurlQueryRequest req = new AlipayDataDataserviceBillDownloadurlQueryRequest();
            req.SetBizModel(model);
            AlipayDataDataserviceBillDownloadurlQueryResponse res = client.CertificateExecute(req);
            Console.WriteLine(res.Body);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(res.Body.Trim());
            Console.WriteLine(respObj.alipay_data_dataservice_bill_downloadurl_query_response.bill_download_url);
            if (respObj.alipay_data_dataservice_bill_downloadurl_query_response.bill_download_url == null)
            {
                return;
            }
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            //string billStr = Util.GetWebContent(respObj.alipay_data_dataservice_bill_downloadurl_query_response.bill_download_url.Trim(), Encoding.GetEncoding("GB2312"));
            string downloadPath = Util.workingPath + "/AlipayCertificate/" + appId + "/downloads";
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }
            string tempFileName = currentDate.ToString("yyyyMMdd") + "_" + Util.GetLongTimeStamp(DateTime.Now).ToString() + ".zip";
            HttpWebRequest reqWeb = (HttpWebRequest)WebRequest.Create(respObj.alipay_data_dataservice_bill_downloadurl_query_response.bill_download_url.Trim());
            HttpWebResponse resWeb = (HttpWebResponse)reqWeb.GetResponse();
            Stream s = resWeb.GetResponseStream();
            if (System.IO.File.Exists(downloadPath + "/" + tempFileName))
            {
                System.IO.File.Delete(downloadPath + "/" + tempFileName);

            }
            using (var zipFileStream = System.IO.File.Create(downloadPath + "/" + tempFileName))
            {
                await s.CopyToAsync(zipFileStream);
            }
            s.Close();
            resWeb.Close();
            reqWeb.Abort();
            using (var zip = ZipFile.Open(downloadPath + "/" + tempFileName, ZipArchiveMode.Read, Encoding.GetEncoding("GB2312")))
            {
                foreach (var entry in zip.Entries)
                {
                    string fileName = entry.FullName.Trim();
                    fileName = Util.UrlDecode(fileName);
                    Console.WriteLine("文件名：{0}", entry.FullName);
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream, Encoding.GetEncoding("GB2312")))
                    {
                        var str = reader.ReadToEnd();
                        if (entry.FullName.Trim().EndsWith(currentDate.ToString("yyyyMMdd") + "_TRANSFER_DETAILS.csv"))
                        {
                            await DealTrans(str.Trim());
                        }
                        if (entry.FullName.Trim().EndsWith(currentDate.ToString("yyyyMMdd") + "_账务明细.csv"))
                        {
                            await DealTrans(str.Trim());
                        }

                        Console.WriteLine(str);
                    }
                }
            }
        }
        [NonAction]
        private async Task DealTrans(string content)
        {
            string[] lineArr = content.Split('\r');
            for (int i = 0; i < lineArr.Length; i++)
            {
                string lineStr = lineArr[i].Trim();
                if (lineStr.StartsWith("#") || lineStr.StartsWith("账务流水号"))
                {
                    continue;
                }

                string[] fieldsArr = lineStr.Split(',');

                AliDownloadFlowBill b = await _db.aliDownloadFlowBill.FindAsync(fieldsArr[0]);
                if (b != null)
                {
                    continue;
                }
                b = new AliDownloadFlowBill()
                {
                    id = fieldsArr[0].Trim(),
                    biz_num = fieldsArr[1].Trim(),
                    out_trade_num = fieldsArr[2].Trim(),
                    prod_name = fieldsArr[3].Trim(),
                    trans_date = DateTime.Parse(fieldsArr[4].Trim()),
                    receiver_ali_account = fieldsArr[5].Trim(),
                    income = double.Parse(fieldsArr[6].Trim()),
                    outcome = double.Parse(fieldsArr[7].Trim()),
                    remainder = double.Parse(fieldsArr[8].Trim()),
                    trans_channel = fieldsArr[9].Trim(),
                    biz_type = fieldsArr[10].Trim(),
                    memo = fieldsArr[11].Trim()
                };
                await _db.aliDownloadFlowBill.AddAsync(b);
            }
            await _db.SaveChangesAsync();
        }

        [HttpGet]
        public void TransTest(string outTradeNo, double amount)
        {
            IAopClient client = GetClient(appId);
            AlipayFundTransUniTransferRequest request = new AlipayFundTransUniTransferRequest();
            AlipayFundTransUniTransferModel model = new AlipayFundTransUniTransferModel();
            model.OrderTitle = "TRANS_ACCOUNT_NO_PWD";

            // 设置描述特定的业务场景
            model.BizScene = "DIRECT_TRANSFER";

            // 设置转账业务请求的扩展参数
            model.BusinessParams = "{\"payer_show_name_use_alias\":\"true\"}";

            // 设置业务备注
            model.Remark = "订单分账转账";

            // 设置商家侧唯一订单号
            model.OutBizNo = outTradeNo.Trim();//"HBI_MTNC_20240619_040557_01_SHARE_03_20240619_001_TRANS";

            // 设置订单总金额
            model.TransAmount = amount.ToString();

            // 设置业务产品码
            model.ProductCode = "TRANS_ACCOUNT_NO_PWD";

            // 设置收款方信息
            Participant payeeInfo = new Participant();
            payeeInfo.Identity = "13501177897";
            payeeInfo.Name = "苍杰";
            payeeInfo.IdentityType = "ALIPAY_LOGON_ID";
            model.PayeeInfo = payeeInfo;

            request.SetBizModel(model);
            AlipayFundTransUniTransferResponse response = client.CertificateExecute(request);

            if (!response.IsError)
            {
                Console.WriteLine("调用成功");
            }
            else
            {
                Console.WriteLine("调用失败");
            }

        }
        [HttpGet]
        public async Task<bool> ClosePayment(OrderPayment payment)
        {
            //OrderPayment payment = await _db.orderPayment.Where(p => p.id == pay)
            AlipayTradeCancelModel model = new AlipayTradeCancelModel()
            {
                OutTradeNo = payment.out_trade_no,
                TradeNo = payment.ali_trade_no
            };
            AlipayTradeCancelRequest req = new AlipayTradeCancelRequest();
            req.SetBizModel(model);
            AlipayTradeCancelResponse res = client.CertificateExecute(req);
            if (res.Code.Trim().Equals("10000") && res.Msg.Trim().ToLower().Equals("success"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
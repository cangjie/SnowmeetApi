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
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;
using Org.BouncyCastle.X509;
using System.IO.Compression;
using System.Net;
using Flurl.Http;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class AliController : ControllerBase
    {
        public class AlipayRequestResult
        {
            public AlipayTradeResponse alipay_trade_precreate_response { get; set; }
            public AlipayOrderCreateResponse alipay_trade_create_response {get; set;} 

            //public AlipayDataDataserviceBillDownloadurlQueryResponse alipay_data_dataservice_bill_downloadurl_query_response {get; set;}

            public AlipayTradeOrderOnsettleQueryResponse alipay_trade_order_onsettle_query_response {get; set;}

            public AlipayDataDataserviceBillDownloadurlQueryResponseObj alipay_data_dataservice_bill_downloadurl_query_response {get; set;}

            public string sign { get; set; }
            public string alipay_cert_sn { get; set; }
        }

        public class AlipayDataDataserviceBillDownloadurlQueryResponseObj
        {
            public string code {get; set;}
            public string msg {get; set;}
            public string bill_download_url {get; set;}
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
        /*
        public class AlipayTradeOrderOnsettleQueryResponse
        {
            public string code { get; set; }
            public string msg { get; set; }

            public double unsettled_amount {get; set;} = 0;

        }
        */
        public ApplicationDBContext _db;
        public IConfiguration _oriConfig;
        public IHttpContextAccessor _http;

        //public string appId = "2021004143665722";
        public string appId = "2021004150619003";

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
        public IAopClient  GetClient(string appId)
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
        //public async Task BindRoyaltiRelation(string login, string name, string memo)
        public async Task<string> BindRoyaltiRelation(int kolId)
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
             if(!response.IsError){
             	Console.WriteLine("调用成功");
                kol.ali_bind = 1;
                _db.kol.Entry(kol).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return "true";
             }
             else{
             	Console.WriteLine("调用失败");
                return "false";
             } 
        }

        [HttpGet]
        public ActionResult<double> GetUnSettledAmount(string tradeNo)
        {
            double ret = 0;
            AlipayTradeOrderOnsettleQueryRequest  request= new AlipayTradeOrderOnsettleQueryRequest() ;
            request.BizContent="{ \"trade_no\":\"" + tradeNo  + "\" }";
            AlipayTradeOrderOnsettleQueryResponse response=client.CertificateExecute(request);
            if (!response.IsError)
                ret = double.Parse(response.UnsettledAmount);
            return Ok(ret);
        }
/*
        [HttpGet]
        public async Task SettleTest(string tradeNo, double amount, string login, string name,  string memo)
        {
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;

            String AlipayPublicCertPath = certPath + "/alipayCertPublicKey_RSA2.crt";   
            //解析支付宝公钥的值需要引用using Org.BouncyCastle.X509和using Aop.Api.Util;
            Org.BouncyCastle.X509.X509Certificate alipayPublicKeyCert = AntCertificationUtil.ParseCert(System.IO.File.ReadAllText(AlipayPublicCertPath));
            String PUBLIC_KEY = AntCertificationUtil.ExtractPemPublicKeyFromCert(alipayPublicKeyCert);

             /** 支付宝网关 **/
            //String ALIPAY_GATEWAY = "https://openapi.alipay.com/gateway.do";

            /** 应用id，如何获取请参考：https://opensupport.alipay.com/support/helpcenter/190/201602493024 **/
            //String APP_ID = appId;

            /** 应用私钥，密钥格式为pkcs1，如何获取私钥请参考：https://opensupport.alipay.com/support/helpcenter/207/201602469554  **/
            //String PRIVATE_KEY = "MIIEpAIBAAKCAQEAsL0G9Qg182PpISGvwXefHyPRnxd4lhy6JEC6NWwHRkY39qJYKPHEWqhUbvFLP7NMw+Gzdz2LmukaBTw4nvVqy/5e+294eZ7LwGDah+E7jbOFMW5JoY9Pz+3NcqRuPhjT5YN4CIIAH4O1YcA3iPri0Yc+lUl9/2dq/Yr9NcR9r/5BJ7AFeYCmLqzEJGoA6L+8O/rlLiLXQMqKMM6h/EUcn7lD6in52T/i4T0h1jBLp+//PX8OGfm/hu2BdV3OgoZ3cf8IM6H5WLQUcA2jO/N0ytnhpXWd/CLsHAJEKb6pZ4McyuK4BnM5JdRSDY+B/wv56LztT6t8e4kvhAWbnu0LVQIDAQABAoIBAFAG156eACfcJpS89yNIMgHcqy85Zn26NkLyGB7WcpjMdMy1h+vKRVmzfL/bfHI0kt7jVOr6MDuNrx2NvimkAJ6r6IA7YjbXw3SxpmH+h4PLNNVEFg0UolQJXoy5jb2KanAzTmezzbB3Z+sCKWNaDthHP/xDEc1TG6wAglUVSsAkROCCA1thaT3cUX3BLR5NujoEbysy0XTzxN2lG3R/+zrkNLd5ab3syqX9YCRMqPEyZKJ37+KjEVT1SDpViMO6GY/4Y1OYHI7rPwCduRDRw6edXKMMyD8YT6ys/xRI9EwcXhbMrVaNA1mdrqnPRr4jl1sQg8OgQVTIremE0qFVUGUCgYEA14scgrtCxFZhngNPLEj2Zw+4rjU1cOEkSvj19gwH/aY2g3DE6btJZbBynRJL5/uVUHV0BwIxiYeXY33SJHgyqbBMIM8hnFGee3xUrWyMgzLLT8UnbV95ZZineHxeFMTueHmX23dxnLiFEq9Wbkv70nRi2jg1RupbDRQ7YFVLLt8CgYEA0elUaCtSJCOyC9fFdgBbJ8fqLT6Y8B6cLlllbOxRhF3NU0DPf5kfeE9JXaVAYiOQoKSvgMcG78Q4Z6A1wf6jpGYx5XOjPXzkvGr/thRSsUuUjOLoN/r085K2lAGTDz9BMXWCfGTReXLUluQnhnmu9d7k0hrVrHYQ7USfsQzzsEsCgYEAjoRfzJ0O740CLJ2ZivmPWuPNQ/rQpBtpiN0GnLKl0fRF1TEKMlVwmXlKv0qqv+/ccX/HwR6VLI9n7RPzj8OeFA8KtyLd4WMiPBogTy8X1WQPhGYixLG9Lgz6prLs7iSsXSJg428dwvdKnekrZ/B7yFLGTe2eZI5ut74p6G9dL9cCgYEAsAFLw9hnFGRVurZeHBYqWI24nd050URpQje04oK3yxv3uJHEKkIS8AbTBlE0TdVyRDAx8/FtsIa/oKvlx1aikYsa1UCDpF/fTtkMtfgOahhsY0Ey4xVqY/0lV66GRyeLm1PjaDgEqCePd0GwnoHTINeW11CmzudkQ/3hREwO3EcCgYAi9+DYB8D1TbEeMPxUMX2l+A4JmdTJvaMZr3D9HIyuJXyYHCHqWB2QCuq7repW9JNJlRm/Zhqvi9t55jP3EBNvFKcveYTkuTy/ilEhDvTPxiC1jHN7qij8Ar/alr4Z54bphhNRYDB49FoX/rYjc3MbPLaEW8s0GgDuJ7Dknd1DvQ==";

            /** 支付宝公钥，如何获取请参考：https://opensupport.alipay.com/support/helpcenter/207/20160248743 **/
            //String PUBLIC_KEY = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAsL0G9Qg182PpISGvwXefHyPRnxd4lhy6JEC6NWwHRkY39qJYKPHEWqhUbvFLP7NMw+Gzdz2LmukaBTw4nvVqy/5e+294eZ7LwGDah+E7jbOFMW5JoY9Pz+3NcqRuPhjT5YN4CIIAH4O1YcA3iPri0Yc+lUl9/2dq/Yr9NcR9r/5BJ7AFeYCmLqzEJGoA6L+8O/rlLiLXQMqKMM6h/EUcn7lD6in52T/i4T0h1jBLp+//PX8OGfm/hu2BdV3OgoZ3cf8IM6H5WLQUcA2jO/N0ytnhpXWd/CLsHAJEKb6pZ4McyuK4BnM5JdRSDY+B/wv56LztT6t8e4kvhAWbnu0LVQIDAQAB";

        /** 初始化 **/
           // IAopClient client = new DefaultAopClient(ALIPAY_GATEWAY, APP_ID, PRIVATE_KEY, "json", "1.0", "RSA2", PUBLIC_KEY, "utf-8", false);

        /** 实例化具体API对应的request类，类名称和接口名称对应,当前调用接口名称alipay.trade.order.settle(统一收单交易结算接口) **/
         //   AlipayTradeOrderSettleRequest request = new AlipayTradeOrderSettleRequest();

        /** 设置业务参数，具体接口参数传值以文档说明为准：https://opendocs.alipay.com/apis/api_1/alipay.trade.order.settle  **/
          //  request.BizContent = "{" +

                /** 结算请求流水号 开发者自行生成并保证唯一性  **/
          //      "\"out_request_no\":\"" + Util.GetLongTimeStamp(DateTime.Now) + "\"," +

                /** 支付宝订单号  **/
            //    "\"trade_no\":\"" + tradeNo.Trim() + "\"," +

                /** 操作员id  **/
              //  "\"operator_id\":\"\"," +

                /** 分账明细信息，单次传入最多20个，一次分账请求中，有任意一个收入方分账失败，则这次分账请求的全部分账处理均会失败  **/
                //"\"royalty_parameters\":[" +

                    /** 分账收入方信息  **/
                  //  "{" +
                          /** 分账类型.普通分账为：transfer;  **/
                    //      "\"royalty_type\":\"transfer\"," +

                          /** 支出方账户  **/
                          //"\"trans_out\":\"2088***335\"," +

                          /** 支出方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号  **/
                          //"\"trans_out_type\":\"userId\"," +

                          /** 收入方账户  **/
                      //    "\"trans_in\":\"2088002319285895\"," +

                           /** 收入方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号   **/
                        //   "\"trans_in_type\":\"userId\"," +

                          /** 分账的金额，单位为元  **/
                          //"\"amount\":0.01," +

                          /** 设分账描述  **/
                          //"\"desc\":\"" + memo + "\"" +
                      //"}" +
              //"]" +
        //"}";

       // AlipayTradeOrderSettleResponse response = client.Execute(request);

        /** 第三方调用（服务商模式），传值app_auth_token后，会收款至授权app_auth_token对应商家账号，如何获传值app_auth_token请参考文档：https://opensupport.alipay.com/support/helpcenter/79/201602494631 **/
        //AlipayTradeOrderSettleResponse response = client.Execute(request,"","传入获取到的app_auth_token值")

        /**获取接口调用结果，如果调用失败，可根据返回错误信息到该文档寻找排查方案：https://opensupport.alipay.com/support/helpcenter/108 **/
       // Console.WriteLine(response.Body);
       // }
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

        [NonAction]
        public AlipayTradeOrderSettleResponse Settle(string tradeNo, double amount, string login, string name,  string memo, string outTradeNo)
        {
            login = Util.UrlDecode(login);
            memo = Util.UrlDecode(memo);
            name = Util.UrlDecode(name);
            AlipayTradeOrderSettleRequest req = new AlipayTradeOrderSettleRequest();

            /*
            AlipayTradeOrderSettleModel model = new AlipayTradeOrderSettleModel();
            model.OutRequestNo = Util.GetLongTimeStamp(DateTime.Now);
            model.TradeNo = tradeNo;
            List<OpenApiRoyaltyDetailInfoPojo> royaltyParameters = new List<OpenApiRoyaltyDetailInfoPojo>();
            OpenApiRoyaltyDetailInfoPojo royaltyParameters0 = new OpenApiRoyaltyDetailInfoPojo();
            royaltyParameters0.RoyaltyType = "transfer";
            royaltyParameters0.TransInType = "userId";
            royaltyParameters0.TransIn = "2088002319285895";
            //royaltyParameters0.TransInName = "苍杰";
            royaltyParameters0.Amount = amount.ToString();
            royaltyParameters0.Desc = memo;
            //royaltyParameters0.AmountPercentage = 30;
            royaltyParameters.Add(royaltyParameters0);
            model.RoyaltyParameters = royaltyParameters;
            //req.SetBizModel(model);
            */

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

            /*
            RoyaltyInfo rInfo = new RoyaltyInfo();
            rInfo.RoyaltyType = "ROYALTY";
            RoyaltyDetailInfos dtl = new RoyaltyDetailInfos();
            dtl.AmountPercentage = "30";
            dtl.BatchNo = Util.GetLongTimeStamp(DateTime.Now).ToString();
            dtl.TransOutType = "userId";
            dtl.TransOut = "2088640272285174";
            dtl.TransIn = "2088002319285895";
            rInfo.RoyaltyDetailInfos = new List<RoyaltyDetailInfos>();
            rInfo.RoyaltyDetailInfos.Add(dtl);

            model.RoyaltyInfo = rInfo;
            */

            model.ExtendParams = new ExtendParams{ RoyaltyFreeze = "true" };
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
        public async Task<string> GetPaymentQrCodeUrl(int paymentId)
        {

            AlipayMchId mch = await GetMch(null);

            client = GetClient(mch.app_id.Trim());

            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            

            
            
            string notify = "https://mini.snowmeet.top/core/Ali/Callback";
            
            AlipayTradePrecreateRequest request = new AlipayTradePrecreateRequest();
            request.SetNotifyUrl(notify);
            

            /*
            RoyaltyInfo rInfo = new RoyaltyInfo();
            rInfo.RoyaltyType = "ROYALTY";
            RoyaltyDetailInfos dtl = new RoyaltyDetailInfos();
            dtl.AmountPercentage = "30";
            dtl.BatchNo = Util.GetLongTimeStamp(DateTime.Now).ToString();
            dtl.TransOutType = "userId";
            dtl.TransOut = "2088640272285174";
            dtl.TransIn = "2088002319285895";
            rInfo.RoyaltyDetailInfos = new List<RoyaltyDetailInfos>();
            rInfo.RoyaltyDetailInfos.Add(dtl);
            */



            AlipayTradePrecreateModel model = new AlipayTradePrecreateModel();
            model.OutTradeNo = payment.out_trade_no.Trim();

            ////////////////////////////////////
            //////等待从订单获取//////////////////
            model.Subject = "test";
            model.Body = "test1";
            /////////////////////////////////////



            model.TotalAmount = payment.amount.ToString();
            //model.RoyaltyInfo = rInfo;
            model.ExtendParams = new ExtendParams{ RoyaltyFreeze = "true" };
            request.SetBizModel(model);
            AlipayTradePrecreateResponse response = client.CertificateExecute(request);
            string responseStr = response.Body.Trim();
            Console.WriteLine(responseStr);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(responseStr);
            payment.notify = notify;
            payment.ali_qr_code = respObj.alipay_trade_precreate_response.qr_code.Trim();
            payment.mch_id = mch.id;
            payment.pay_method = "支付宝";
            _db.OrderPayment.Entry(payment).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return respObj.alipay_trade_precreate_response.qr_code.Trim();
        }

        [HttpPost]
        public async Task<ActionResult<string>>  Callback()
        {
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            StreamReader sr = new StreamReader(Request.Body);
            string postStr = await sr.ReadToEndAsync();
            sr.Close();
            System.IO.File.AppendAllText( certPath + "/alipay_callback_" + DateTime.Now.ToString("yyyyMMdd") + ".txt", DateTime.Now.ToString() + "\t" + postStr + "\r\n");
            string[] postArr = postStr.Split('&');
            string outTradeNo = "";
            string tradeNo = "";
            for(int i = 0; i < postArr.Length; i++)
            {
                string field = postArr[i].Trim();
                if (field.StartsWith("out_trade_no"))
                {
                    outTradeNo = field.Split('=')[1];
                    TenpayController tenHelper = new TenpayController(_db, _oriConfig, _http);
                    await tenHelper.SetTenpayPaymentSuccess(outTradeNo);
                    //break;
                }
                if (field.StartsWith("trade_no"))
                {
                    tradeNo = field.Split('=')[1];
                    
                    //break;
                }
                if (!tradeNo.Trim().Equals("") && !outTradeNo.Trim().Equals(""))
                {
                    OrderPayment payment = await _db.OrderPayment.Where(p => p.out_trade_no.Trim().Equals(outTradeNo.Trim()))
                        .OrderByDescending(p => p.id).FirstAsync();
                    if (payment != null)
                    {
                        payment.ali_trade_no = tradeNo.Trim();
                        _db.OrderPayment.Entry(payment).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                    }
                    break;
                }

            }
            return Ok("success");
        }

        [NonAction]
        public async Task<OrderPaymentRefund> Refund(int refundId)
        {
            OrderPaymentRefund refund = await _db.OrderPaymentRefund.FindAsync(refundId);
            if (refund == null)
            {
                return null;
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(refund.payment_id);
            if (payment == null)
            {
                return null;
            }
            var refunds = await _db.OrderPaymentRefund.Where(r => r.payment_id == payment.id)
                .AsNoTracking().ToListAsync();
            //string outRefundNo = payment.out_trade_no.Trim() + "_REFND_" + DateTime.Now.ToString("yyyyMMdd") 
            //    + "_" + refunds.Count.ToString().PadLeft(2, '0');
            //refund.out_refund_no = outRefundNo;
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
            catch(Exception ex)
            {
                refund.memo = ex.ToString().Length > 500? ex.ToString().Substring(0, 500) : ex.ToString();
            }
            
            _db.OrderPaymentRefund.Entry(refund).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return refund;
        }


        [HttpGet]
        public AlipayTradeRefundResponse Refund(string outTradeNo, string outRefundNo, double amount, string reason="")
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

        [HttpGet("{appId}")]
        public async Task GetBill(string appId, DateTime billDate)
        {
            /*
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            string privateKey = await System.IO.File.ReadAllTextAsync(certPath + "/private_key_" + appId + ".txt");
            
            string publicKey = await System.IO.File.ReadAllTextAsync(certPath + "/alipayCertPublicKey_RSA2.crt");


            
            CertParams certParams = new CertParams
            {
                AlipayPublicCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayCertPublicKey_RSA2.crt",
                AppCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/appCertPublicKey_" + appId + ".crt",
                RootCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayRootCert.crt"
            };
            */
            //IAopClient alipayClient = new DefaultAopClient("https://openapi.alipay.com/gateway.do", appId, privateKey, "json", "1.0", "RSA2", "utf-8", false, certParams);
            
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
                    
                    string fileName =  entry.FullName.Trim();
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

            if(!response.IsError){
             	Console.WriteLine("调用成功");
             }
             else{
             	Console.WriteLine("调用失败");
             }

        }
        [HttpGet]
        public void GetFlow(DateTime startDate, DateTime endDate)
        {
            IAopClient client = GetClient(appId);
            AlipayDataBillAccountlogQueryRequest req = new AlipayDataBillAccountlogQueryRequest();
            req.BizContent="{" +
                "  \"start_time\":\"" + startDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"end_time\":\"" + endDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"page_no\":\"1\"," +
                "  \"page_size\":\"2000\" }";
            AlipayDataBillAccountlogQueryResponse response=client.CertificateExecute(req);
            Console.WriteLine(response.Body);
        }

        [HttpGet]
        public void GetBalance(string type, DateTime startDate, DateTime endDate)
        {
            
            IAopClient client = GetClient(appId);
            AlipayDataBillTransferQueryRequest  req = new AlipayDataBillTransferQueryRequest() ;
            req.BizContent="{" +
                "  \"start_time\":\"" + startDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"end_time\":\"" + endDate.ToString("yyyy-MM-dd HH:mm:ss") + "\"," +
                "  \"type\":\"" + type + "\"," +
                "  \"page_no\":\"1\"," +
                "  \"page_size\":\"2000\" }";
            AlipayDataBillTransferQueryResponse response=client.CertificateExecute(req);
            Console.WriteLine(response.Body);
        }

        [HttpGet]
        public async Task DataAll(DateTime billDate, string type = "signcustomer")
        {
            IAopClient client = GetClient(appId);
            AlipayDataDataserviceBillDownloadurlQueryModel model = new AlipayDataDataserviceBillDownloadurlQueryModel();
            //model.setSmid("2088123412341234");
            model.BillType =  type;
            model.BillDate =  billDate.ToString("yyyy-MM-dd");
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
            string tempFileName = billDate.ToString("yyyyMMdd") + "_" + Util.GetLongTimeStamp(DateTime.Now).ToString() + ".zip";
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
                    
                    string fileName =  entry.FullName.Trim();
                    fileName = Util.UrlDecode(fileName);
                   
                    Console.WriteLine("文件名：{0}", entry.FullName);
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream, Encoding.GetEncoding("GB2312")))
                    {
                        var str = reader.ReadToEnd();
                        if (entry.FullName.Trim().EndsWith(billDate.ToString("yyyyMMdd") + "_TRANSFER_DETAILS.csv"))
                        {
                            await DealTrans(str.Trim());
                        }
                        if (entry.FullName.Trim().EndsWith(billDate.ToString("yyyyMMdd") + "_账务明细.csv"))
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
            for(int i = 0; i < lineArr.Length; i++)
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
                    trans_date =  DateTime.Parse(fieldsArr[4].Trim()),
                    receiver_ali_account = fieldsArr[5].Trim(),
                    income = double.Parse(fieldsArr[6].Trim()),
                    outcome = double.Parse(fieldsArr[7].Trim()),
                    remainder = double.Parse(fieldsArr[8].Trim()),
                    trans_channel = fieldsArr[9].Trim(),
                    biz_type =  fieldsArr[10].Trim(),
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

            if(!response.IsError)
            {
                Console.WriteLine("调用成功");
            }
            else
            {
                Console.WriteLine("调用失败");
            }

        }

        /*
        [NonAction]
        private async Task DealBalance(string content)
        {


        }
        */
        
    }
}
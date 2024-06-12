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
            //public AlipayTradeOrderOnsettleQueryResponse alipay_trade_order_onsettle_query_response {get; set;}
            public string sign { get; set; }
            public string alipay_cert_sn { get; set; }
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

        [HttpGet]
        public async Task Settle(string tradeNo, double amount, string login, string name,  string memo)
        {
            login = Util.UrlDecode(login);
            memo = Util.UrlDecode(memo);
            name = Util.UrlDecode(name);
            AlipayTradeOrderSettleRequest req = new AlipayTradeOrderSettleRequest();
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
            
            req.BizContent = "{" +

                /** 结算请求流水号 开发者自行生成并保证唯一性  **/
                "\"out_request_no\":\"" + Util.GetLongTimeStamp(DateTime.Now) + "\"," +

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
                          "\"trans_in\":\"13501177897\"," +

                           /** 收入方账户类型。userId表示是支付宝账号对应的支付宝唯一用户号;loginName表示是支付宝登录号   **/
                           "\"trans_in_type\":\"loginName\"," +

                          /** 分账的金额，单位为元  **/
                          "\"amount\":0.01," +

                          /** 设分账描述  **/
                          "\"desc\":\"" + memo + "\"" +
                      "}" +
              "]" +
        "}";



            AlipayTradeOrderSettleResponse response = client.CertificateExecute(req);

            if(!response.IsError)
            {
                Console.WriteLine("调用成功");
            }
            else
            {
                Console.WriteLine("调用失败");
            }

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
            string outRefundNo = payment.out_trade_no.Trim() + "_" + DateTime.Now.ToString("yyyyMMdd") 
                + "_" + refunds.Count.ToString().PadLeft(2, '0');
            refund.out_refund_no = outRefundNo;
            try
            {
                AlipayTradeRefundResponse res = Refund(payment.out_trade_no.Trim(), outRefundNo, refund.amount, refund.reason.Trim());
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
    }
}
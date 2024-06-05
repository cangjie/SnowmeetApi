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

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class AliController : ControllerBase
    {
        public class AlipayRequestResult
        {
            public AlipayTradeResponse alipay_trade_precreate_response { get; set; }
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

        public ApplicationDBContext _db;
        public IConfiguration _oriConfig;
        public IHttpContextAccessor _http;

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

        [HttpGet]
        public async Task BindRoyaltiRelation(string login, string name, string memo)
        {
            login = Util.UrlDecode(login);
            name = Util.UrlDecode(name);
            memo = Util.UrlDecode(memo);
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
             }
             else{
             	Console.WriteLine("调用失败");
             } 
        }

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
            royaltyParameters0.TransInType = "loginName";
            royaltyParameters0.TransIn = login;
            royaltyParameters0.TransInName = name;
            royaltyParameters0.Amount = amount.ToString();
            royaltyParameters0.Desc = memo;
            royaltyParameters.Add(royaltyParameters0);
            model.RoyaltyParameters = royaltyParameters;
            req.SetBizModel(model);
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

        [NonAction]
        public async Task<string> GetPaymentQrCodeUrl(int paymentId)
        {
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            

            
            
            string notify = "https://mini.snowmeet.top/core/Ali/Callback";
            
            AlipayTradePrecreateRequest request = new AlipayTradePrecreateRequest();
            request.SetNotifyUrl(notify);
            AlipayTradePrecreateModel model = new AlipayTradePrecreateModel();
            model.OutTradeNo = payment.out_trade_no.Trim();
            model.Subject = "test";
            model.Body = "test1";
            model.TotalAmount = payment.amount.ToString();
            request.SetBizModel(model);
            AlipayTradePrecreateResponse response = client.CertificateExecute(request);
            string responseStr = response.Body.Trim();
            Console.WriteLine(responseStr);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(responseStr);
            payment.notify = notify;
            payment.ali_qr_code = respObj.alipay_trade_precreate_response.qr_code.Trim();
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
            for(int i = 0; i < postArr.Length; i++)
            {
                string field = postArr[i].Trim();
                if (field.StartsWith("out_trade_no"))
                {
                    string outTradeNo = field.Split('=')[1];
                    TenpayController tenHelper = new TenpayController(_db, _oriConfig, _http);
                    await tenHelper.SetTenpayPaymentSuccess(outTradeNo);
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
                if (res.FundChange.ToUpper() == "Y")
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
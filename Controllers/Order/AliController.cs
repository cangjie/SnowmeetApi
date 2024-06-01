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

        public AliController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _oriConfig = config;
            _http = httpContextAccessor;
        }

        [NonAction]
        public async Task<string> GetPaymentQrCodeUrl(int paymentId)
        {
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            string appCertPublicKeyPath = certPath + "/appCertPublicKey_" + appId + ".crt";
            

            string privateKey = System.IO.File.OpenText(certPath + "/private_key_" + appId + ".txt").ReadToEnd().Trim();

            CertParams certParams = new CertParams
            {
                AlipayPublicCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayCertPublicKey_RSA2.crt",
                AppCertPath = appCertPublicKeyPath,
                RootCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayRootCert.crt"
            };
            string notify = "https://mini.snowmeet.top/core/Ali/Callback";
            IAopClient client = new DefaultAopClient("https://openapi.alipay.com/gateway.do", appId, privateKey, "json", "1.0", "RSA2", "utf-8", false, certParams);
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
                }

            }
            return Ok("success");
        }

    }
}
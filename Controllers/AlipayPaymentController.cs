using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
/*
using AlipaySDKNet.OpenAPI.Api;
using AlipaySDKNet.OpenAPI.Client;
using AlipaySDKNet.OpenAPI.Model;
using AlipaySDKNet.OpenAPI.Util;
using AlipaySDKNet.OpenAPI.Util.Model;
*/
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
/*
using Aop.Api.Request;
using Aop.Api;
using Aop.Api.Response;
*/


using Newtonsoft.Json;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Pipelines;
using Aop.Api;
using Aop.Api.Request;
using Aop.Api.Response;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class AlipayPaymentController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        //private readonly Order.OrderPaymentController payCtrl;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        private readonly string appId = "2021004108630187";
        private readonly string privateKey = "MIIEpAIBAAKCAQEA2CUlcPcrr6tFYccgpbszuZ8iaiQsPVzrUa32oCXT/Pws5b5Fn4XCFnltT14cYRLrVH2kI6s8uwyyACy7djZ6pzYjo6dUXknNJr6O+danMfOHxPw9AvMbOhgJ90ALu2pbXfOnm0YNRRdvgDajRukpzBukD06lgh4YFJ65Xo7jJrgVPtvIKvYjX0PVBCZAZfov/+jVL5qV6LFqU7/EXeS+YnrvHXLNHtBiIybAH0g+sk0w0+htzFnevb+D0T91HE7dGda1C3eGeVXsVsQcPH4R9/Vv9XCIaVnMQ7O8F4zu8mvJXVEkwaP6qPRpHlvTez6HQtNV4cUx2b7qsH7+VMppeQIDAQABAoIBAQC1K02axlKjyBc7wcCnqaXNbIlYFkHOnVfQ+tPBoHNzlZu8ZiPNdjwcwmLRJ7z50PXXuAggraMV1apNYzEuALQF++cbgWHCNnHyi78jwrwZrlqaziIFmuezZfrUd6ZdzOQQd+6Aw9LTmrUm7qUlBsK2BmUZP36S9c6RWgleLL5Q8Ggb/0iYHGeiqlDlUaGDSSl++bwnsNJjsrh13qgMm/C/wY1pL6QbUGqOOjo8I7fv/MA5akeZ+6zO1REcbMKiVYs1t+i3+L8AHX8JICvu8aQS7cUmxzu+byRTkhOO6It4irKGqv2Z4gLHKPc3V1zFwXVIOnLTv/+XJvN+RGfX4WQBAoGBAPqq4eVskCu9+R+od/hvASVAM7bxBM3DFnpfHodZg9RWaNIjVb72FbF1REyvVKEW0lAmWisMMs4Z+XY7WU5LpWT02JKqHY3T4j/4KURvyw3xBIjWhVs7JEfFlLO/llErX62vOTyxSZOJW/xgCDQgh1e66lr9PkvzoUbpEzO9vfuZAoGBANy+QdtTzZhXJYVu6CFavzHR+r9Hh0AoHdqbzvmYXw/7xIEa3HnffEVGWPg+usv886c3MN0K9pW1McfvWX1COTkowyklyTCMHbj6Obxh6jxSKx52yqeIpJGyV75HBJrTwCF9YHT4BzQ1cbIksTg1DMWkD0FAybFQ4YJslTPMv4jhAoGABFlMm/9bLPcZyFvS4QOEAJJxkz3xOGSnEi5uSCjcaaWqIeMtDKgWTkLbkX0FOdo8gdl4fQC0LPE0a8Gx1fLoBq1cyIadBqXjafqzNJW/7xj8XCdknuWSxo/9+XRcdkILYecFVjE5No8Ogn1kBwt9bZ83i6aTGxw58xH+HEqxbhkCgYBpc8pqSKKTAC7Ai7cBGCT2W+V5s2X9VCzO3lgGDLB9Jj09n+NrpUPspCqkjPMXuAN+AnOpZS9fXWwmo0UQ/a3wjHSPF6oBMy6Py5oBUJVhs689omo1lqVnpNcd4zdj73x9gzOtLT/jxRRHkhfHTjCHylQvTBAOUSEp+U1drZZigQKBgQDWADai7Y5iMrnNCbAPTL8Xqn147tIg5YDW1jx72Wua1nCBOLzGNTQUcejMpFDD6LaHxkpgN06FiBTMHUa+39DwoFZcocREaOMZL86FXvAGE3mPxI6SUVTIWxTQgBdV5e9ZNzw/Lnc/k0g8bIVyQ41acNg1NgkBK/SwYOyzG1Ztzg==";
        //private readonly string privateKey = "9fQQanADekz/FUt6//ts0w==";
        //private readonly string publicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAoIC17Lr5tp14L27r7w57bYUqnWlMtH3lehlMLP8f8+5bY7jZ4WNFl0Knl+lWKaU9FizeIf98eJO3A16wHLTVrDFQRot/S9JnygtsptR8yPiGEI1DCVeTFD3+B0G7mjcI2jervvlDzLKL0+VotBXoaf85BFB2GfH//J2XdYWRNcub+L2VuITk5nojjfRRmD99GAiv4aR0GdovIghKKWzp4y92CnfI1HdhsgW4fMJxD2D1BtAC52rIcU4G8ctzwqWFTZRObtgrsXP4S+EuFmPVz4U0PwIBDotTQpV4d0bbb5WaNAbTIvTKpH0LdYfcdMDxEyCADlBerTPK/SFQazciSQIDAQAB";

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

        public AlipayPaymentController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }

        [HttpGet]
        public void QRCodeTest(string qrCodeUrl)
        {
            /*
            ThoughtWorks.QRCode.Codec.QRCodeEncoder coder = new ThoughtWorks.QRCode.Codec.QRCodeEncoder();
            System.Drawing.Bitmap bmp = coder.Encode("https:/\/qr.alipay.com\/bax07611fimfgrbn8vyk5511", System.Text.Encoding.UTF8);
            bmp.Save(Util.workingPath + "/wwwroot/images/alipayqr.bmp");
            */

            byte[] bArr = QRCoder.BitmapByteQRCodeHelper.GetQRCode(qrCodeUrl, QRCoder.QRCodeGenerator.ECCLevel.Q, 5);
            Response.ContentType = "image/jpeg";
            Response.ContentLength = bArr.Length;
            PipeWriter pw = Response.BodyWriter;
            Stream sOut = pw.AsStream();
            for (int k = 0; k < bArr.Length; k++)
            {
                sOut.WriteByte(bArr[k]);
            }
            sOut.Close();
        }

        [HttpGet]
        public async Task  Test()
        {
            CertParams certParams = new CertParams
            {
                AlipayPublicCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayCertPublicKey_RSA2.crt",
                AppCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/appCertPublicKey_2021004108630187.crt",
                RootCertPath = Util.workingPath + "/AlipayCertificate/" + appId + "/alipayRootCert.crt"
            };
            IAopClient client = new DefaultAopClient("https://openapi.alipay.com/gateway.do", appId, privateKey, "json", "1.0", "RSA2", "utf-8", false, certParams);
            AlipayTradePrecreateRequest request = new AlipayTradePrecreateRequest();
            request.SetNotifyUrl("https://mini.snowmeet.top/core/AlipayPayment/callback");
            string outTradeNo = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0') + DateTime.Now.Day.ToString().PadLeft(2, '0') + DateTime.Now.Hour.ToString().PadLeft(2, '0')
                + DateTime.Now.Minute.ToString().PadLeft(2, '0') + DateTime.Now.Second.ToString().PadLeft(2, '0');
            Dictionary<string, object> bizContent = new Dictionary<string, object>();
            bizContent.Add("out_trade_no", outTradeNo);
            bizContent.Add("total_amount", 0.02);
            bizContent.Add("subject", "测试商品");


            string Contentjson = JsonConvert.SerializeObject(bizContent);
            request.BizContent = Contentjson;
            //AlipayTradePrecreateResponse response = client.Execute(request);
            AlipayTradePrecreateResponse response = client.CertificateExecute(request);
            string responseStr = response.Body.Trim();
            Console.WriteLine(responseStr);
            AlipayRequestResult respObj = JsonConvert.DeserializeObject<AlipayRequestResult>(responseStr);
            QRCodeTest(respObj.alipay_trade_precreate_response.qr_code.Trim());
        }

        [HttpPost]
        public async Task  callback()
        {
            StreamReader sr = new StreamReader(Request.Body);
            string postStr = await sr.ReadToEndAsync();
            sr.Close();
            System.IO.File.AppendAllText("alipay_callback.txt", DateTime.Now.ToString() + "\t" + postStr + "\r\n");
            await Response.WriteAsync("success");
        }


	}
}


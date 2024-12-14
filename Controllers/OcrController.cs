using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using System.Runtime.CompilerServices;
using SnowmeetApi.Models.Users;
using System.Threading.Tasks;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Ocr.V20181119.Models;
using TencentCloud.Ocr.V20181119;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OcrController : ControllerBase
    {
        public class DetectedTextClass
        { 
            public string DetectedText { get; set; }
        }
        public class OcrResult
        { 
            public DetectedTextClass[] TextDetections { get; set; }
        }

        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        private MemberController _memberHelper;
        private  string tcAppId = "";
        private  string tcSecret = "";



        public OcrController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            tcAppId = _config.GetSection("Settings").GetSection("TencentCloudId").Value.Trim();
            tcSecret = _config.GetSection("Settings").GetSection("TencentCloudSecret").Value.Trim();
            _memberHelper = new MemberController(context, config);

        }

        [HttpPost]
        public async Task<ActionResult<string[]>> GeneralBasicOCR([FromQuery] string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey);
            if (member.is_staff != 1 && member.is_manager != 1 && member.is_admin != 1)
            {
                return BadRequest();
            }
            StreamReader sr = new StreamReader(_http.HttpContext.Request.Body);
            string img = await sr.ReadToEndAsync();
            sr.Close();
            /*
            Credential cred = new Credential
            {
                SecretId = "AKIDwiJhroX4lETWMyMWS9cu15vXu40iCRSE",
                SecretKey = "b0TjndO8raHuRvUt5BB2hHk3AFD1c1WD"
            };
            */

            Credential cred = new Credential
            {
                SecretId = tcAppId,
                SecretKey = tcSecret
            };


            ClientProfile clientProfile = new ClientProfile();
            // 实例化一个http选项，可选的，没有特殊需求可以跳过
            HttpProfile httpProfile = new HttpProfile();
            httpProfile.Endpoint = ("ocr.tencentcloudapi.com");
            clientProfile.HttpProfile = httpProfile;

            // 实例化要请求产品的client对象,clientProfile是可选的
            OcrClient client = new OcrClient(cred, "", clientProfile);
            // 实例化一个请求对象,每个接口都会对应一个request对象
            GeneralBasicOCRRequest req = new GeneralBasicOCRRequest();
            req.ImageBase64 = img;
            GeneralBasicOCRResponse resp = client.GeneralBasicOCRSync(req);
            string json = AbstractModel.ToJsonString(resp);
            OcrResult r = JsonConvert.DeserializeObject<OcrResult>(json);
            string[] ret = new string[r.TextDetections.Length];
            //Regex rgx = new Regex(@"\d{5,9}");
            for (int i = 0; i < ret.Length; i++)
            {
                string s = r.TextDetections[i].DetectedText.Trim();
                if (!s.ToUpper().StartsWith("N") && isCardNo(s))
                {
                    s = "NO." + s; 
                }
                ret[i] = s;
            }
            return Ok(ret);
        }

        [NonAction]
        public bool isCardNo(string s)
        {
            Regex r = new Regex(@"\d{5,9}");
            Match m = r.Match(s);
            if (m.Success)
            {
                if (m.Value.Trim().Equals(s.Trim()))
                {
                    return true;
                }
            }
            return false;

        }

        [NonAction]
        public ActionResult<string> testReg(string s)
        {
            Regex r = new Regex(@"\d{5,9}");
            Match m = r.Match(s);
            if (m.Success)
            {
                if (m.Value.Trim().Equals(s.Trim()))
                {
                    return Ok("No." + s.Trim());
                }
            }
            return Ok(s);
        }
    }

    
}

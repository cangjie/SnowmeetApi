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
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OcrController : ControllerBase

    {
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
        public async Task<ActionResult<string>> GeneralBasicOCR([FromQuery] string sessionKey)
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
            Credential cred = new Credential
            {
                SecretId = "AKIDwiJhroX4lETWMyMWS9cu15vXu40iCRSE",
                SecretKey = "b0TjndO8raHuRvUt5BB2hHk3AFD1c1WD"
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

            return Ok(AbstractModel.ToJsonString(resp));
        }

    }
}

using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System.Threading.Tasks;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Controllers.Tiktok
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class TiktokHelperController : ControllerBase
    {
        private readonly ApplicationDBContext _db;

        private readonly IConfiguration _config;

        private readonly Settings _settings;

        public TiktokHelperController(ApplicationDBContext db, IConfiguration config)
		{
            _db = db;
            _config = config;
            _settings = Settings.GetSettings(_config);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<string>> Login(string code)
        {
            string postData = "{ \"appid\": \"" + _settings.tiktokAppId.Trim() + "\", \"secret\": \"" + _settings.tiktokAppSecret.Trim() + "\", "
                + " \"code\": \"" + code.Trim() + "\"}";
            string loginUrl = "https://" + _settings.tiktokDomain + "/api/apps/v2/jscode2session";
            string retStr = Util.GetWebContent(loginUrl, postData, "application/json");
            Code2Session codeObj = JsonConvert.DeserializeObject<Code2Session>(retStr);

            try
            {
                MiniSession session = new MiniSession()
                {
                    session_key = codeObj.data.session_key,
                    //open_id = codeObj.data.openid,
                    session_type = "tiktok"
                };
                await _db.miniSession.AddAsync(session);
                await _db.SaveChangesAsync();
                return Ok(session.session_key);
            }
            catch
            {
                return BadRequest();
            }
        }

        [HttpGet]
        public void RefreshAccessToken()
        {
            GetAccessToken();
        }


        [NonAction]
        public string GetAccessToken()
        {
            string tokenFilePath = $"{Environment.CurrentDirectory}";
            tokenFilePath = tokenFilePath + "/access_token.tiktok";
            string token = "";
            string tokenTime = Util.GetLongTimeStamp(DateTime.Parse("1970-1-1"));
            string nowTime = Util.GetLongTimeStamp(DateTime.Now);
            bool fileExists = false;
            if (System.IO.File.Exists(tokenFilePath))
            {
                fileExists = true;
                using (StreamReader sr = new StreamReader(tokenFilePath))
                {
                    try
                    {
                        token = sr.ReadLine();
                    }
                    catch
                    {

                    }
                    try
                    {
                        tokenTime = sr.ReadLine();
                    }
                    catch
                    {

                    }
                    sr.Close();
                }
                long timeDiff = long.Parse(nowTime) - long.Parse(tokenTime);
                TimeSpan ts = new TimeSpan(0, 0, 0, 0, (int)timeDiff);
                //TimeSpan ts = new TimeSpan()
                if (ts.TotalSeconds > 3600)
                {
                    token = "";
                    if (fileExists)
                    {
                        System.IO.File.Delete(tokenFilePath);
                    }
                }
                else
                {
                    return token.Trim();
                    //return "";
                }
            }
            string getTokenUrl = "https://" + _settings.tiktokDomain + "/api/apps/v2/token";
            string postData = "{ \"appid\": \"" + _settings.tiktokAppId + "\", \"secret\": \"" + _settings.tiktokAppSecret
                + "\",  \"grant_type\": \"client_credential\"}";
            try
            {
                string ret = Util.GetWebContent(getTokenUrl, postData, "application/json");
                
                AccessToken at = JsonConvert.DeserializeObject<AccessToken>(ret);
                if (!at.data.access_token.Trim().Equals(""))
                {
                    System.IO.File.WriteAllText(tokenFilePath, at.data.access_token.Trim() + "\r\n" + nowTime);
                    return at.data.access_token.Trim().Trim();
                    //return "";
                }
                else
                {
                    return "";
                }
            }
            catch
            {
                return "";
            }

        }
        public class Code2Session
        {
            public class DataStruct
            {
                public string openid { get; set; } = "";
                public string session_key { get; set; } = "";
                public string anonymous_openid { get; set; } = "";
                public string unionid { get; set; } = "";
            }

            public int err_no { get; set; }
            public string err_tips { get; set; }
            public DataStruct data { get; set; }
        }

        public class AccessToken
        {
            public struct DataStruct
            {
                public string access_token { get; set; }
                public int expires_in { get; set; }
            }
            public int err_no { get; set; }
            public string err_tips { get; set; }
            public DataStruct data { get; set; }
            

        }
    }
}


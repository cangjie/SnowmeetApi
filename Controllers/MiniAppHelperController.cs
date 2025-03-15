using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;


using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.RegularExpressions;

using System.Xml;
using System.Security.Cryptography;
using SnowmeetApi.Data;
using SnowmeetApi;
using SnowmeetApi.Controllers.User;
using System.Net.Http;

namespace LuqinMiniAppBase.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MiniAppHelperController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly Settings _settings;
        public MemberController _memberHelper;
        public MiniAppHelperController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _settings = Settings.GetSettings(_config);
            _memberHelper = new MemberController(db, config);
        }
        [HttpGet]
        public ActionResult<string> PushMessage(string signature,
            string timestamp, string nonce, string echostr)
        {
            return echostr.Trim();
        }
        [HttpPost]
        public async Task<ActionResult<string>> PushMessage([FromQuery] string signature,
            [FromQuery] string timestamp, [FromQuery] string nonce)
        {
            string[] validStringArr = new string[] { _settings.token.Trim(), timestamp.Trim(), nonce.Trim() };
            Array.Sort(validStringArr);
            string validString = String.Join("", validStringArr);
            SHA1 sha = SHA1.Create();
            ASCIIEncoding enc = new ASCIIEncoding();
            byte[] bArr = enc.GetBytes(validString);
            bArr = sha.ComputeHash(bArr);
            string validResult = "";
            for (int i = 0; i < bArr.Length; i++)
            {
                validResult = validResult + bArr[i].ToString("x").PadLeft(2, '0');
            }
            if (validResult != signature)
            {
                return NoContent();
            }
            string body = "";
            var stream = Request.Body;
            if (stream != null)
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
                {
                    body = await reader.ReadToEndAsync();
                    string path = $"{Environment.CurrentDirectory}";
                    string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                        + DateTime.Now.Day.ToString().PadLeft(2, '0');
                    if (path.StartsWith("/"))
                    {
                       path = path + "/";
                    }
                    else
                    {
                        path = path + "\\";
                    }
                    path = path + "wechat_post_" + dateStr + ".txt";
                    using (StreamWriter fw = new StreamWriter(path, true))
                    {
                        fw.WriteLine(body.Trim());
                        fw.Close();
                    }
                }
            }
            try
            {
                XmlDocument xmlD = new XmlDocument();
                xmlD.LoadXml(body);
                XmlNode root = xmlD.SelectSingleNode("//xml");
                string eventStr = "";
                string eventKey = "";
                string content = "";
                string msgId = "";
                string msgType = root.SelectSingleNode("MsgType").InnerText.Trim();
                if (msgType.Trim().Equals("event"))
                {
                    eventStr = root.SelectSingleNode("Event").InnerText.Trim();
                    eventKey = root.SelectSingleNode("EventKey").InnerText.Trim();
                }
                else
                {
                    content = root.SelectSingleNode("Content").InnerText.Trim();
                    msgId = root.SelectSingleNode("MsgId").InnerText.Trim();
                    msgType = root.SelectSingleNode("MsgType").InnerText.Trim();
                }
                OAReceive msg = new OAReceive()
                {
                    id = 0,
                    ToUserName = root.SelectSingleNode("ToUserName").InnerText.Trim(),
                    FromUserName = root.SelectSingleNode("FromUserName").InnerText.Trim(),
                    CreateTime = root.SelectSingleNode("CreateTime").InnerText.Trim(),
                    MsgType = msgType,
                    Event = eventStr,
                    EventKey = eventKey,
                    MsgId = msgId,
                    Content = content

                };
                await _db.oAReceive.AddAsync(msg);
                await _db.SaveChangesAsync();
                return "success";
            }
            catch
            {
            }
            return "success";
        }
        [HttpGet]
        public async Task<ActionResult<Code2Session>> MemberLogin(string code, string openIdType)
        {
            string appId = _settings.appId;
            string appSecret = _settings.appSecret;
            string checkUrl = "https://api.weixin.qq.com/sns/jscode2session?appid=" + appId.Trim()
                + "&secret=" + appSecret.Trim() + "&js_code=" + code.Trim()
                + "&grant_type=authorization_code";
            string jsonResult = Util.GetWebContent(checkUrl);
            Code2Session sessionObj = JsonConvert.DeserializeObject<Code2Session>(jsonResult);
            if (!sessionObj.errcode.ToString().Equals(""))
            {
                return BadRequest();
            }
            Member member = new Member()
            {
                id = 0
            };
            if (sessionObj.unionid != null && !sessionObj.unionid.Trim().Equals(""))
            {
                member = await _memberHelper.GetMember(sessionObj.unionid.Trim(), "wechat_unionid");
            }
            if ((member == null || member.id == 0) && sessionObj.openid != null && !sessionObj.openid.Trim().Equals(""))
            {
                member = await _memberHelper.GetMember(sessionObj.openid.Trim(), openIdType.Trim());
            }
            if (member == null)
            {
                member = new Member()
                {
                    id = 0,
                    real_name = "",
                    gender = ""
                };
                if (sessionObj.unionid != null && !sessionObj.unionid.Trim().Equals(""))
                {
                    MemberSocialAccount msa = new MemberSocialAccount()
                    {
                        type = "wechat_unionid",
                        num = sessionObj.unionid.Trim(),
                        valid = 1,
                        memo = ""
                    };
                    member.memberSocialAccounts.Add(msa);
                }
                if (sessionObj.openid != null && !sessionObj.openid.Trim().Equals(""))
                {
                    MemberSocialAccount msa = new MemberSocialAccount()
                    {
                        type = openIdType.Trim(),
                        num = sessionObj.openid.Trim(),
                        valid = 1,
                        memo = ""
                    };
                    member.memberSocialAccounts.Add(msa);
                }
                member = await _memberHelper.CreateMember(member);
            }
            bool existsUnionid = false;
            bool existsOpneId = false;
            foreach(MemberSocialAccount msa in member.memberSocialAccounts)
            {
                if (msa.type.Trim().Equals("wechat_unionid"))
                {
                    existsUnionid = true;
                    
                }
                if (msa.type.Trim().Equals(openIdType.Trim()))
                {
                    existsOpneId = true;
                }
            }
            if (!existsUnionid && sessionObj.unionid != null && !sessionObj.unionid.Trim().Equals(""))
            {
                MemberSocialAccount newMsa = new MemberSocialAccount()
                {
                    member_id = member.id,
                    type = "wechat_unionid",
                    num = sessionObj.unionid,
                    valid = 1,
                    memo = ""
                };
                await _db.memberSocialAccount.AddAsync(newMsa);
                await _db.SaveChangesAsync();
            }
            if (!existsOpneId && sessionObj.openid != null && !sessionObj.openid.Trim().Equals(""))
            {
                MemberSocialAccount newMsa = new MemberSocialAccount()
                {
                    member_id = member.id,
                    type = openIdType.Trim(),
                    num = sessionObj.openid,
                    valid = 1,
                    memo = ""
                };
                await _db.memberSocialAccount.AddAsync(newMsa);
                await _db.SaveChangesAsync();
            }
            var sessionList = await _db.MiniSessons.Where(m => (m.session_key.Trim().Equals(sessionObj.session_key.Trim())
                    && m.open_id.Trim().Equals(sessionObj.openid.Trim()) 
                    && m.session_type.Trim().Equals(openIdType.Trim())  )).ToListAsync();
            MiniSession session = new MiniSession();
            if (sessionList.Count > 0)
            {
                session = sessionList[0];
            }
            else
            {
                session = new MiniSession()
                {
                    session_key = sessionObj.session_key,
                    session_type = openIdType.Trim(),
                    open_id = sessionObj.openid.Trim(),
                    member_id = member.id
                };
                await _db.MiniSessons.AddAsync(session);
                await _db.SaveChangesAsync();
            }
            sessionObj.member = _memberHelper.RemoveSensitiveInfo(member);
            sessionObj.openid = "";
            sessionObj.unionid = "";
            return Ok(sessionObj);
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
            tokenFilePath = tokenFilePath + "/access_token.official_account";
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
                }
            }
            string getTokenUrl = "https://api.weixin.qq.com/cgi-bin/token?grant_type=client_credential&appid="
                + _settings.appId.Trim() + "&secret=" + _settings.appSecret.Trim();
            try
            {
                string ret = Util.GetWebContent(getTokenUrl);
                AccessToken at = JsonConvert.DeserializeObject<AccessToken>(ret);
                if (!at.access_token.Trim().Equals(""))
                {
                    System.IO.File.AppendAllText(tokenFilePath, at.access_token + "\r\n" + nowTime);
                    return at.access_token.Trim();
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
        [NonAction]
        public async Task<WebApiLog> PerformRequest(string url, string header, string payload, 
            string method = "GET", string source = "易龙雪聚小程序", string purpose = "", string memo = "")
        {
            WebApiLog log = new WebApiLog()
            {
                id = 0,
                source = source.Trim(),
                purpose = purpose.Trim(),
                memo = memo.Trim(),
                method = method.Trim(),
                header = header.Trim(),
                payload = payload.Trim(),
                request_url = url.Trim()
            };
            await _db.webApiLog.AddAsync(log);
            await _db.SaveChangesAsync();
            try
            {
                switch(method.ToLower())
                {
                    case "post":
                        log.response = Util.GetWebContent(log.request_url, log.payload);
                    break;
                    default:
                        log.response = Util.GetWebContent(log.request_url);
                    break;
                }
            }
            catch
            {

            }
            log.deal = 1;
            log.update_date = DateTime.Now;
            _db.webApiLog.Entry(log).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return log;
        }
        public class Code2Session
        {
            public string openid { get; set; } = "";
            public string session_key { get; set; } = "";
            public string unionid { get; set; } = null;
            public string errcode { get; set; } = "";
            public string errmsg { get; set; } = "";

            public int? member_id {get; set;} = null;

            public Member member {get; set;} = null;
        }

        protected class AccessToken
        {
            public string access_token = "";
            public int expires_in = 0;

        }

    }
}
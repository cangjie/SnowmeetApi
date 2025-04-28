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
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Controllers;

namespace LuqinMiniAppBase.Controllers
{
    [Route("api/[controller]/[action]")]
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


        //new season///////////////////////////////////////////////////////////////////
        [HttpGet]
        public async Task<ActionResult<ApiResult<Code2Session>>> MemberLogin(string code, string openIdType)
        {
            ApiResult<Code2Session> result = new ApiResult<Code2Session>();
            string appId = _settings.appId;
            string appSecret = _settings.appSecret;
            string checkUrl = "https://api.weixin.qq.com/sns/jscode2session?appid=" + appId.Trim()
                + "&secret=" + appSecret.Trim() + "&js_code=" + code.Trim()
                + "&grant_type=authorization_code";
            WebApiLog log = await PerformRequest(checkUrl, "", "", "GET", "小程序登录", "获取session");
            if (log == null || log.response == null || log.response.Trim().Length <= 0)
            {
                result.code = 1;
                result.message = "请求小程序登录接口失败";
                result.data = null;
                return Ok(result);
            }
            string jsonResult = log.response.Trim();
            Code2Session sessionObj = JsonConvert.DeserializeObject<Code2Session>(jsonResult);
            if (!sessionObj.errcode.ToString().Equals(""))
            {
                result.code = 1;
                result.message = "获取session失败 " + sessionObj.errcode.ToString() + " " + sessionObj.errmsg;
                result.data = null;
                return Ok(result);
            }
            string openId = sessionObj.openid;
            string sessionKey = sessionObj.session_key;
            string? unionId = null;
            int? memberId = null;
            try
            {
                unionId = sessionObj.unionid;
            }
            catch
            {
                unionId = null;
            }

            if (unionId != null && unionId.Trim().Length > 0)
            {
                List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                    .Where(m => (m.num.Trim().Equals(unionId.Trim()) && m.valid == 1 && m.type.Trim().Equals("wechat_unionid")))
                    .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
                if (msaList.Count > 0)
                {
                    memberId = msaList[0].member_id;
                }
            }
            if (memberId == null)
            {
                List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                    .Where(m => (m.num.Trim().Equals(openId.Trim()) && m.valid == 1 && m.type.Trim().Equals("wechat_mini_openid")))
                    .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
                if (msaList.Count > 0)
                {
                    memberId = msaList[0].member_id;
                }
            }
            Member member = new Member();
            if (memberId == null)
            {
                member.id = 0;
                if (openId != null && openId.Trim().Length > 0)
                {
                    MemberSocialAccount msa = new MemberSocialAccount()
                    {
                        type = "wechat_mini_openid",
                        num = openId.Trim(),
                        valid = 1,
                        memo = "",
                        member_id = member.id
                    };
                    member.memberSocialAccounts.Add(msa);
                }
                if (unionId != null && unionId.Trim().Length > 0)
                {
                    MemberSocialAccount msa = new MemberSocialAccount()
                    {
                        type = "wechat_unionid",
                        num = unionId.Trim(),
                        valid = 1,
                        memo = "",
                        member_id = member.id
                    };
                    member.memberSocialAccounts.Add(msa);
                }
                await _db.member.AddAsync(member);
                await _db.SaveChangesAsync();
            }
            if (member.id == 0)
            {
                List<Member> memberList = await _db.member
                    .Where(m => m.id == memberId)
                    .Include(m => m.memberSocialAccounts)
                    .ToListAsync();
                if (memberList.Count <= 0)
                {
                    result.code = 1;
                    result.message = "获取会员信息失败";
                    result.data = null;
                    return Ok(result);
                }
                member = memberList[0];
                if (member.wechatMiniOpenId == null)
                {
                    MemberSocialAccount msa = new MemberSocialAccount()
                    {
                        type = "wechat_mini_openid",
                        num = openId.Trim(),
                        valid = 1,
                        memo = "",
                        member_id = member.id
                    };
                    member.memberSocialAccounts.Add(msa);
                    _db.member.Entry(member).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
                if (member.wechatUnionId == null)
                {
                    MemberSocialAccount msa = new MemberSocialAccount()
                    {
                        type = "wechat_unionid",
                        num = unionId.Trim(),
                        valid = 1,
                        memo = "",
                        member_id = member.id
                    };
                    member.memberSocialAccounts.Add(msa);
                    _db.member.Entry(member).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }
            string sessionType = "wechat_mini_openid";
            MiniSession session = await _db.miniSession.FindAsync(sessionKey.Trim(), sessionType);
            DateTime expireDate = DateTime.Now.AddHours(2);
            if (session == null)
            {
                session = new MiniSession()
                {
                    session_key = sessionKey.Trim(),
                    session_type = sessionType.Trim(),
                    member_id = member.id,
                    valid = 1,
                    expire_date = expireDate
                };
                await _db.miniSession.AddAsync(session);
                
            }
            else
            {
                session.valid = 1;
                session.member_id = member.id;
                session.expire_date = expireDate;
                _db.miniSession.Entry(session).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            member.memberSocialAccounts = member.memberSocialAccounts.Where(m => m.valid == 1 && m.type.Trim().Equals("cell")).OrderByDescending(m => m.id).ToList();
            sessionObj.member = member;
            StaffController _staffHelper = new StaffController(_db);
            sessionObj.staff = await _staffHelper.GetStaffBySocialNum(openId, "wechat_mini_openid", DateTime.Now);
            sessionObj.openid = "";
            sessionObj.unionid = "";
            result.code = 0;
            result.message = "";
            result.data = sessionObj;
            return Ok(result);
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
                        log.response = Util.GetWebContent(log.request_url, log.payload, "application/json");
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
            [NotMapped]
            public Member member {get; set;} = null;
            [NotMapped]
            public Staff staff {get; set;} = null;
        }

        protected class AccessToken
        {
            public string access_token = "";
            public int expires_in = 0;

        }

    }
}
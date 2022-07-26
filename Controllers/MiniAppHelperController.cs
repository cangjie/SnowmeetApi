using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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

namespace LuqinMiniAppBase.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MiniAppHelperController : ControllerBase
    {

        private readonly ApplicationDBContext _db;

        private readonly IConfiguration _config;

        private readonly Settings _settings;

        public MiniAppHelperController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _settings = Settings.GetSettings(_config);
        }

        [HttpGet]
        public ActionResult<string> PushMessage(string signature,
            string timestamp, string nonce, string echostr)
        {
            return echostr.Trim();
        }


        /*
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
                        OfficailAccountReply reply = new OfficailAccountReply(_db, _config, msg);
                        return reply.Reply().Trim();
                    }
                    catch
                    {

                    }
                    return "success";
                }

                */
        [HttpGet]
        public  async Task<ActionResult<Code2Session>> Login(string code)
        {
            string appId = _settings.appId;
            string appSecret = _settings.appSecret;
            string checkUrl = "https://api.weixin.qq.com/sns/jscode2session?appid=" + appId.Trim()
                + "&secret=" + appSecret.Trim() + "&js_code=" + code.Trim()
                + "&grant_type=authorization_code";
            string jsonResult = Util.GetWebContent(checkUrl);
            Code2Session sessionObj = JsonConvert.DeserializeObject<Code2Session>(jsonResult);
            
            if (sessionObj.errcode.ToString().Equals(""))
            {
                var sessionList = _db.MiniSessons.Where(m => (m.session_key.Trim().Equals(sessionObj.session_key.Trim())
                    && m.open_id.Trim().Equals(sessionObj.openid.Trim()))).ToList();
                if (sessionList.Count == 0)
                {
                    MiniSession mSession = new MiniSession()
                    {
                        session_key = sessionObj.session_key.Trim(),
                        open_id = sessionObj.openid.Trim()
                    };
                    await _db.MiniSessons.AddAsync(mSession);
                    await _db.SaveChangesAsync();
                }
                MiniAppUser user = await _db.MiniAppUsers.FindAsync(sessionObj.openid);
                if (user == null)
                {
                    user = new MiniAppUser()
                    {
                        open_id = sessionObj.openid
                    };
                    await _db.MiniAppUsers.AddAsync(user);
                    await _db.SaveChangesAsync();
                }
                var uidList = _db.UnionIds.Where(u => (u.open_id.Trim().Equals(sessionObj.openid.Trim())
                    && u.source.Trim().Equals("snowmeet_mini") && u.union_id.Trim().Equals(sessionObj.unionid.Trim()))).ToList();
                if (uidList.Count == 0)
                {
                    UnionId uid = new UnionId()
                    {
                        source = "snowmeet_mini",
                        open_id = sessionObj.openid,
                        union_id = sessionObj.unionid.Trim()
                    };
                    await _db.UnionIds.AddAsync(uid);
                    await _db.SaveChangesAsync();
                }
                return sessionObj;
            }
            return NotFound();
        }


        /*
        [HttpGet("{code}")]
        public async Task<ActionResult<MiniAppUser>> Login(string code)
        {
            string appId = _settings.appId;
            string appSecret = _settings.appSecret;
            string checkUrl = "https://api.weixin.qq.com/sns/jscode2session?appid=" + appId.Trim()
                + "&secret=" + appSecret.Trim() + "&js_code=" + code.Trim()
                + "&grant_type=authorization_code";
            string jsonResult = Util.GetWebContent(checkUrl);
            Code2Session sessionObj = JsonConvert.DeserializeObject<Code2Session>(jsonResult);
            var miniUser = new MiniAppUser()
            {
                open_id = ""
            };
            if (sessionObj.errcode.ToString().Equals(""))
            {
                UserHelperController userHelper = new UserHelperController(_db, _config);
                string unionId = sessionObj.unionid.Trim();
                if (!unionId.Trim().Equals(""))
                {
                    int userId = userHelper.GetUserId(unionId.Trim()).Value;
                    //New Unic User
                    if (userId == 0)
                    {
                        UnicUser unicUser = new UnicUser()
                        {
                            id = 0,
                            oa_union_id = unionId.Trim(),
                            sessionKey = ""
                        };
                        await _db.unicUser.AddAsync(unicUser);
                        try
                        {
                            await _db.SaveChangesAsync();
                            if (unicUser.id > 0)
                            {
                                string openId = sessionObj.openid.Trim();
                                miniUser = userHelper.GetMiniUser(openId.Trim()).Value;
                                if (miniUser != null)
                                {
                                    if (miniUser.user_id != userId)
                                    {
                                        miniUser.user_id = userId;
                                        _db.Entry(miniUser);
                                        try
                                        {
                                            await _db.SaveChangesAsync();
                                        }
                                        catch
                                        {

                                        }

                                    }
                                    miniUser.sessionKey = sessionObj.session_key.Trim();
                                }
                                else
                                {
                                    miniUser = new MiniUser()
                                    {
                                        id = 0,
                                        user_id = userId,
                                        open_id = openId.Trim(),
                                        original_id = _settings.originalId.Trim(),
                                        sessionKey = sessionObj.session_key.Trim()

                                    };
                                    await _db.miniUser.AddAsync(miniUser);
                                    try
                                    {
                                        await _db.SaveChangesAsync();
                                    }
                                    catch
                                    {

                                    }
                                }
                            }

                        }
                        catch
                        {

                        }
                    }
                    //Old Unic User
                    else
                    {
                        var unicUser = await _db.unicUser.FindAsync(userId);
                        var miniUserList = _db.miniUser
                            .Where(u => (u.user_id == userId
                            && u.original_id.Trim().Equals(_settings.originalId.Trim())))
                            .ToList();
                        if (miniUserList.Count == 0)
                        {
                            miniUser = new MiniUser()
                            {
                                id = 0,
                                user_id = userId,
                                open_id = sessionObj.openid.Trim(),
                                original_id = _settings.originalId.Trim(),
                                sessionKey = sessionObj.session_key.Trim()

                            };
                            await _db.miniUser.AddAsync(miniUser);
                            try
                            {
                                await _db.SaveChangesAsync();
                            }
                            catch
                            {

                            }
                        }
                        else
                        {
                            miniUser = miniUserList[0];
                            if (miniUser.user_id != userId)
                            {
                                miniUser.user_id = userId;
                                _db.Entry(miniUser);
                                try
                                {
                                    await _db.SaveChangesAsync();
                                }
                                catch
                                {

                                }
                            }
                            miniUser.sessionKey = sessionObj.session_key.Trim();
                        }
                    }

                    //set token
                    var tokenList = _db.token.Where(t => (t.state == 1
                        && t.user_id == miniUser.user_id)).ToList();
                    for (int i = 0; i < tokenList.Count; i++)
                    {
                        tokenList[i].state = 0;
                        _db.Entry(tokenList[i]);
                        try
                        {
                            await _db.SaveChangesAsync();
                        }
                        catch
                        {

                        }
                    }
                    Token token = new Token()
                    {
                        id = 0,
                        token = miniUser.sessionKey.Trim(),
                        open_id = miniUser.open_id.Trim(),
                        original_id = _settings.originalId.Trim(),
                        user_id = miniUser.user_id,
                        expire_timestamp = 0,
                        state = 1
                    };
                    await _db.token.AddAsync(token);
                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch
                    {

                    }
                    miniUser.open_id = "";
                    return miniUser;
                }
                else
                {
                    return NotFound();
                }

            }
            return NoContent();
        }

        */


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


        public class Code2Session
        {
            public string openid { get; set; } = "";
            public string session_key { get; set; } = "";
            public string unionid { get; set; } = "";
            public string errcode { get; set; } = "";
            public string errmsg { get; set; } = "";
        }

        protected class AccessToken
        {
            public string access_token = "";
            public int expires_in = 0;

        }

    }
}
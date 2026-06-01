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
using Flurl.Util;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Aop.Api;
using Aop.Api.Request;
using Aop.Api.Response;
using Aop.Api.Util;

namespace SnowmeetApi.Controllers
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
        [HttpGet]
        public async Task<ActionResult<ApiResult<Code2Session>>> MemberLogin(string code, string openIdType)
        {
            // 支付宝小程序登录走独立分支（用 alipay.system.oauth.token + 自家 RSA 密钥对，与微信通道完全无关）
            if (openIdType != null && openIdType.Trim().Equals("alipay_payerid"))
            {
                return await _alipayMemberLogin(code);
            }
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
                // 2026-05-29 修: 仅当 unionid 反查无果时,再用 social_account_for_job 兜底。
                // 之前是无条件覆盖 → social_account_for_job 里存在指向死会员的脏数据(典型 id=55 指 member_id=40649,
                // 该会员早已删除),会把刚 PaymentIdentity 建的真实会员(如 41104)立刻打回 null,触发下方孤儿清理。
                if (memberId == null)
                {
                    SocialAccountForJob jobAccount = await _db.socialAccountForJob.Where(s => s.wechat_mini_openid == openId).AsNoTracking().FirstOrDefaultAsync();
                    if (jobAccount != null)
                    {
                        memberId = jobAccount.member_id;
                    }
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
            // 2026-05-29 重构: 不再自动建 stub member。
            // memberId 找到 → 拉出对应 member;没找到(未注册 user) → member = null
            // 未注册 user 的 openid+unionid 暂存到 mini_session,延迟到 PaymentIdentityController(点支付按钮时)再建会员
            Member member = null;
            if (memberId != null)
            {
                member = await _memberHelper.GetWholeMemberById((int)memberId);
                // member 查不到说明 MSA 表脏数据(memberId 指向不存在的 member) → 仍当未注册处理(不建恢复 stub)
                // 此时 session 仅记 openid+unionid, 用户下次正常流程会被 PaymentIdentity 引导建会员
            }
            string sessionType = "wechat_mini_openid";
            MiniSession session = await _db.miniSession.FindAsync(sessionKey.Trim(), sessionType);
            DateTime expireDate = DateTime.Now.AddHours(2);
            string? cleanOpenId = string.IsNullOrEmpty(openId) ? null : openId.Trim();
            string? cleanUnionId = string.IsNullOrEmpty(unionId) ? null : unionId.Trim();
            if (session == null)
            {
                session = new MiniSession()
                {
                    session_key = sessionKey.Trim(),
                    session_type = sessionType.Trim(),
                    member_id = member?.id,
                    wechat_openid = cleanOpenId,
                    wechat_unionid = cleanUnionId,
                    valid = 1,
                    expire_date = expireDate
                };
                await _db.miniSession.AddAsync(session);

            }
            else
            {
                session.valid = 1;
                session.member_id = member?.id;
                session.wechat_openid = cleanOpenId;
                session.wechat_unionid = cleanUnionId;
                session.expire_date = expireDate;
                _db.miniSession.Entry(session).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            //await _db.SaveChangesAsync();
            
            sessionObj.member = member;
            StaffController _staffHelper = new StaffController(_db);
            sessionObj.staff = await _staffHelper.GetStaffBySocialNum(openId, "wechat_mini_openid", DateTime.Now);
            sessionObj.openid = "";
            sessionObj.unionid = "";
            result.code = 0;
            result.message = "";
            result.data = sessionObj;
            //_db.member.Entry(member).State = EntityState.Detached;
            //await _db.SaveChangesAsync();
            // 2026-05-29 删除「孤儿清理」整段(原 try/catch 块): 当 openid 关联到当前 memberId 之外的会员时,
            // 把该会员 valid=0 + MSA 全部 valid=0 + 订单转移到 memberId。这段与「scanner 优先,不动 MSA」
            // 原则直接冲突,会把刚 PaymentIdentity 建的真实会员立刻打回失效,触发新一轮散客分支建会员。
            // 复现案例: 41104 → MemberLogin 触发后被 invalidate → 散客分支建 41105 → 下次刷新再杀一次。
            // 返回前缩 memberSocialAccounts 仅留 cell(优化网络);未注册 user 时 member 为 null,跳过
            if (member != null)
            {
                member.memberSocialAccounts = member.memberSocialAccounts.Where(m => m.valid == 1 && m.type.Trim().Equals("cell")).OrderByDescending(m => m.id).ToList();
            }
            return Ok(result);
        }

        // 支付宝小程序登录：与 wechat 路径平行的实现
        // 入参 code = my.getAuthCode 拿到的 auth_code（base scope 即可）
        // 流程：oauth.token 换 (access_token, user_id) → MSA 反查 member → 写 MiniSession
        // 与 wechat 同策略：未匹配会员**不建 stub**（2026-05-29 重构定调），延迟到 PaymentIdentity 建
        private async Task<ActionResult<ApiResult<Code2Session>>> _alipayMemberLogin(string code)
        {
            ApiResult<Code2Session> result = new ApiResult<Code2Session>();
            if (string.IsNullOrEmpty(code))
            {
                result.code = 1;
                result.message = "auth_code 缺失";
                result.data = null;
                return Ok(result);
            }

            // Step 1: alipay.system.oauth.token 换 access_token + user_id
            IAopClient client;
            try
            {
                client = _getAlipayMiniClient();
            }
            catch (Exception e)
            {
                result.code = 1;
                result.message = "支付宝证书加载失败：" + e.Message;
                result.data = null;
                return Ok(result);
            }

            var tokenReq = new AlipaySystemOauthTokenRequest();
            tokenReq.GrantType = "authorization_code";
            tokenReq.Code = code.Trim();
            AlipaySystemOauthTokenResponse tokenResp;
            try
            {
                tokenResp = client.CertificateExecute(tokenReq);
            }
            catch (Exception e)
            {
                result.code = 1;
                result.message = "支付宝 oauth.token 请求异常：" + e.Message;
                result.data = null;
                return Ok(result);
            }
            if (tokenResp.IsError || string.IsNullOrEmpty(tokenResp.AccessToken) || string.IsNullOrEmpty(tokenResp.UserId))
            {
                result.code = 1;
                result.message = "支付宝 oauth.token 失败：" + (tokenResp.SubMsg ?? tokenResp.Msg);
                result.data = null;
                return Ok(result);
            }
            string accessToken = tokenResp.AccessToken.Trim();
            string userId = tokenResp.UserId.Trim();

            // Step 2: MSA 反查会员（type='alipay_payerid'）
            int? memberId = null;
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => m.num.Trim().Equals(userId) && m.valid == 1 && m.type.Trim().Equals("alipay_payerid"))
                .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
            if (msaList.Count > 0)
            {
                memberId = msaList[0].member_id;
            }
            Member member = null;
            if (memberId != null)
            {
                member = await _memberHelper.GetWholeMemberById((int)memberId);
                // 若 MSA 指向已删 member（脏数据），仍按未注册处理，不建 stub
            }

            // Step 3: 写 / 更新 MiniSession，session_key=access_token，session_type='alipay_payerid'
            // wechat_openid 列复用存 alipay user_id（列名虽叫 wechat 但实际是「该 session 对应方的 openid 等价物」）
            string sessionType = "alipay_payerid";
            MiniSession session = await _db.miniSession.FindAsync(accessToken, sessionType);
            DateTime expireDate = DateTime.Now.AddHours(2);
            if (session == null)
            {
                session = new MiniSession()
                {
                    session_key = accessToken,
                    session_type = sessionType,
                    member_id = member?.id,
                    wechat_openid = userId,
                    wechat_unionid = null,
                    valid = 1,
                    expire_date = expireDate
                };
                await _db.miniSession.AddAsync(session);
            }
            else
            {
                session.valid = 1;
                session.member_id = member?.id;
                session.wechat_openid = userId;
                session.wechat_unionid = null;
                session.expire_date = expireDate;
                _db.miniSession.Entry(session).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            // Step 4: 返回 Code2Session（结构同 wechat 分支：session_key + member + staff）
            Code2Session sessionObj = new Code2Session
            {
                session_key = accessToken,
                openid = "",
                unionid = "",
                errcode = "",
                errmsg = "",
                member_id = member?.id,
                member = member,
                alipay_payerid = userId
            };
            StaffController _staffHelper = new StaffController(_db);
            sessionObj.staff = await _staffHelper.GetStaffBySocialNum(userId, "alipay_payerid", DateTime.Now);

            // 同 wechat 路径：member 非空时仅保留 cell 类 MSA，缩减网络体积
            if (member != null)
            {
                member.memberSocialAccounts = member.memberSocialAccounts.Where(m => m.valid == 1 && m.type.Trim().Equals("cell")).OrderByDescending(m => m.id).ToList();
            }

            result.code = 0;
            result.message = "";
            result.data = sessionObj;
            return Ok(result);
        }

        // 创建支付宝小程序 appId 的 IAopClient（独立证书：AlipayCertificate/2021006157624571/）
        // 与 AliController.GetClient(appId) 同模式，但 PaymentIdentity / MemberLogin 都需要自己取，直接复制以避免跨控制器依赖
        private IAopClient _getAlipayMiniClient()
        {
            const string appId = "2021006157624571";
            //const string appId = "2021004143665722";
            string certPath = Util.workingPath + "/AlipayCertificate/" + appId;
            string privateKey = System.IO.File.OpenText(certPath + "/private_key_" + appId + ".txt").ReadToEnd().Trim();
            CertParams certParams = new CertParams
            {
                AlipayPublicCertPath = certPath + "/alipayCertPublicKey_RSA2.crt",
                AppCertPath = certPath + "/appCertPublicKey_" + appId + ".crt",
                RootCertPath = certPath + "/alipayRootCert.crt"
            };
            return new DefaultAopClient("https://openapi.alipay.com/gateway.do", appId, privateKey, "json", "1.0", "RSA2", "utf-8", false, certParams);
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
            string method = "GET", string source = "易龙雪聚小程序", string purpose = "", string memo = "", string? batchId = null)
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
                request_url = url.Trim(),
                batch_id = batchId
            };
            await _db.webApiLog.AddAsync(log);
            await _db.SaveChangesAsync();
            try
            {
                switch (method.ToLower())
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

        [HttpGet]
        public ActionResult<string> OpenMiniProgram(string path, string query, string version = "release")
        {
            path = Util.UrlDecode(path);
            query = Util.UrlDecode(query);
            string ret = "";
            string token = GetAccessToken();
            string postUrl = "https://api.weixin.qq.com/wxa/generatescheme?access_token=" + token.Trim();
            string postData = "{ "
                + "\"jump_wxa\": "
                + "{ "
                + " \"path\": \"" + path.Trim() + "\" , "
                + " \"query\": \"" + query + "\", "
                + " \"env_version\": \"" + version + "\" }, "
                + " \"is_expire\": true , "
                + " \"expire_type\":1, "
                + " \"expire_interval\":1 }";
            ret = Util.GetWebContent(postUrl, postData, "application/json");
            return Ok(ret);
        }


        public class Code2Session
        {
            public string openid { get; set; } = "";
            public string session_key { get; set; } = "";
            public string unionid { get; set; } = null;
            public string errcode { get; set; } = "";
            public string errmsg { get; set; } = "";
            public int? member_id { get; set; } = null;
            public string? alipay_payerid { get; set; } = null;
            [NotMapped]
            public Member member { get; set; } = null;
            [NotMapped]
            public Staff staff { get; set; } = null;
        }

        protected class AccessToken
        {
            public string access_token = "";
            public int expires_in = 0;

        }

    }
}
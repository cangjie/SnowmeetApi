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
using Newtonsoft.Json.Linq;
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
        public async Task<ActionResult<ApiResult<Code2Session>>> MemberLogin(string code, string openIdType, string? aliSessionKey = null, string? aliEncData = null)
        {
            // 支付宝小程序登录走独立分支（用 alipay.system.oauth.token + 自家 RSA 密钥对，与微信通道完全无关）
            // 2026-06-03: aliSessionKey+aliEncData 两参数支持二次调用（取手机号 + MSA cell 兜底匹配会员）
            // (微信分支的局部变量 sessionKey 与之同名会冲突, 故 alipay 参数加 ali 前缀)
            if (openIdType != null && openIdType.Trim().Equals("alipay_payerid"))
            {
                return await _alipayMemberLogin(code, aliSessionKey, aliEncData);
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
        // 入参:
        //   code = my.getAuthCode 拿到的 auth_code(首次调用必填; 二次调用可空)
        //   sessionKey = 首次调用返回的 session_key(即 access_token), 二次调用必填
        //   encData = my.getPhoneNumber 返回 response 字段, 二次调用必填
        //
        // 流程概览(2026-06-03 重构):
        //   首次调用(有 code, 无 sessionKey/encData):
        //     1. oauth.token 换 access_token + payerId
        //     2. MSA(type=alipay_payerid, num=payerId) 反查 memberId
        //     3. memberId 命中且 MSA(type=cell) 有 valid=1 一条 → 直接成功(needPhone=false)
        //     4. 否则写 mini_session + needPhone=true 返回, 等客户端补 encData 二次调用
        //   二次调用(有 sessionKey+encData):
        //     1. 反查 mini_session(sessionKey, session_type='alipay_payerid')
        //     2. AlipayPhoneDecryptHelper.Decrypt 解出 phone
        //     3. session.member_id 仍为 null → MSA(type=cell, num=phone) 反查 memberId
        //     4. memberId 命中 + MSA 无该 payerId 的 alipay_payerid 记录 → INSERT(_ensureAlipayPayerIdMsa)
        //     5. 更新 mini_session(cell, member_id, expire_date) 返回
        //   策略约束: 未匹配会员**永不建 Member**(5-29 定调), 散客建会员留给 PaymentIdentity 流程
        private async Task<ActionResult<ApiResult<Code2Session>>> _alipayMemberLogin(string code, string? sessionKey, string? encData)
        {
            // 二次调用分支(sessionKey + encData 同时存在)
            if (!string.IsNullOrEmpty(sessionKey) && !string.IsNullOrEmpty(encData))
            {
                return await _alipayMemberLoginSecondCall(sessionKey, encData);
            }
            // 否则当首次调用处理(必须有 code)
            return await _alipayMemberLoginFirstCall(code);
        }

        private async Task<ActionResult<ApiResult<Code2Session>>> _alipayMemberLoginFirstCall(string code)
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
                string inner = e.InnerException != null ? (" | inner=" + e.InnerException.Message) : "";
                result.message = "支付宝 oauth.token 请求异常：" + e.GetType().Name + " | " + e.Message + inner;
                result.data = null;
                return Ok(result);
            }
            string accessToken = string.IsNullOrEmpty(tokenResp.AccessToken) ? "" : tokenResp.AccessToken.Trim();
            string? userId = string.IsNullOrEmpty(tokenResp.UserId) ? null : tokenResp.UserId.Trim();
            string? openId = null;
            try
            {
                if (!string.IsNullOrEmpty(tokenResp.Body))
                {
                    JObject bodyObj = JObject.Parse(tokenResp.Body);
                    JToken? tokenNode = bodyObj["alipay_system_oauth_token_response"] ?? bodyObj;
                    if (tokenNode != null)
                    {
                        if (string.IsNullOrEmpty(userId))
                        {
                            string? userIdFromBody = tokenNode["user_id"]?.ToString();
                            if (!string.IsNullOrEmpty(userIdFromBody)) userId = userIdFromBody.Trim();
                        }
                        string? openIdFromBody = tokenNode["open_id"]?.ToString();
                        if (!string.IsNullOrEmpty(openIdFromBody)) openId = openIdFromBody.Trim();
                    }
                }
            }
            catch
            {
                // no-op: body 解析失败不影响主流程，后续仍按 SDK 字段判断
            }

            // 严格区分两个标识：
            // - payerId 仅来自 user_id（用于后续需要 user_id 的交易链路）
            // - openId 仅来自 open_id
            // 不再用 open_id 回退填充 payerId，避免两字段被误写成同值。
            string? payerId = !string.IsNullOrEmpty(userId) ? userId : null;

            if (tokenResp.IsError || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(payerId))
            {
                List<string> errParts = new List<string>();
                if (!string.IsNullOrEmpty(tokenResp.Code)) errParts.Add("code=" + tokenResp.Code.Trim());
                if (!string.IsNullOrEmpty(tokenResp.SubCode)) errParts.Add("sub_code=" + tokenResp.SubCode.Trim());
                if (!string.IsNullOrEmpty(tokenResp.Msg)) errParts.Add("msg=" + tokenResp.Msg.Trim());
                if (!string.IsNullOrEmpty(tokenResp.SubMsg)) errParts.Add("sub_msg=" + tokenResp.SubMsg.Trim());
                if (string.IsNullOrEmpty(payerId)) errParts.Add("hint=oauth.token 未返回 user_id（payer_id），请确认支付宝授权范围是否满足）");
                if (!string.IsNullOrEmpty(tokenResp.Body))
                {
                    string body = tokenResp.Body.Trim();
                    if (body.Length > 400)
                    {
                        body = body.Substring(0, 400) + "...";
                    }
                    errParts.Add("body=" + body);
                }

                result.code = 1;
                result.message = errParts.Count > 0
                    ? ("支付宝 oauth.token 失败：" + string.Join(" | ", errParts))
                    : "支付宝 oauth.token 失败：支付宝返回空错误信息（msg/sub_msg/body 均为空）";
                result.data = null;
                return Ok(result);
            }
            payerId = payerId.Trim();

            // Step 2: MSA 反查会员（type='alipay_payerid'）
            int? memberId = null;
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => m.valid == 1
                    && m.type.Trim().Equals("alipay_payerid")
                    && (
                        m.num.Trim().Equals(payerId)
                        || (!string.IsNullOrEmpty(userId) && m.num.Trim().Equals(userId))
                        || (!string.IsNullOrEmpty(openId) && m.num.Trim().Equals(openId))
                    ))
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
                if (member == null)
                {
                    memberId = null;
                }
            }

            // Step 3: 直查 MSA 表看会员是否已绑 valid=1 cell(不走 Member.cell getter, 避免 Include 漏判)
            bool memberHasCell = false;
            if (memberId != null)
            {
                memberHasCell = await _db.memberSocialAccount.AnyAsync(m =>
                    m.member_id == memberId
                    && m.type.Trim().Equals("cell")
                    && m.valid == 1);
            }

            // Step 4: 写 / 更新 MiniSession，session_key=access_token，session_type='alipay_payerid'
            // 2026-06-03: payerId 写入新建的 alipay_payerid 列(替代之前 wechat_openid 列的 hack)
            string sessionType = "alipay_payerid";
            MiniSession session = await _db.miniSession.FindAsync(accessToken, sessionType);
            DateTime expireDate = DateTime.Now.AddHours(2);
            if (session == null)
            {
                session = new MiniSession()
                {
                    session_key = accessToken,
                    session_type = sessionType,
                    member_id = memberId,
                    alipay_openid = openId,
                    alipay_payerid = payerId,
                    cell = null,
                    wechat_openid = null,
                    wechat_unionid = null,
                    valid = 1,
                    expire_date = expireDate
                };
                await _db.miniSession.AddAsync(session);
            }
            else
            {
                session.valid = 1;
                session.member_id = memberId;
                session.alipay_openid = openId;
                session.alipay_payerid = payerId;
                // 二次调用回来才填 cell, 首次保持原值不动(覆盖式重置只在 alipay_payerid 上做)
                session.wechat_openid = null;
                session.wechat_unionid = null;
                session.expire_date = expireDate;
                _db.miniSession.Entry(session).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            // Step 5: 组装 Code2Session 返回(needPhone 信号: 缺会员 或 会员无 cell)
            bool needPhone = (memberId == null) || (!memberHasCell);
            Code2Session sessionObj = new Code2Session
            {
                session_key = accessToken,
                openid = "",
                unionid = "",
                errcode = "",
                errmsg = "",
                member_id = memberId,
                member = member,
                alipay_openid = openId,
                alipay_payerid = payerId,
                cell = null,
                needPhone = needPhone
            };
            StaffController _staffHelper = new StaffController(_db);
            sessionObj.staff = await _staffHelper.GetStaffBySocialNum(payerId, "alipay_payerid", DateTime.Now);

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

        // 二次调用: 用首次返回的 sessionKey(access_token)反查 mini_session, 解 encData → phone, 用 phone 做 MSA(cell)兜底匹配
        private async Task<ActionResult<ApiResult<Code2Session>>> _alipayMemberLoginSecondCall(string sessionKey, string encData)
        {
            ApiResult<Code2Session> result = new ApiResult<Code2Session>();
            const string sessionType = "alipay_payerid";

            // Step 1: 反查 mini_session
            string sk = Util.UrlDecode(sessionKey ?? "").Trim();
            MiniSession session = await _db.miniSession.FindAsync(sk, sessionType);
            if (session == null || session.valid != 1 || session.expire_date < DateTime.Now)
            {
                result.code = 1;
                result.message = "session 过期或不存在,请重新登录";
                result.data = null;
                return Ok(result);
            }
            string payerId = session.alipay_payerid;
            if (string.IsNullOrEmpty(payerId))
            {
                // 历史 session(2026-06-03 之前)把 payerId 写在 wechat_openid 列, 兼容回退
                payerId = session.wechat_openid;
            }

            // Step 2: 解密 encData → phone
            string phone;
            try
            {
                phone = SnowmeetApi.Helpers.AlipayPhoneDecryptHelper.Decrypt(encData, SnowmeetApi.Controllers.Order.PaymentIdentityController.ALIPAY_MINI_APP_ID);
            }
            catch (Exception ex)
            {
                result.code = 1;
                result.message = "手机号解析失败: " + ex.Message;
                result.data = null;
                return Ok(result);
            }
            if (string.IsNullOrEmpty(phone))
            {
                result.code = 1;
                result.message = "手机号解析为空";
                result.data = null;
                return Ok(result);
            }

            int? memberId = session.member_id;

            // Step 3: session.member_id 仍为 null → 用 phone 反查 MSA(cell)
            if (memberId == null)
            {
                List<MemberSocialAccount> cellMsaList = await _db.memberSocialAccount
                    .Where(m => m.valid == 1
                        && m.type.Trim().Equals("cell")
                        && m.num.Trim().Equals(phone))
                    .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
                if (cellMsaList.Count > 0)
                {
                    memberId = cellMsaList[0].member_id;
                }
            }

            // Step 4: 若现在 memberId 命中, 且 MSA 表无该 payerId 的 alipay_payerid 记录 → INSERT
            Member member = null;
            if (memberId != null)
            {
                member = await _memberHelper.GetWholeMemberById((int)memberId);
                if (member == null)
                {
                    // MSA 指向死会员的脏数据 → 当未注册处理(不建 stub)
                    memberId = null;
                }
                else if (!string.IsNullOrEmpty(payerId))
                {
                    await _ensureAlipayPayerIdMsa((int)memberId, payerId);
                }
            }

            // Step 5: 更新 mini_session(cell + member_id + expire_date 续期)
            session.cell = phone;
            session.member_id = memberId;
            session.expire_date = DateTime.Now.AddHours(2);
            _db.miniSession.Entry(session).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            // Step 6: 组装返回
            Code2Session sessionObj = new Code2Session
            {
                session_key = sk,
                openid = "",
                unionid = "",
                errcode = "",
                errmsg = "",
                member_id = memberId,
                member = member,
                alipay_openid = session.alipay_openid,
                alipay_payerid = payerId,
                cell = phone,
                needPhone = false
            };
            StaffController _staffHelper = new StaffController(_db);
            if (!string.IsNullOrEmpty(payerId))
            {
                sessionObj.staff = await _staffHelper.GetStaffBySocialNum(payerId, "alipay_payerid", DateTime.Now);
            }

            if (member != null)
            {
                member.memberSocialAccounts = member.memberSocialAccounts.Where(m => m.valid == 1 && m.type.Trim().Equals("cell")).OrderByDescending(m => m.id).ToList();
            }

            result.code = 0;
            result.message = "";
            result.data = sessionObj;
            return Ok(result);
        }

        // INSERT MSA(type=alipay_payerid, num=payerId, member_id) 若不存在; 存在但 valid=0 → revive
        // 与 PaymentIdentityController._addMsa 同语义, 但本 controller 内独立持有避免跨控制器依赖
        private async Task _ensureAlipayPayerIdMsa(int memberId, string payerId)
        {
            if (string.IsNullOrEmpty(payerId)) return;
            string pid = payerId.Trim();
            var existing = await _db.memberSocialAccount
                .Where(m => m.member_id == memberId
                    && m.type.Trim().Equals("alipay_payerid")
                    && m.num.Trim().Equals(pid))
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                if (existing.valid != 1)
                {
                    existing.valid = 1;
                    existing.update_date = DateTime.Now;
                    _db.memberSocialAccount.Entry(existing).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
                return;
            }
            var msa = new MemberSocialAccount
            {
                id = 0,
                member_id = memberId,
                type = "alipay_payerid",
                num = pid,
                valid = 1
            };
            await _db.memberSocialAccount.AddAsync(msa);
            await _db.SaveChangesAsync();
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
            public string? alipay_openid { get; set; } = null;
            public string? alipay_payerid { get; set; } = null;
            public string? cell { get; set; } = null;
            // 2026-06-03: 支付宝 MemberLogin 二次调用语义信号 — true 表示首次未命中会员或会员无 cell,
            // 客户端需 my.getPhoneNumber 拿 encData 后再发起二次调用 MemberLogin(sessionKey, encData)
            public bool needPhone { get; set; } = false;
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
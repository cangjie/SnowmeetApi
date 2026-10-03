using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers.Fnb
{
    /// <summary>
    /// 餐饮业务（平行于滑雪各业务，相对独立）— 企业微信消息下发。
    /// 首个场景：食材过期提醒（到期前经由本控制器给相关人员推图文通知）。
    /// 企业微信自建应用：AgentId 1000009（与 WeComController 的 wedoc 应用同一企业、不同应用/Secret）。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class FnbWeComController : ControllerBase
    {
        // 与 WeComController 同一企业主体
        public const string CORP_ID = "ww3a46c4555ae069f9";
        // 餐饮通知自建应用
        public const int AGENT_ID = 1000009;
        public const string AGENT_SECRET = "W4MBlCAmAfDrXLYVj2xDTd1qh21_Del8RE7jcE9tu0Q";
        // 应用 1000009「网页授权及 JS-SDK」可信域名；只给这个域名下的页面签名
        public const string JSSDK_HOST = "mini.snowmeet.top";

        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly MiniAppHelperController _mH;

        public FnbWeComController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _mH = new MiniAppHelperController(_db, _config);
        }

        // ====== DTO ======

        public class NewsArticle
        {
            public string title { get; set; }              // 必填，≤128 字节
            public string description { get; set; } = "";  // ≤512 字节
            public string url { get; set; } = "";          // 点击图文跳转的 H5 链接（填了 appid+pagepath 时被忽略）
            public string picurl { get; set; } = "";       // 图片链接（可空）
            // 跳微信小程序页面：appid + pagepath 必须同时填写，填写后点击卡片直接打开小程序（url 被忽略）。
            // 前提：该小程序已在企业微信后台与本应用(1000009)关联。雪聚小程序 appid = wxd1310896f2aa68bb
            public string? appid { get; set; } = null;
            public string? pagepath { get; set; } = null;  // 例 pages/xxx/index?id=123
        }

        public class SendNewsBody
        {
            // 企业微信成员 UserID，多人用 | 分隔；"@all" 发给应用可见范围全部成员
            public string touser { get; set; } = "@all";
            public List<NewsArticle> articles { get; set; } = new List<NewsArticle>();
        }

        public class WeComSendResponse
        {
            public int errcode { get; set; }
            public string errmsg { get; set; }
            public string? invaliduser { get; set; } = null;
            public string? msgid { get; set; } = null;
            public string? access_token { get; set; } = null;
        }

        public class WeComTicketResponse
        {
            public int errcode { get; set; }
            public string errmsg { get; set; }
            public string? ticket { get; set; } = null;
            public int expires_in { get; set; }
        }

        // jsapi_ticket 有频率限制、有效期 2 小时：进程内缓存，提前 5 分钟过期
        private class CachedTicket
        {
            public string ticket;
            public DateTime expireAt;
        }
        private static CachedTicket? _corpTicket = null;
        private static CachedTicket? _agentTicket = null;
        private static readonly SemaphoreSlim _ticketLock = new SemaphoreSlim(1, 1);

        // ====== API ======

        // 企业微信 JS-SDK 签名（ww.register 的 getConfigSignature / getAgentConfigSignature）。
        // 不要求登录：签名只在可信域名下的页面里有效，这里也只给 JSSDK_HOST 的页面签。
        // 企业签名（config）和应用签名（agentConfig）一次都给，页面按需使用。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetJsSdkSignature(string url)
        {
            string? pageUrl = NormalizeJsSdkUrl(url);
            if (pageUrl == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "只能为 https://" + JSSDK_HOST + " 下的页面签名", data = null });
            }
            (string? corpTicket, string? agentTicket) = await GetJsApiTickets(DateTime.Now.ToString("yyyyMMddHHmmssfff"));
            if (corpTicket == null || agentTicket == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "获取企业微信 jsapi_ticket 失败", data = null });
            }
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string nonceStr = Guid.NewGuid().ToString("N").Substring(0, 16);
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new
                {
                    corpId = CORP_ID,
                    agentId = AGENT_ID,
                    url = pageUrl,
                    config = new { timestamp, nonceStr, signature = ComputeJsSdkSignature(corpTicket, nonceStr, timestamp, pageUrl) },
                    agentConfig = new { timestamp, nonceStr, signature = ComputeJsSdkSignature(agentTicket, nonceStr, timestamp, pageUrl) }
                }
            });
        }

        // 去掉 # 之后的部分；不是 https://JSSDK_HOST（默认端口）的页面返回 null
        [NonAction]
        public static string? NormalizeJsSdkUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }
            url = url.Trim();
            int hash = url.IndexOf('#');
            if (hash >= 0)
            {
                url = url.Substring(0, hash);
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !uri.IsDefaultPort
                || !uri.Host.Equals(JSSDK_HOST, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return url;
        }

        // 与微信 JS-SDK 同一算法：sha1("jsapi_ticket=..&noncestr=..&timestamp=..&url=..")，小写十六进制
        [NonAction]
        public static string ComputeJsSdkSignature(string ticket, string nonceStr, long timestamp, string url)
        {
            string raw = "jsapi_ticket=" + ticket + "&noncestr=" + nonceStr + "&timestamp=" + timestamp + "&url=" + url;
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }

        // 下发图文消息（news）。店员级鉴权；后续食材过期定时任务走 [NonAction] SendNews 直调。
        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> SendNewsMessage([FromBody] SendNewsBody body,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            if (body == null || body.articles == null || body.articles.Count == 0)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "缺少图文内容", data = null });
            }
            if (body.articles.Count > 8)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "图文消息最多 8 条", data = null });
            }
            for (int i = 0; i < body.articles.Count; i++)
            {
                if (body.articles[i] == null || string.IsNullOrWhiteSpace(body.articles[i].title))
                {
                    return Ok(new ApiResult<object>() { code = 1, message = "第 " + (i + 1) + " 条图文缺少标题", data = null });
                }
            }
            WeComSendResponse res = await SendNews(body.articles,
                string.IsNullOrWhiteSpace(body.touser) ? "@all" : body.touser.Trim(), "餐饮通知");
            if (res == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "企业微信接口调用失败", data = null });
            }
            if (res.errcode != 0)
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1,
                    message = "企业微信返回错误：" + res.errcode + " " + (res.errmsg ?? ""),
                    data = new { errcode = res.errcode, errmsg = res.errmsg, invaliduser = res.invaliduser }
                });
            }
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { msgid = res.msgid, invaliduser = res.invaliduser }
            });
        }

        // ====== Core（供本接口和后续食材过期定时任务复用） ======

        [NonAction]
        public async Task<WeComSendResponse?> SendNews(List<NewsArticle> articles, string toUser, string purpose)
        {
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            string token = await GetToken(batchId, purpose);
            if (token == null)
            {
                return null;
            }
            var payloadObj = new
            {
                touser = toUser,
                msgtype = "news",
                agentid = AGENT_ID,
                news = new
                {
                    articles = articles.Select(a => new
                    {
                        title = a.title.Trim(),
                        description = (a.description ?? "").Trim(),
                        url = (a.url ?? "").Trim(),
                        picurl = (a.picurl ?? "").Trim()
                    }).ToList()
                },
                enable_duplicate_check = 0
            };
            string payload = JsonConvert.SerializeObject(payloadObj);
            WebApiLog log = await _mH.PerformRequest(
                "https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token=" + token,
                "", payload, "POST", "企业微信", purpose, "下发图文消息", batchId);
            try
            {
                return JsonConvert.DeserializeObject<WeComSendResponse>(log.response);
            }
            catch
            {
                return null;
            }
        }

        // 下发纯文本消息（content ≤ 2048 字节）。美团采集程序的登录失效、停机提醒用它。
        [NonAction]
        public async Task<WeComSendResponse?> SendText(string content, string toUser, string purpose)
        {
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            string token = await GetToken(batchId, purpose);
            if (token == null)
            {
                return null;
            }
            var payloadObj = new
            {
                touser = toUser,
                msgtype = "text",
                agentid = AGENT_ID,
                text = new { content = content.Trim() },
                enable_duplicate_check = 0
            };
            string payload = JsonConvert.SerializeObject(payloadObj);
            WebApiLog log = await _mH.PerformRequest(
                "https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token=" + token,
                "", payload, "POST", "企业微信", purpose, "下发文本消息", batchId);
            try
            {
                return JsonConvert.DeserializeObject<WeComSendResponse>(log.response);
            }
            catch
            {
                return null;
            }
        }

        // 取餐饮应用 access_token（每次现取，与 WeComController.GetToken 同模式；调用量小无需缓存）
        [NonAction]
        public async Task<string?> GetToken(string batchId, string purpose)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid=" + CORP_ID + "&corpsecret=" + AGENT_SECRET;
            WebApiLog tokenLog = await _mH.PerformRequest(url, "", "", "GET", "企业微信", purpose, "获取餐饮应用Token", batchId);
            try
            {
                WeComSendResponse res = JsonConvert.DeserializeObject<WeComSendResponse>(tokenLog.response);
                return res.errcode == 0 ? res.access_token : null;
            }
            catch
            {
                return null;
            }
        }

        // 企业 jsapi_ticket（config 用）+ 应用 jsapi_ticket（agentConfig 用），带缓存；任一失败返回 null
        [NonAction]
        public async Task<(string?, string?)> GetJsApiTickets(string batchId)
        {
            if (IsFresh(_corpTicket) && IsFresh(_agentTicket))
            {
                return (_corpTicket!.ticket, _agentTicket!.ticket);
            }
            await _ticketLock.WaitAsync();
            try
            {
                if (!IsFresh(_corpTicket) || !IsFresh(_agentTicket))
                {
                    string? token = await GetToken(batchId, "JS-SDK签名");
                    if (token == null)
                    {
                        return (null, null);
                    }
                    if (!IsFresh(_corpTicket))
                    {
                        _corpTicket = await FetchTicket(
                            "https://qyapi.weixin.qq.com/cgi-bin/get_jsapi_ticket?access_token=" + token,
                            "获取企业jsapi_ticket", batchId);
                    }
                    if (!IsFresh(_agentTicket))
                    {
                        _agentTicket = await FetchTicket(
                            "https://qyapi.weixin.qq.com/cgi-bin/ticket/get?access_token=" + token + "&type=agent_config",
                            "获取应用jsapi_ticket", batchId);
                    }
                }
                return (_corpTicket?.ticket, _agentTicket?.ticket);
            }
            finally
            {
                _ticketLock.Release();
            }
        }

        private static bool IsFresh(CachedTicket? t)
        {
            return t != null && t.expireAt > DateTime.Now;
        }

        private async Task<CachedTicket?> FetchTicket(string url, string memo, string batchId)
        {
            WebApiLog log = await _mH.PerformRequest(url, "", "", "GET", "企业微信", "JS-SDK签名", memo, batchId);
            try
            {
                WeComTicketResponse res = JsonConvert.DeserializeObject<WeComTicketResponse>(log.response);
                if (res == null || res.errcode != 0 || string.IsNullOrEmpty(res.ticket))
                {
                    return null;
                }
                return new CachedTicket()
                {
                    ticket = res.ticket,
                    expireAt = DateTime.Now.AddSeconds(Math.Max(res.expires_in - 300, 60))
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

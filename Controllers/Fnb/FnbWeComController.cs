using System;
using System.Collections.Generic;
using System.Linq;
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

        // ====== API ======

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
    }
}

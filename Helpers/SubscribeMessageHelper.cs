using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 小程序订阅消息发送。
    ///
    /// 注意跟 ServiceMessageController 区分：那个发的是**公众号**模板消息
    /// （cgi-bin/message/template/send，token 来自 weixin.snowmeet.top/get_token.aspx），
    /// 这里发的是**小程序**订阅消息（cgi-bin/message/subscribe/send，token 是小程序 appid 的），
    /// 两套 token 不能混用。
    ///
    /// 之所以要有这个类而不是继续在业务代码里拼字符串：既有的微信 JSON 全是手工字符串拼接，
    /// 券名里只要出现一个引号就会拼出非法 JSON。这里走真正的序列化。
    /// </summary>
    public class SubscribeMessageHelper
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public SubscribeMessageHelper(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        /// <summary>
        /// 构造 subscribeMessage.send 的请求体。纯函数，单元测试覆盖。
        /// </summary>
        public static string BuildPayload(string openId, string templateId, string page,
            Dictionary<string, string> data, string miniprogramState = "formal")
        {
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                ["touser"] = openId,
                ["template_id"] = templateId,
                ["page"] = page,
                ["miniprogram_state"] = miniprogramState,
                ["lang"] = "zh_CN",
                ["data"] = data.ToDictionary(
                    kv => kv.Key,
                    kv => (object)new Dictionary<string, string>() { ["value"] = kv.Value })
            };
            // UnsafeRelaxedJsonEscaping：让中文原样输出而不是 \uXXXX。
            // 引号等 JSON 结构字符仍会被正确转义，安全性不受影响。
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        /// <summary>
        /// 发送并落库留痕。返回微信原始响应体，由调用方判断成败。
        /// 落库放在这里而不是各调用方，是为了保证每一次发送都有记录。
        /// </summary>
        public async Task<string> Send(string openId, string templateId, string page,
            Dictionary<string, string> data, string miniprogramState = "formal")
        {
            string json = BuildPayload(openId, templateId, page, data, miniprogramState);
            string token = new MiniAppHelperController(_db, _config).GetAccessToken();
            string url = "https://api.weixin.qq.com/cgi-bin/message/subscribe/send?access_token=" + token.Trim();
            string ret = Util.GetWebContent(url, json);
            try
            {
                await _db.AddAsync(new TemplateMessage()
                {
                    template_id = templateId,
                    from = Settings.GetSettings(_config).appId.Trim(),
                    to = openId,
                    first = "",
                    keywords = json,
                    remark = "小程序订阅消息",
                    url = page,
                    ret_message = ret == null ? "" : ret
                });
                await _db.SaveChangesAsync();
            }
            catch
            {
                // 留痕失败不能影响已经发出去的消息
            }
            return ret;
        }
    }
}

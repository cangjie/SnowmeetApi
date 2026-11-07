using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 万龙自我游（供应商）订单回调 POST core/WanlongZiwoyouHelper/CallBack 的记录：
    /// 把收到的原文和时间、订单号/订单状态摘要写进 web_api_log，事后能对上「哪一条通知是哪张票什么状态」。
    ///
    /// 这个接口是公开的（供应商不带登录），所以：正文最多存 8000 字符，超出截断并标记；
    /// 请求头只留 Content-Type 和 User-Agent，不存授权、Cookie 等。
    /// </summary>
    public static class ZiwoyouCallbackLog
    {
        public const int MAX_BODY_CHARS = 8000;
        public const string TRUNCATED_MARK = "（已截断）";
        public const string SOURCE = "万龙自我游";
        public const string PURPOSE = "回调通知";

        // web_api_log 的列宽（varchar）：超了插入会失败，记录就丢了，所以写入前先截断
        public const int MAX_MEMO = 500;
        public const int MAX_URL = 500;
        public const int MAX_HEADER = 1000;

        static string Cap(string? s, int max) => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max));

        /// <summary>读正文，最多读 MAX_BODY_CHARS 个字符，超出的截断并加标记（不会把超大请求体全读进内存）。</summary>
        public static async Task<string> ReadBodyAsync(HttpRequest request)
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, true, 1024, leaveOpen: true);
            var buffer = new char[MAX_BODY_CHARS + 1];
            int n = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            return n > MAX_BODY_CHARS ? new string(buffer, 0, MAX_BODY_CHARS) + TRUNCATED_MARK : new string(buffer, 0, n);
        }

        /// <summary>备注：从 JSON 里取订单号和订单状态（根级优先，其次 data 里），不是 JSON 或没有就是空串。</summary>
        public static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            JObject? root;
            try { root = JToken.Parse(body) as JObject; }
            catch { return ""; }
            if (root == null) return "";
            var data = root["data"] as JObject;
            var parts = new List<string>();
            foreach (string key in new[] { "orderId", "orderState" })
            {
                JToken? v = root[key];
                if (v == null || v.Type == JTokenType.Null || v.Type == JTokenType.Object || v.Type == JTokenType.Array) v = data?[key];
                if (v != null && v.Type != JTokenType.Null && v.Type != JTokenType.Object && v.Type != JTokenType.Array)
                {
                    parts.Add(key + "=" + v.ToString());
                }
            }
            return Cap(string.Join(" ", parts), MAX_MEMO);
        }

        public static WebApiLog Build(HttpRequest request, string body, DateTime now)
        {
            string userAgent = request.Headers["User-Agent"].ToString();
            return new WebApiLog
            {
                source = SOURCE,
                purpose = PURPOSE,
                memo = Summarize(body),
                method = request.Method,
                request_url = Cap(request.Path.Value + request.QueryString.Value, MAX_URL),
                header = Cap("Content-Type: " + (request.ContentType ?? "") + "\nUser-Agent: " + userAgent, MAX_HEADER),
                payload = body,
                response = "",
                deal = 0,
                create_date = now
            };
        }
    }
}

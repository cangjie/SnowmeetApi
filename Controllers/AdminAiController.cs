using System;
using System.Diagnostics;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AdminAiController : ControllerBase
    {
        private const int MinStaffLevel = 200;
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public AdminAiController(ApplicationDBContext db, IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        public class HelpRequest
        {
            public string page_key { get; set; } = "";
            public string? question { get; set; }
            public JsonElement? business_context { get; set; }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> GetPageHelpByStaff([FromBody] HelpRequest request,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            return await AskReqai(request, "page_help", "请说明当前页面的用途、标准操作步骤、关键限制和常见错误。",
                sessionKey, sessionType);
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> AskPageHelpByStaff([FromBody] HelpRequest request,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (string.IsNullOrWhiteSpace(request.question))
            {
                return BadRequest(new ApiResult<object> { code = 1, message = "请输入问题" });
            }
            return await AskReqai(request, "follow_up", request.question.Trim(), sessionKey, sessionType);
        }

        private async Task<ActionResult<ApiResult<object>>> AskReqai(HelpRequest request, string operation, string prompt,
            string sessionKey, string sessionType)
        {
            if (string.IsNullOrWhiteSpace(request.page_key) || !request.page_key.StartsWith("pages/admin/", StringComparison.Ordinal))
            {
                return BadRequest(new ApiResult<object> { code = 1, message = "页面标识不合法" });
            }
            Staff? staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < MinStaffLevel)
            {
                return Ok(new ApiResult<object> { code = 1, message = "没有权限" });
            }

            string traceId = Guid.NewGuid().ToString("N");
            string baseUrl = _config["Reqai:BaseUrl"]?.TrimEnd('/') ?? "";
            string endpoint = baseUrl + "/api/service/page-help";
            string payload = JsonSerializer.Serialize(new
            {
                page_key = request.page_key,
                operation,
                prompt,
                staff_id = staff.id,
                trace_id = traceId,
                business_context = request.business_context
            });
            AdminAiRequestLog log = new AdminAiRequestLog
            {
                staff_id = staff.id,
                session_type = sessionType,
                trace_id = traceId,
                operation = operation,
                page_key = request.page_key,
                request_url = endpoint,
                request_payload = payload
            };
            await _db.AddAsync(log);
            await _db.SaveChangesAsync();
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                string token = _config["Reqai:ServiceToken"] ?? "";
                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("reqai 服务地址或服务凭据未配置");
                }
                HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                httpRequest.Headers.Add("X-Snowmeet-Service-Token", token);
                HttpResponseMessage response = await _httpClientFactory.CreateClient("Reqai").SendAsync(httpRequest);
                string responsePayload = await response.Content.ReadAsStringAsync();
                log.response_payload = responsePayload;
                log.response_status_code = (int)response.StatusCode;
                log.response_headers = JsonSerializer.Serialize(response.Headers.Concat(response.Content.Headers)
                    .ToDictionary(header => header.Key, header => string.Join(",", header.Value)));
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException("reqai 返回 HTTP " + (int)response.StatusCode + "：" + responsePayload);
                }
                using JsonDocument responseJson = JsonDocument.Parse(responsePayload);
                JsonElement root = responseJson.RootElement;
                log.model = root.TryGetProperty("model", out JsonElement model) ? model.GetString() : null;
                log.effort = root.TryGetProperty("effort", out JsonElement effort) ? effort.GetString() : null;
                log.reqai_invocation_id = root.TryGetProperty("invocation_id", out JsonElement invocation)
                    ? invocation.ToString() : null;
                log.usage = root.TryGetProperty("usage", out JsonElement usage) ? usage.GetRawText() : null;
                log.success = true;
                log.completed_date = DateTime.Now;
                log.duration_ms = (int)stopwatch.ElapsedMilliseconds;
                _db.Update(log);
                await _db.SaveChangesAsync();
                return Ok(new ApiResult<object> { data = new { traceId, auditId = log.id, result = root.Clone() } });
            }
            catch (Exception error)
            {
                log.success = false;
                log.error_message = error.ToString();
                log.completed_date = DateTime.Now;
                log.duration_ms = (int)stopwatch.ElapsedMilliseconds;
                _db.Update(log);
                await _db.SaveChangesAsync();
                return StatusCode(502, new ApiResult<object> { code = 1, message = "管理员帮助服务暂不可用", data = new { traceId, auditId = log.id } });
            }
        }
    }
}
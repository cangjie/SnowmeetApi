using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AdminAiController(ApplicationDBContext db, IConfiguration config, IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
        }

        public class HelpRequest
        {
            public string page_key { get; set; } = "";
            public string? question { get; set; }
            public JsonElement? business_context { get; set; }
        }

        public class NaturalLanguageQueryRequest
        {
            public string question { get; set; } = "";
        }

        private class RentQueryIntentResponse
        {
            public RentQueryIntent? intent { get; set; }
            public string? model { get; set; }
            public string? effort { get; set; }
        }

        private class RentQueryIntent
        {
            public string status { get; set; } = "";
            public DateTime? start_date { get; set; }
            public DateTime? end_date { get; set; }
            public string? shop { get; set; }
            public string? rent_status { get; set; }
            public bool? is_test { get; set; }
            public bool? is_entertain { get; set; }
            public bool? have_discount { get; set; }
            public bool? use_card { get; set; }
            public string? cell_suffix { get; set; }
            public string? keyword { get; set; }
            public string? clarification { get; set; }
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

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> QueryRentOrdersByNaturalLanguage(
            [FromBody] NaturalLanguageQueryRequest request, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            if (string.IsNullOrWhiteSpace(request.question))
            {
                return BadRequest(new ApiResult<object> { code = 1, message = "请输入查询条件" });
            }
            Staff? staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object> { code = 1, message = "没有权限" });
            }

            string traceId = Guid.NewGuid().ToString("N");
            string baseUrl = _config["Reqai:BaseUrl"]?.TrimEnd('/') ?? "";
            string endpoint = baseUrl + "/api/service/rent-query-intent";
            string payload = JsonSerializer.Serialize(new { question = request.question.Trim() });
            AdminAiRequestLog log = new AdminAiRequestLog
            {
                staff_id = staff.id, session_type = sessionType, trace_id = traceId,
                operation = "rent_query", page_key = "pages/admin/rent/new_rent_list",
                request_url = endpoint, request_payload = payload
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
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException("reqai 返回 HTTP " + (int)response.StatusCode + "：" + responsePayload);
                }
                RentQueryIntentResponse? parsed = JsonSerializer.Deserialize<RentQueryIntentResponse>(responsePayload);
                RentQueryIntent? intent = parsed?.intent;
                if (intent == null || (intent.status != "ready" && intent.status != "clarification_required" && intent.status != "unsupported"))
                {
                    throw new InvalidOperationException("reqai 返回了无效查询条件");
                }
                log.model = parsed?.model;
                log.effort = parsed?.effort;
                if (intent.status != "ready")
                {
                    await CompleteQueryLog(log, stopwatch, true);
                    return Ok(new ApiResult<object> { data = new { traceId, auditId = log.id, intent.status, intent.clarification } });
                }
                ValidateRentIntent(intent);
                OrderController orderController = new OrderController(_db, _config, _httpContextAccessor);
                List<SnowmeetApi.Models.Order> orders = await orderController.GetCommonOrders(null, intent.shop, null, null, "租赁",
                    intent.start_date, intent.end_date, null, intent.is_test, intent.is_entertain, null, null,
                    intent.have_discount, null, null, null, null, intent.keyword, null, null, null,
                    null, intent.use_card, intent.cell_suffix, intent.rent_status, null);
                if (orders.Count > 200) orders = orders.OrderByDescending(o => o.biz_date).Take(200).ToList();
                var statusCounts = orders.GroupBy(o => o.rentProperties?.rentStatus ?? "临时订单")
                    .ToDictionary(group => group.Key, group => group.Count());
                double chargeTotal = orders.Sum(o => o.totalCharge);
                double paidTotal = orders.Sum(o => o.paidAmount);
                double refundTotal = orders.Sum(o => o.refundAmount);
                int unpaidCount = orders.Count(o => o.paidAmount < o.totalCharge && o.closed == 0);
                await CompleteQueryLog(log, stopwatch, true);
                return Ok(new ApiResult<object>
                {
                    data = new
                    {
                        traceId,
                        auditId = log.id,
                        intent = new { intent.start_date, intent.end_date, intent.shop, intent.rent_status,
                                       intent.is_test, intent.is_entertain, intent.have_discount, intent.use_card,
                                       intent.cell_suffix, intent.keyword },
                        summary = new { total = orders.Count, chargeTotal, paidTotal, refundTotal, unpaidCount, statusCounts,
                                        note = orders.Count == 200 ? "结果最多展示 200 单，请继续缩小条件。" : "统计基于当前筛选结果。" }
                    }
                });
            }
            catch (Exception error)
            {
                await CompleteQueryLog(log, stopwatch, false, error);
                return StatusCode(502, new ApiResult<object> { code = 1, message = "数据查询服务暂不可用", data = new { traceId, auditId = log.id } });
            }
        }

        private static void ValidateRentIntent(RentQueryIntent intent)
        {
            string[] statuses = { "未支付", "未开始", "租赁中", "部分归还", "全部归还", "部分退押金", "全额退押金", "了结关闭", "临时订单" };
            if (intent.rent_status != null && !statuses.Contains(intent.rent_status)) throw new InvalidOperationException("租赁状态不支持");
            if (intent.start_date == null || intent.end_date == null) throw new InvalidOperationException("请明确查询日期范围");
            if (intent.end_date < intent.start_date || intent.end_date > intent.start_date.Value.AddDays(365)) throw new InvalidOperationException("查询日期范围不能超过 365 天");
            if (intent.cell_suffix != null && (intent.cell_suffix.Length < 4 || !intent.cell_suffix.All(char.IsDigit))) throw new InvalidOperationException("手机号条件不合法");
        }

        private async Task CompleteQueryLog(AdminAiRequestLog log, Stopwatch stopwatch, bool success, Exception? error = null)
        {
            log.success = success;
            log.error_message = error?.ToString();
            log.completed_date = DateTime.Now;
            log.duration_ms = (int)stopwatch.ElapsedMilliseconds;
            _db.Update(log);
            await _db.SaveChangesAsync();
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
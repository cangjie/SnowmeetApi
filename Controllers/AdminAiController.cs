using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;
using SnowmeetApi.Services.AdminAssistant;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AdminAiController : ControllerBase
    {
        private const int MinStaffLevel = 200;
        private static readonly Regex PhoneLike = new(@"(?<!\d)(?:\+?86[\s-]*)?1[3-9]\d(?:[\s-]*\d{4}){2}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex SensitiveCredential = new("(?<![\\p{L}\\p{N}_-])(?:authorization|password|secret|api[_-]?key|service[_-]?token|access[_-]?token|refresh[_-]?token|token|cookie|openid|payment(?:[_-]?(?:id|no|token))?|transaction(?:[_-]?id)?)\\b\\s*(?:=\\s*|:\\s*|\"\\s*:\\s*\")(?:\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[^\\r\\n,;，；}\\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAdminAssistantService _adminAssistant;

        public AdminAiController(ApplicationDBContext db, IConfiguration config, IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor, IAdminAssistantService adminAssistant)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _adminAssistant = adminAssistant;
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

        [HttpPost]
        public async Task<ActionResult<ApiResult<AdminAssistantResponse>>> AskAdminAssistantByStaff(
            [ModelBinder(BinderType = typeof(AdminAssistantRequestModelBinder))] AdminAssistantRequest? request, string sessionKey,
            string sessionType = "wechat_mini_openid", CancellationToken cancellationToken = default)
        {
            if (!IsValidClientRequest(request))
            {
                return BadRequest(new ApiResult<AdminAssistantResponse> { code = 1, message = "请求参数不合法" });
            }

            AdminAssistantRequest validRequest = request!;
            validRequest.question = validRequest.question.Trim();
            Staff? staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null)
            {
                string deniedTraceId = Guid.NewGuid().ToString("N");
                AdminAssistantResponse deniedResponse = FailureResponse(deniedTraceId, "没有权限");
                return StatusCode(403, new ApiResult<AdminAssistantResponse>
                {
                    code = 1,
                    message = "没有权限",
                    data = deniedResponse
                });
            }

            string traceId = Guid.NewGuid().ToString("N");
            bool structuredEnabled = _config.GetValue<bool>("AdminAssistant:StructuredProtocolEnabled");
            return await ExecuteAndAudit(validRequest, staff, traceId, structuredEnabled, sessionType, cancellationToken);
        }

        private async Task<ActionResult<ApiResult<AdminAssistantResponse>>> ExecuteAndAudit(AdminAssistantRequest request,
            Staff staff, string traceId, bool structuredEnabled, string sessionType, CancellationToken cancellationToken)
        {
            AdminAiRequestLog log = new()
            {
                staff_id = staff.id,
                session_type = sessionType,
                trace_id = traceId,
                provider = structuredEnabled ? "reqai" : "reqai_legacy",
                operation = "admin_assistant",
                page_key = request.page_key,
                request_url = "/api/AdminAi/AskAdminAssistantByStaff",
                request_payload = SafeAuditJson(new
                {
                    request.version,
                    request.page_key,
                    request.question,
                    request.conversation,
                    request.context,
                    structured_protocol_enabled = structuredEnabled
                })
            };
            await _db.AddAsync(log, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                AdminAssistantExecutionResult execution = await _adminAssistant.AskAsync(
                    request, staff, traceId, structuredEnabled, cancellationToken);
                ApiResult<AdminAssistantResponse> response = new() { data = execution.response };
                await CompleteAssistantLog(log, stopwatch, true, 200, execution.audit, response);
                return Ok(response);
            }
            catch (AdminAssistantClarificationException error)
            {
                AdminAssistantResponse response = FailureResponse(traceId, "请补充查询条件后重试。");
                ApiResult<AdminAssistantResponse> body = new() { data = response };
                await CompleteAssistantLog(log, stopwatch, true, 200, error.audit, body);
                return Ok(body);
            }
            catch (AdminAssistantPermissionException error)
            {
                AdminAssistantResponse response = FailureResponse(traceId, "没有权限");
                ApiResult<AdminAssistantResponse> body = new() { code = 1, message = "没有权限", data = response };
                await CompleteAssistantLog(log, stopwatch, false, 403, error.audit, body);
                return StatusCode(403, body);
            }
            catch (AdminAssistantUnsupportedException error)
            {
                // 明确拒答不是失败：店员拿到的是「这个查不了 + 去哪儿自己筛」，
                // 200 返回，审计里记下原因，便于统计店员最想要哪个还没做的能力。
                AdminAssistantResponse response = FailureResponse(traceId, error.Message);
                ApiResult<AdminAssistantResponse> body = new() { data = response };
                await CompleteAssistantLog(log, stopwatch, true, 200, error.audit, body);
                return Ok(body);
            }
            catch (AdminAssistantOperationException error)
            {
                string message = error.stage == AdminAssistantFailureStage.Execution
                    ? (error.domainLabel ?? "订单") + "查询暂不可用，请稍后重试。"
                    : "管理员助手服务暂不可用，请稍后重试。";
                AdminAssistantResponse response = FailureResponse(traceId, message);
                ApiResult<AdminAssistantResponse> body = new() { code = 1, message = message, data = response };
                await CompleteAssistantLog(log, stopwatch, false, 502, error.audit, body);
                return StatusCode(502, body);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                AdminAssistantResponse response = FailureResponse(traceId, "管理员助手服务暂不可用，请稍后重试。");
                ApiResult<AdminAssistantResponse> body = new() { code = 1, message = response.reply.text, data = response };
                await CompleteAssistantLog(log, stopwatch, false, 502,
                    new AdminAssistantAuditData { validation_result = "rejected", error = "unexpected_failure" }, body);
                return StatusCode(502, body);
            }
        }

        private static bool IsValidClientRequest(AdminAssistantRequest? request)
        {
            if (request == null || request.version != "1" || string.IsNullOrWhiteSpace(request.page_key) ||
                !request.page_key.StartsWith("pages/admin/", StringComparison.Ordinal) || request.page_key.Length > 255 ||
                request.question == null || request.question.Trim().Length is < 1 or > 2000 || request.conversation == null ||
                request.conversation.Count > 20)
            {
                return false;
            }

            int totalLength = 0;
            foreach (AssistantConversationMessage message in request.conversation)
            {
                if (message == null || (message.role != "user" && message.role != "assistant") || message.content == null ||
                    message.content.Length > 2000)
                {
                    return false;
                }
                totalLength += message.content.Length;
                if (totalLength > 12000) return false;
            }
            return true;
        }

        private static AdminAssistantResponse FailureResponse(string traceId, string text) => new()
        {
            version = "1",
            trace_id = traceId,
            reply = new AssistantReply { text = text },
            actions = new List<ClientAssistantAction>(),
            context = new AdminAssistantContext()
        };

        private async Task CompleteAssistantLog(AdminAiRequestLog log, Stopwatch stopwatch, bool success, int statusCode,
            AdminAssistantAuditData audit, ApiResult<AdminAssistantResponse> response)
        {
            log.success = success;
            log.response_status_code = statusCode;
            log.response_payload = SafeAuditJson(new
            {
                response,
                audit = new
                {
                    planner = SafePlannerAudit(audit.planner_json),
                    audit.validation_result,
                    audit.query,
                    audit.summary,
                    audit.error
                }
            });
            log.error_message = RedactAuditText(audit.error);
            log.completed_date = DateTime.Now;
            log.duration_ms = (int)stopwatch.ElapsedMilliseconds;
            _db.Update(log);
            await _db.SaveChangesAsync();
        }

        private static Dictionary<string, object?> SafePlannerAudit(string? plannerJson)
        {
            if (string.IsNullOrWhiteSpace(plannerJson))
            {
                return new Dictionary<string, object?> { ["planner_payload_status"] = "missing" };
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(plannerJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new Dictionary<string, object?> { ["planner_payload_status"] = "invalid_shape" };
                }
                return new Dictionary<string, object?>
                {
                    ["planner_payload_status"] = HasOnlyProperties(document.RootElement, "version", "reply", "actions", "model", "effort")
                        ? "parsed" : "rejected_shape",
                    ["planner"] = ProjectPlannerProtocol(document.RootElement)
                };
            }
            catch (JsonException)
            {
                return new Dictionary<string, object?> { ["planner_payload_status"] = "malformed" };
            }
        }

        private static Dictionary<string, object?> ProjectPlannerProtocol(JsonElement payload)
        {
            Dictionary<string, object?> result = new();
            if (payload.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.String)
                result["version"] = SafePlannerString(version.GetString(), 32);
            if (payload.TryGetProperty("reply", out JsonElement reply) && reply.ValueKind == JsonValueKind.Object)
                result["reply"] = ProjectPlannerReply(reply);
            if (payload.TryGetProperty("actions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array)
            {
                List<Dictionary<string, object?>> projectedActions = new();
                foreach (JsonElement action in actions.EnumerateArray())
                {
                    if (action.ValueKind != JsonValueKind.Object) continue;
                    Dictionary<string, object?> projected = new();
                    CopyPlannerString(action, projected, "id", 64);
                    CopyPlannerString(action, projected, "type", 64);
                    CopyPlannerString(action, projected, "mode", 16);
                    if (action.TryGetProperty("arguments", out JsonElement arguments) && arguments.ValueKind == JsonValueKind.Object)
                        projected["arguments"] = ProjectPlannerArguments(arguments);
                    if (action.TryGetProperty("aggregation", out JsonElement aggregation) && aggregation.ValueKind == JsonValueKind.Object)
                        projected["aggregation"] = ProjectPlannerAggregation(aggregation);
                    projectedActions.Add(projected);
                }
                result["actions"] = projectedActions;
            }
            return result;
        }

        private static Dictionary<string, object?> ProjectPlannerReply(JsonElement reply)
        {
            Dictionary<string, object?> result = new();
            CopyPlannerString(reply, result, "text", 2000);
            if (reply.TryGetProperty("citations", out JsonElement citations) && citations.ValueKind == JsonValueKind.Array &&
                citations.GetArrayLength() == 0)
                result["citations"] = new List<object?>();
            return result;
        }

        private static Dictionary<string, object?> ProjectPlannerArguments(JsonElement arguments)
        {
            Dictionary<string, object?> result = new();
            CopyPlannerString(arguments, result, "start_date", 10);
            CopyPlannerString(arguments, result, "end_date", 10);
            CopyPlannerString(arguments, result, "shop", 64);
            CopyPlannerString(arguments, result, "rent_status", 64);
            CopyPlannerBoolean(arguments, result, "is_test");
            CopyPlannerBoolean(arguments, result, "is_entertain");
            CopyPlannerBoolean(arguments, result, "have_discount");
            CopyPlannerBoolean(arguments, result, "use_card");
            CopyPlannerBoolean(arguments, result, "has_retail");
            CopyPlannerCellSuffix(arguments, result);
            CopyPlannerString(arguments, result, "keyword", 100);
            return result;
        }

        private static Dictionary<string, object?> ProjectPlannerAggregation(JsonElement aggregation)
        {
            Dictionary<string, object?> result = new();
            CopyPlannerStringArray(aggregation, result, "metrics", 32);
            CopyPlannerStringArray(aggregation, result, "group_by", 32);
            return result;
        }

        private static void CopyPlannerString(JsonElement source, Dictionary<string, object?> target, string property, int maximumLength)
        {
            if (!source.TryGetProperty(property, out JsonElement value)) return;
            if (value.ValueKind == JsonValueKind.Null)
                target[property] = null;
            else if (value.ValueKind == JsonValueKind.String)
                target[property] = SafePlannerString(value.GetString(), maximumLength);
        }

        private static void CopyPlannerBoolean(JsonElement source, Dictionary<string, object?> target, string property)
        {
            if (!source.TryGetProperty(property, out JsonElement value)) return;
            if (value.ValueKind == JsonValueKind.Null)
                target[property] = null;
            else if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                target[property] = value.GetBoolean();
        }

        private static void CopyPlannerCellSuffix(JsonElement source, Dictionary<string, object?> target)
        {
            if (!source.TryGetProperty("cell_suffix", out JsonElement value)) return;
            if (value.ValueKind == JsonValueKind.Null)
                target["cell_suffix"] = null;
            else if (value.ValueKind == JsonValueKind.String)
                target["cell_suffix"] = SafeCellSuffix(value.GetString());
        }

        private static void CopyPlannerStringArray(JsonElement source, Dictionary<string, object?> target, string property, int maximumLength)
        {
            if (!source.TryGetProperty(property, out JsonElement values)) return;
            if (values.ValueKind == JsonValueKind.Null)
            {
                target[property] = null;
                return;
            }
            if (values.ValueKind != JsonValueKind.Array) return;
            List<string> projected = new();
            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String)
                    projected.Add(SafePlannerString(value.GetString(), maximumLength));
            }
            target[property] = projected;
        }

        private static string SafePlannerString(string? value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            string normalized = value.Length > maximumLength ? value.Substring(0, maximumLength) : value;
            return RedactAuditText(normalized) ?? "";
        }

        private static string SafeCellSuffix(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return value.Length <= 4 ? value : value.Substring(value.Length - 4);
        }

        private static bool HasOnlyProperties(JsonElement element, params string[] allowed)
        {
            HashSet<string> names = new(allowed, StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Contains(property.Name) || !seen.Add(property.Name)) return false;
            }
            return true;
        }

        private static string SafeAuditJson(object value)
        {
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            using MemoryStream stream = new();
            using Utf8JsonWriter writer = new(stream);
            WriteSafeAuditJson(writer, document.RootElement);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteSafeAuditJson(Utf8JsonWriter writer, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        if (IsSensitiveAuditProperty(property.Name)) continue;
                        writer.WritePropertyName(property.Name);
                        if (IsCellSuffixAuditProperty(property.Name))
                        {
                            if (property.Value.ValueKind == JsonValueKind.String)
                                writer.WriteStringValue(SafeCellSuffix(property.Value.GetString()));
                            else
                                property.Value.WriteTo(writer);
                            continue;
                        }
                        WriteSafeAuditJson(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in value.EnumerateArray()) WriteSafeAuditJson(writer, item);
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(RedactAuditText(value.GetString()));
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }

        private static bool IsSensitiveAuditProperty(string name)
        {
            string normalized = name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
            return normalized.Contains("token", StringComparison.Ordinal) || normalized.Contains("cookie", StringComparison.Ordinal) ||
                normalized.Contains("secret", StringComparison.Ordinal) || normalized.Contains("authorization", StringComparison.Ordinal) ||
                normalized.Contains("openid", StringComparison.Ordinal) || normalized.Contains("payment", StringComparison.Ordinal) ||
                normalized.Contains("transaction", StringComparison.Ordinal) || normalized is "orders" or "order" or "orderrows" or
                "orderdetails" or "orderitems" or "contactname" or "realname" or "cell" or "phone" or "mobile";
        }

        private static bool IsCellSuffixAuditProperty(string name) =>
            string.Equals(name, "cell_suffix", StringComparison.Ordinal);

        private static string? RedactAuditText(string? value)
        {
            if (value == null) return null;
            string withoutPhones = PhoneLike.Replace(value, "[已隐去手机号]");
            return SensitiveCredential.Replace(withoutPhones, "[已隐去敏感信息]");
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
                orders = OrderQueryRules.FilterByCustomerCellSuffix(orders, intent.cell_suffix);
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

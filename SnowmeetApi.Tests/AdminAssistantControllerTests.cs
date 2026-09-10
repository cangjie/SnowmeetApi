using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;
using SnowmeetApi.Services.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class AdminAssistantControllerTests
    {
        [Fact]
        public async Task 无效v1请求返回400且不调用助手()
        {
            RejectingAssistantService assistant = new();
            AdminAiController controller = new(null!, Configuration(), new EmptyHttpClientFactory(),
                new HttpContextAccessor(), assistant);

            ActionResult<ApiResult<AdminAssistantResponse>> action = await controller.AskAdminAssistantByStaff(new AdminAssistantRequest
            {
                version = "2",
                page_key = "pages/admin/member/member_list",
                question = "这个页面怎么用"
            }, "trusted-session");

            BadRequestObjectResult result = Assert.IsType<BadRequestObjectResult>(action.Result);
            ApiResult<AdminAssistantResponse> body = Assert.IsType<ApiResult<AdminAssistantResponse>>(result.Value);
            Assert.Equal(1, body.code);
            Assert.Equal("请求参数不合法", body.message);
            Assert.False(assistant.wasCalled);
        }

        [Fact]
        public async Task 失败会写入脱敏审计并返回清空的查询上下文()
        {
            AdminAiRequestLog audit = await RecordPlannerFailure("{\"trace\":\"trace-secret\",\"service_token\":\"secret\",\"Cookie\":\"cookie\",\"openid\":\"openid\",\"orders\":[{\"id\":1}]}");

            Assert.Equal("admin_assistant", audit.operation);
            Assert.Equal(7, audit.staff_id);
            Assert.False(audit.success);
            Assert.Equal("planner_failed", audit.error_message);
            Assert.Equal(502, audit.response_status_code);
            Assert.DoesNotContain("trace-secret", audit.response_payload);
            Assert.Contains("validation_result", audit.response_payload);
            Assert.DoesNotContain("service_token", audit.response_payload);
            Assert.DoesNotContain("Cookie", audit.response_payload);
            Assert.DoesNotContain("openid", audit.response_payload);
            Assert.DoesNotContain("orders", audit.response_payload);
            Assert.DoesNotContain("database details", audit.response_payload);
        }

        [Theory]
        [InlineData("service_token=secret; Cookie=secret; contact_name=Jane; cell=13800138000")]
        [InlineData("{\"version\":\"1\",\"reply\":{\"text\":\"safe\",\"citations\":[]},\"actions\":[],\"debug\":{\"orders\":[{\"contact_name\":\"Jane\",\"cell\":\"13800138000\"}]}}")]
        public async Task 拒绝的规划内容只记录安全协议投影(string plannerJson)
        {
            AdminAiRequestLog audit = await RecordPlannerFailure(plannerJson);

            Assert.DoesNotContain("secret", audit.response_payload);
            Assert.DoesNotContain("Cookie", audit.response_payload);
            Assert.DoesNotContain("Jane", audit.response_payload);
            Assert.DoesNotContain("13800138000", audit.response_payload);
            Assert.DoesNotContain("orders", audit.response_payload);
            Assert.DoesNotContain("debug", audit.response_payload);
            Assert.Contains("planner_payload_status", audit.response_payload);
        }

        [Fact]
        public async Task 成功审计保留安全规划细节且手机号仅记录后四位()
        {
            const string fullCell = "13800138000";
            const string formattedPhone = "+86 138 0013 8000";
            const string bearer = "TOP-SECRET-ABC";
            const string password = "quoted password value";
            const string apiKey = "multi token credential";
            const string plannerJson = "{\"version\":\"1\",\"reply\":{\"text\":\"我来查询。\",\"citations\":[]},\"actions\":[{\"id\":\"a1\",\"type\":\"rental_order.query\",\"mode\":\"replace\",\"arguments\":{\"start_date\":\"2026-04-01\",\"end_date\":\"2026-04-30\",\"shop\":\"万龙\",\"rent_status\":null,\"has_retail\":true,\"cell_suffix\":\"13800138000\",\"keyword\":\"雪板\"},\"aggregation\":{\"metrics\":[\"order_count\",\"charge_total\"],\"group_by\":[\"shop\"]}}]}";
            AdminAssistantRequest request = ValidRequest();
            request.question = "查询 13800138000，" + formattedPhone + " service_token=planner-secret; Cookie=session-secret; " +
                "openid=member-secret; payment_id=payment-secret; authorization=Bearer " + bearer + " " +
                "password=\"" + password + "\" api-key='" + apiKey + "'";
            request.context.rental_order_query = QueryState(fullCell);

            (ActionResult<ApiResult<AdminAssistantResponse>> action, AdminAiRequestLog audit) =
                await RecordSuccessfulQuery(request, plannerJson, fullCell);

            OkObjectResult result = Assert.IsType<OkObjectResult>(action.Result);
            ApiResult<AdminAssistantResponse> body = Assert.IsType<ApiResult<AdminAssistantResponse>>(result.Value);
            Assert.Equal(fullCell, body.data!.context.rental_order_query!.cell_suffix);
            Assert.Equal(fullCell, Assert.Single(body.data.actions).state.cell_suffix);

            Assert.DoesNotContain(fullCell, audit.request_payload);
            Assert.DoesNotContain(fullCell, audit.response_payload);
            Assert.DoesNotContain("planner-secret", audit.request_payload);
            Assert.DoesNotContain("session-secret", audit.request_payload);
            Assert.DoesNotContain("member-secret", audit.request_payload);
            Assert.DoesNotContain("payment-secret", audit.request_payload);
            Assert.DoesNotContain("auth-secret", audit.request_payload);
            Assert.DoesNotContain(formattedPhone, audit.request_payload);
            Assert.DoesNotContain("138 0013 8000", audit.request_payload);
            Assert.DoesNotContain(bearer, audit.request_payload);
            Assert.DoesNotContain(password, audit.request_payload);
            Assert.DoesNotContain(apiKey, audit.request_payload);
            Assert.Contains("\"cell_suffix\":\"8000\"", audit.request_payload);
            Assert.Contains("\"cell_suffix\":\"8000\"", audit.response_payload);
            JsonElement plannerAction = JsonDocument.Parse(audit.response_payload!).RootElement
                .GetProperty("audit").GetProperty("planner").GetProperty("planner").GetProperty("actions")[0];
            JsonElement arguments = plannerAction.GetProperty("arguments");
            Assert.Equal("replace", plannerAction.GetProperty("mode").GetString());
            Assert.Equal("2026-04-01", arguments.GetProperty("start_date").GetString());
            Assert.Equal(JsonValueKind.Null, arguments.GetProperty("rent_status").ValueKind);
            Assert.True(arguments.GetProperty("has_retail").GetBoolean());
            Assert.Equal("雪板", arguments.GetProperty("keyword").GetString());
            Assert.Equal("8000", arguments.GetProperty("cell_suffix").GetString());
            JsonElement aggregation = plannerAction.GetProperty("aggregation");
            Assert.Equal("order_count", aggregation.GetProperty("metrics")[0].GetString());
            Assert.Equal("charge_total", aggregation.GetProperty("metrics")[1].GetString());
            Assert.Equal("shop", aggregation.GetProperty("group_by")[0].GetString());
            Assert.DoesNotContain("service_token", audit.response_payload);
        }

        [Fact]
        public async Task 失败审计不会保留自由文本中的手机号或敏感标记()
        {
            const string fullCell = "13800138000";
            AdminAssistantRequest request = ValidRequest();
            request.question = "查询 " + fullCell + " token=question-secret cookie=question-cookie authorization=question-auth";
            AdminAiRequestLog audit = await RecordPlannerFailure(
                "malformed service_token=planner-secret; openid=planner-openid; payment_id=planner-payment; authorization=planner-auth; " + fullCell, request,
                "execution failed token=error-secret authorization=error-auth " + fullCell);

            string serialized = audit.request_payload + audit.response_payload + (audit.error_message ?? "");
            Assert.DoesNotContain(fullCell, serialized);
            Assert.DoesNotContain("question-secret", serialized);
            Assert.DoesNotContain("question-cookie", serialized);
            Assert.DoesNotContain("planner-secret", serialized);
            Assert.DoesNotContain("planner-openid", serialized);
            Assert.DoesNotContain("planner-payment", serialized);
            Assert.DoesNotContain("error-secret", serialized);
            Assert.DoesNotContain("question-auth", serialized);
            Assert.DoesNotContain("planner-auth", serialized);
            Assert.DoesNotContain("error-auth", serialized);
            Assert.DoesNotContain("planner_payload_status\":\"malformed\"", audit.request_payload);
            Assert.Contains("planner_payload_status\":\"malformed\"", audit.response_payload);
        }

        [Fact]
        public async Task 审计会完整抹除带分隔符手机号和带引号的多词凭据()
        {
            const string formattedPhone = "+86 138 0013 8000";
            const string bearer = "TOP-SECRET-ABC";
            const string password = "quoted password value";
            const string apiKey = "multi token credential";
            AdminAssistantRequest request = ValidRequest();
            request.question = "查询 " + formattedPhone + " authorization=Bearer " + bearer +
                " password=\"" + password + "\" api-key='" + apiKey + "'";
            request.context.rental_order_query = new RentalOrderQueryState { keyword = formattedPhone + " cookie=ctx cookie" };
            string plannerJson = "{\"version\":\"1\",\"reply\":{\"text\":\"authorization=Bearer " + bearer + "\",\"citations\":[]},\"actions\":[{\"id\":\"a1\",\"type\":\"rental_order.query\",\"mode\":\"patch\",\"arguments\":{\"keyword\":\"" + formattedPhone + " api-key='" + apiKey + "'\"},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}";

            AdminAiRequestLog audit = await RecordPlannerFailure(plannerJson, request,
                "password=\"" + password + "\" authorization=Bearer " + bearer + " " + formattedPhone);

            string serialized = audit.request_payload + audit.response_payload + (audit.error_message ?? "");
            Assert.DoesNotContain(formattedPhone, serialized);
            Assert.DoesNotContain("138 0013 8000", serialized);
            Assert.DoesNotContain("8000", serialized);
            Assert.DoesNotContain(bearer, serialized);
            Assert.DoesNotContain(password, serialized);
            Assert.DoesNotContain(apiKey, serialized);
            Assert.DoesNotContain("ctx cookie", serialized);
        }

        [Fact]
        public async Task 权限拒绝返回403且保留安全v1失败响应()
        {
            using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).Options;
            await using ApplicationDBContext db = new(options);
            await db.Database.EnsureCreatedAsync();
            await AddTrustedStaffSession(db);
            AdminAiController controller = new(db, Configuration(), new EmptyHttpClientFactory(), new HttpContextAccessor(),
                new PermissionAssistantService());

            ActionResult<ApiResult<AdminAssistantResponse>> action = await controller.AskAdminAssistantByStaff(ValidRequest(), "trusted-session");

            ObjectResult result = Assert.IsType<ObjectResult>(action.Result);
            Assert.Equal(403, result.StatusCode);
            ApiResult<AdminAssistantResponse> body = Assert.IsType<ApiResult<AdminAssistantResponse>>(result.Value);
            Assert.Equal(1, body.code);
            Assert.Equal("没有权限", body.message);
            Assert.NotNull(body.data);
            Assert.False(string.IsNullOrWhiteSpace(body.data!.trace_id));
            Assert.Empty(body.data.actions);
            Assert.Null(body.data.context.rental_order_query);
            AdminAiRequestLog audit = await db.adminAiRequestLog.SingleAsync();
            Assert.Equal(403, audit.response_status_code);
            Assert.Equal("permission_denied", audit.error_message);
        }

        [Fact]
        public async Task 未识别的员工会返回403和安全v1失败响应()
        {
            using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).Options;
            await using ApplicationDBContext db = new(options);
            await db.Database.EnsureCreatedAsync();
            RejectingAssistantService assistant = new();
            AdminAiController controller = new(db, Configuration(), new EmptyHttpClientFactory(), new HttpContextAccessor(), assistant);

            ActionResult<ApiResult<AdminAssistantResponse>> action = await controller.AskAdminAssistantByStaff(ValidRequest(), "unknown-session");

            ObjectResult result = Assert.IsType<ObjectResult>(action.Result);
            Assert.Equal(403, result.StatusCode);
            ApiResult<AdminAssistantResponse> body = Assert.IsType<ApiResult<AdminAssistantResponse>>(result.Value);
            Assert.Equal(1, body.code);
            Assert.Equal("没有权限", body.message);
            Assert.NotNull(body.data);
            Assert.False(string.IsNullOrWhiteSpace(body.data!.trace_id));
            Assert.Empty(body.data.actions);
            Assert.Null(body.data.context.rental_order_query);
            Assert.False(assistant.wasCalled);
            Assert.Empty(await db.adminAiRequestLog.ToListAsync());
        }

        private static async Task<AdminAiRequestLog> RecordPlannerFailure(string plannerJson, AdminAssistantRequest? request = null,
            string error = "planner_failed")
        {
            using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).Options;
            await using ApplicationDBContext db = new(options);
            await db.Database.EnsureCreatedAsync();
            await AddTrustedStaffSession(db);
            ThrowingAssistantService assistant = new(plannerJson, error);
            AdminAiController controller = new(db, Configuration(new Dictionary<string, string?>
            {
                ["AdminAssistant:StructuredProtocolEnabled"] = "true"
            }), new EmptyHttpClientFactory(), new HttpContextAccessor(), assistant);

            ActionResult<ApiResult<AdminAssistantResponse>> action = await controller.AskAdminAssistantByStaff(request ?? ValidRequest(), "trusted-session");

            ObjectResult result = Assert.IsType<ObjectResult>(action.Result);
            Assert.Equal(502, result.StatusCode);
            ApiResult<AdminAssistantResponse> body = Assert.IsType<ApiResult<AdminAssistantResponse>>(result.Value);
            Assert.Equal(1, body.code);
            Assert.DoesNotContain("database details", body.message);
            Assert.NotNull(body.data);
            Assert.Empty(body.data!.actions);
            Assert.Null(body.data.context.rental_order_query);
            Assert.True(assistant.wasCalled);
            return await db.adminAiRequestLog.SingleAsync();
        }

        private static async Task<(ActionResult<ApiResult<AdminAssistantResponse>> action, AdminAiRequestLog audit)> RecordSuccessfulQuery(
            AdminAssistantRequest request, string plannerJson, string cellSuffix)
        {
            using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).Options;
            await using ApplicationDBContext db = new(options);
            await db.Database.EnsureCreatedAsync();
            await AddTrustedStaffSession(db);
            SuccessfulAssistantService assistant = new(plannerJson, cellSuffix);
            AdminAiController controller = new(db, Configuration(new Dictionary<string, string?>
            {
                ["AdminAssistant:StructuredProtocolEnabled"] = "true"
            }), new EmptyHttpClientFactory(), new HttpContextAccessor(), assistant);

            ActionResult<ApiResult<AdminAssistantResponse>> action = await controller.AskAdminAssistantByStaff(request, "trusted-session");

            Assert.True(assistant.wasCalled);
            return (action, await db.adminAiRequestLog.SingleAsync());
        }

        private static IConfiguration Configuration(IDictionary<string, string?>? values = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static AdminAssistantRequest ValidRequest() => new()
        {
            version = "1",
            page_key = "pages/admin/member/member_list",
            question = "查询四月租赁订单",
            context = new AdminAssistantContext()
        };

        private static RentalOrderQueryState QueryState(string cellSuffix) => new()
        {
            start_date = new DateTime(2026, 4, 1),
            end_date = new DateTime(2026, 4, 30),
            cell_suffix = cellSuffix
        };

        private static async Task AddTrustedStaffSession(ApplicationDBContext db)
        {
            Staff staff = new() { id = 7, valid = 1, title_level = 200 };
            Member member = new() { id = 8 };
            SocialAccountForJob account = new() { id = 9, member_id = member.id, wechat_mini_openid = "openid" };
            StaffSocialAccount staffAccount = new()
            {
                id = 10, staff_id = staff.id, social_account_id = account.id, valid = 1,
                start_date = DateTime.Today.AddDays(-1)
            };
            MiniSession session = new()
            {
                session_key = "trusted-session", session_type = "wechat_mini_openid", member_id = member.id,
                valid = 1, expire_date = DateTime.Now.AddHours(1)
            };
            await db.AddRangeAsync(staff, member, account, staffAccount, session);
            await db.SaveChangesAsync();
        }

        private sealed class RejectingAssistantService : IAdminAssistantService
        {
            public bool wasCalled { get; private set; }

            public Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff, string traceId,
                bool structuredEnabled, CancellationToken cancellationToken)
            {
                wasCalled = true;
                throw new InvalidOperationException("invalid requests must not reach the assistant service");
            }
        }

        private sealed class ThrowingAssistantService : IAdminAssistantService
        {
            private readonly string _plannerJson;
            private readonly string _error;
            public bool wasCalled { get; private set; }

            public ThrowingAssistantService(string plannerJson, string error = "planner_failed")
            {
                _plannerJson = plannerJson;
                _error = error;
            }

            public Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff, string traceId,
                bool structuredEnabled, CancellationToken cancellationToken)
            {
                wasCalled = true;
                AdminAssistantAuditData audit = new()
                {
                    planner_json = _plannerJson,
                    validation_result = "rejected",
                    error = _error
                };
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner,
                    new InvalidOperationException("database details"), audit);
            }
        }

        private sealed class SuccessfulAssistantService : IAdminAssistantService
        {
            private readonly string _plannerJson;
            private readonly string _cellSuffix;
            public bool wasCalled { get; private set; }

            public SuccessfulAssistantService(string plannerJson, string cellSuffix)
            {
                _plannerJson = plannerJson;
                _cellSuffix = cellSuffix;
            }

            public Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff, string traceId,
                bool structuredEnabled, CancellationToken cancellationToken)
            {
                wasCalled = true;
                RentalOrderQueryState state = QueryState(_cellSuffix);
                RentalOrderQuerySummary summary = new(new Dictionary<string, double> { ["order_count"] = 2 },
                    new List<RentalOrderQuerySummaryGroup>());
                return Task.FromResult(new AdminAssistantExecutionResult
                {
                    response = new AdminAssistantResponse
                    {
                        version = "1", trace_id = traceId, reply = new AssistantReply { text = "完成。" },
                        actions = new List<ClientAssistantAction> { new() { id = "a1", state = state, summary = summary } },
                        context = new AdminAssistantContext { rental_order_query = state }
                    },
                    audit = new AdminAssistantAuditData
                    {
                        planner_json = _plannerJson, validation_result = "accepted", query = state, summary = summary
                    }
                });
            }
        }

        private sealed class PermissionAssistantService : IAdminAssistantService
        {
            public Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff, string traceId,
                bool structuredEnabled, CancellationToken cancellationToken) =>
                throw new AdminAssistantPermissionException(new AdminAssistantAuditData
                {
                    validation_result = "rejected", error = "permission_denied"
                });
        }

        private sealed class EmptyHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }
    }
}

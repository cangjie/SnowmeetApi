using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
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
            using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            DbContextOptions<ApplicationDBContext> options = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).Options;
            await using ApplicationDBContext db = new(options);
            await db.Database.EnsureCreatedAsync();
            await AddTrustedStaffSession(db);
            ThrowingAssistantService assistant = new();
            AdminAiController controller = new(db, Configuration(new Dictionary<string, string?>
            {
                ["AdminAssistant:StructuredProtocolEnabled"] = "true"
            }), new EmptyHttpClientFactory(), new HttpContextAccessor(), assistant);

            ActionResult<ApiResult<AdminAssistantResponse>> action = await controller.AskAdminAssistantByStaff(ValidRequest(), "trusted-session");

            ObjectResult result = Assert.IsType<ObjectResult>(action.Result);
            Assert.Equal(502, result.StatusCode);
            ApiResult<AdminAssistantResponse> body = Assert.IsType<ApiResult<AdminAssistantResponse>>(result.Value);
            Assert.Equal(1, body.code);
            Assert.DoesNotContain("database details", body.message);
            Assert.NotNull(body.data);
            Assert.Empty(body.data!.actions);
            Assert.Null(body.data.context.rental_order_query);
            Assert.True(assistant.wasCalled);

            AdminAiRequestLog audit = await db.adminAiRequestLog.SingleAsync();
            Assert.Equal("admin_assistant", audit.operation);
            Assert.Equal(7, audit.staff_id);
            Assert.False(audit.success);
            Assert.Equal("planner_failed", audit.error_message);
            Assert.Equal(502, audit.response_status_code);
            Assert.Contains("trace-secret", audit.response_payload);
            Assert.Contains("validation_result", audit.response_payload);
            Assert.DoesNotContain("service_token", audit.response_payload);
            Assert.DoesNotContain("Cookie", audit.response_payload);
            Assert.DoesNotContain("openid", audit.response_payload);
            Assert.DoesNotContain("orders", audit.response_payload);
            Assert.DoesNotContain("database details", audit.response_payload);
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
            public bool wasCalled { get; private set; }

            public Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff, string traceId,
                bool structuredEnabled, CancellationToken cancellationToken)
            {
                wasCalled = true;
                AdminAssistantAuditData audit = new()
                {
                    planner_json = "{\"trace\":\"trace-secret\",\"service_token\":\"secret\",\"Cookie\":\"cookie\",\"openid\":\"openid\",\"orders\":[{\"id\":1}]}",
                    validation_result = "rejected",
                    error = "planner_failed"
                };
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner,
                    new InvalidOperationException("database details"), audit);
            }
        }

        private sealed class EmptyHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }
    }
}

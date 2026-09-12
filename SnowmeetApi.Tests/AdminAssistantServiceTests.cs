using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;
using SnowmeetApi.Services.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class AdminAssistantServiceTests
    {
        [Fact]
        public async Task 查询action执行后被转换且不会原样下发()
        {
            FakeReqaiClient reqai = new(plan: QueryPlan("rental_order.query"), final: Reply("共 12 单。"));
            FakeQueryExecutor query = new(Summary(orderCount: 12));

            AdminAssistantExecutionResult execution = await Service(reqai, query).AskAsync(Request(), Staff(level: 100), "trace", true, default);
            AdminAssistantResponse result = execution.response;

            Assert.Equal("共 12 单。", result.reply.text);
            Assert.Equal("rental_order.show_results", Assert.Single(result.actions).type);
            Assert.DoesNotContain(result.actions, x => x.type == "rental_order.query");
            Assert.Equal("trace", result.trace_id);
            Assert.Equal("completed", result.actions[0].status);
            Assert.Equal("accepted", execution.audit.validation_result);
            Assert.NotNull(execution.audit.planner_json);
            Assert.Equal("2026-04-01", execution.audit.query!.start_date?.ToString("yyyy-MM-dd"));
            Assert.Equal(12d, execution.audit.summary!.metrics["order_count"]);
        }

        [Fact]
        public async Task Finalize失败仍返回确定性汇总和成功action()
        {
            FakeReqaiClient reqai = new(plan: QueryPlan(), finalizeError: new HttpRequestException());

            AdminAssistantExecutionResult execution = await Service(reqai, new FakeQueryExecutor(Summary(12))).AskAsync(Request(), Staff(100), "trace", true, default);
            AdminAssistantResponse result = execution.response;

            Assert.Contains("12", result.reply.text);
            Assert.Equal("completed", Assert.Single(result.actions).status);
            Assert.Equal("accepted", execution.audit.validation_result);
            Assert.Equal("finalize_failed", execution.audit.error);
            Assert.NotNull(execution.audit.planner_json);
            Assert.Equal("2026-04-01", execution.audit.query!.start_date?.ToString("yyyy-MM-dd"));
            Assert.Equal(12d, execution.audit.summary!.metrics["order_count"]);
        }

        [Theory]
        [InlineData(99, true)]
        [InlineData(199, false)]
        public async Task 权限按action而不是统一门槛判断(int level, bool query)
        {
            AdminAssistantPermissionException error = await Assert.ThrowsAsync<AdminAssistantPermissionException>(() =>
                Service(query ? QueryPlanClient() : TextPlanClient(), FakeQuery()).AskAsync(Request(), Staff(level), "trace", true, default));

            Assert.NotNull(error.audit);
            Assert.Equal("permission_denied", error.audit!.error);
            Assert.NotNull(error.audit.planner_json);
        }

        [Fact]
        public async Task 非法规划响应保留原始响应和稳定审计()
        {
            const string plannerJson = "{not json";

            AdminAssistantOperationException error = await Assert.ThrowsAsync<AdminAssistantOperationException>(() =>
                Service(new FakeReqaiClient(plan: plannerJson), FakeQuery()).AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.Equal(AdminAssistantFailureStage.Planner, error.stage);
            Assert.Equal(plannerJson, error.audit.planner_json);
            Assert.Equal("rejected", error.audit.validation_result);
            Assert.Equal("planner_failed", error.audit.error);
            Assert.Null(error.audit.query);
            Assert.Null(error.audit.summary);
        }

        [Fact]
        public async Task 澄清错误保留合并后的条件审计()
        {
            FakeReqaiClient reqai = new(plan: PatchPlan("{\"rent_status\":\"未支付\"}"));

            AdminAssistantClarificationException error = await Assert.ThrowsAsync<AdminAssistantClarificationException>(() =>
                Service(reqai, FakeQuery()).AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.NotNull(error.audit);
            Assert.Equal("clarification_required", error.audit!.validation_result);
            Assert.Equal("clarification_required", error.audit.error);
            Assert.Equal("未支付", error.audit.query!.rent_status);
            Assert.Null(error.audit.summary);
        }

        [Fact]
        public async Task 执行失败保留完整条件和稳定错误审计()
        {
            FakeQueryExecutor query = new(Summary(0), new InvalidOperationException("database details"));

            AdminAssistantOperationException error = await Assert.ThrowsAsync<AdminAssistantOperationException>(() =>
                Service(QueryPlanClient(), query).AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.Equal(AdminAssistantFailureStage.Execution, error.stage);
            Assert.Equal("accepted", error.audit.validation_result);
            Assert.Equal("execution_failed", error.audit.error);
            Assert.Equal("2026-04-01", error.audit.query!.start_date?.ToString("yyyy-MM-dd"));
            Assert.Null(error.audit.summary);
        }

        [Fact]
        public async Task 合并后的非法条件保留状态和验证失败审计()
        {
            FakeReqaiClient reqai = new(plan: QueryPlanWithDates("2026-04-30", "2026-04-01"));

            AdminAssistantOperationException error = await Assert.ThrowsAsync<AdminAssistantOperationException>(() =>
                Service(reqai, FakeQuery()).AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.Equal(AdminAssistantFailureStage.Execution, error.stage);
            Assert.Equal("rejected", error.audit.validation_result);
            Assert.Equal("query_validation_failed", error.audit.error);
            Assert.Equal("2026-04-30", error.audit.query!.start_date?.ToString("yyyy-MM-dd"));
            Assert.Equal("2026-04-01", error.audit.query.end_date?.ToString("yyyy-MM-dd"));
            Assert.Null(error.audit.summary);
        }

        [Fact]
        public async Task 执行器前拒绝客户端上下文保留的非法条件()
        {
            AdminAssistantRequest request = Request();
            request.context.rental_order_query = new AdminAssistantQueryState
            {
                start_date = new DateTime(2026, 4, 1), end_date = new DateTime(2026, 4, 30), shop = " "
            };
            FakeQueryExecutor query = FakeQuery();

            AdminAssistantOperationException error = await Assert.ThrowsAsync<AdminAssistantOperationException>(() =>
                Service(new FakeReqaiClient(plan: PatchPlan("{}")), query).AskAsync(request, Staff(100), "trace", true, default));

            Assert.Equal(AdminAssistantFailureStage.Execution, error.stage);
            Assert.Equal("query_validation_failed", error.audit.error);
            Assert.False(query.wasCalled);
        }

        [Fact]
        public async Task Finalize只接收完整条件和严格汇总()
        {
            FakeReqaiClient reqai = new(plan: QueryPlan(), final: Reply("完成。"));
            QuerySummary summary = Summary(12);

            await Service(reqai, new FakeQueryExecutor(summary)).AskAsync(Request(), Staff(100), "trace", true, default);

            Assert.NotNull(reqai.finalizeRequest);
            Assert.Equal("查询今年四月租赁订单", reqai.finalizeRequest!.question);
            Assert.Equal("rental_order.query", reqai.finalizeRequest.executed.type);
            Assert.Equal("2026-04-01", reqai.finalizeRequest.executed.query["start_date"]);
            Assert.Equal(12d, reqai.finalizeRequest.executed.summary.metrics["order_count"]);
            Assert.Empty(reqai.finalizeRequest.executed.summary.groups);
        }

        [Fact]
        public async Task Finalize请求和失败审计会隐去格式化手机号及凭据赋值()
        {
            const string fullCell = "13800138000";
            const string spacedCell = "138 0013 8000";
            const string prefixedCell = "+86-138-0013-8000";
            CapturingReqaiHandler handler = new(PrivacyPlan(fullCell));
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reqai:BaseUrl"] = "https://reqai.test",
                ["Reqai:ServiceToken"] = "service-token"
            }).Build();
            ReqaiAdminAssistantClient reqai = new(new StaticHttpClientFactory(handler), configuration);
            AdminAssistantRequest request = Request();
            request.question = "查询订单 " + spacedCell + "、" + prefixedCell +
                "；authorization=Bearer TOP-SECRET-ABC; password=\"quoted password value\"; secret='private phrase'; " +
                "api-key=API KEY VALUE; token=TOKEN VALUE; Cookie=COOKIE VALUE; OpenID=OPENID VALUE; payment_id=PAYMENT VALUE";

            AdminAssistantExecutionResult result = await Service(reqai, FakeQuery()).AskAsync(request, Staff(100), "trace", true, default);
            string finalizeBody = handler.finalizeBody!;
            string errorAudit = JsonSerializer.Serialize(result.audit);
            string finalizeQuestion = JsonDocument.Parse(finalizeBody).RootElement.GetProperty("question").GetString()!;

            Assert.Equal("finalize_failed", result.audit.error);
            Assert.Contains("\"cell_suffix\":\"8000\"", finalizeBody);
            Assert.DoesNotContain(fullCell, finalizeBody);
            Assert.DoesNotContain(spacedCell, finalizeBody);
            Assert.DoesNotContain(prefixedCell, finalizeBody);
            Assert.DoesNotContain(fullCell, errorAudit);
            Assert.DoesNotContain("TOP-SECRET-ABC", finalizeBody);
            Assert.DoesNotContain("quoted password value", finalizeBody);
            Assert.DoesNotContain("private phrase", finalizeBody);
            Assert.DoesNotContain("API KEY VALUE", finalizeBody);
            Assert.DoesNotContain("TOKEN VALUE", finalizeBody);
            Assert.DoesNotContain("COOKIE VALUE", finalizeBody);
            Assert.DoesNotContain("OPENID VALUE", finalizeBody);
            Assert.DoesNotContain("PAYMENT VALUE", finalizeBody);
            Assert.DoesNotContain("TOP-SECRET-ABC", errorAudit);
            Assert.DoesNotContain("quoted password value", errorAudit);
            Assert.DoesNotContain("private phrase", errorAudit);
            Assert.DoesNotContain("API KEY VALUE", errorAudit);
            Assert.DoesNotContain("TOKEN VALUE", errorAudit);
            Assert.DoesNotContain("COOKIE VALUE", errorAudit);
            Assert.DoesNotContain("OPENID VALUE", errorAudit);
            Assert.DoesNotContain("PAYMENT VALUE", errorAudit);
            Assert.Contains("查询订单", finalizeQuestion);
        }

        [Fact]
        public async Task 纯文字回复保留现有上下文且不查询()
        {
            AdminAssistantRequest request = Request();
            request.context.rental_order_query = new AdminAssistantQueryState
            {
                start_date = new DateTime(2026, 4, 1), end_date = new DateTime(2026, 4, 30)
            };
            FakeQueryExecutor query = FakeQuery();

            AdminAssistantResponse result = (await Service(TextPlanClient(), query).AskAsync(request, Staff(200), "trace", true, default)).response;

            Assert.Equal("页面说明", result.reply.text);
            Assert.Empty(result.actions);
            Assert.Same(request.context.rental_order_query, result.context.rental_order_query);
            Assert.False(query.wasCalled);
        }

        private static AdminAssistantRequest Request() => new()
        {
            page_key = "pages/admin/member/member_list",
            question = "查询今年四月租赁订单",
            conversation = new List<AssistantConversationMessage>(),
            context = new AdminAssistantContext()
        };

        private static Staff Staff(int level) => new() { id = 7, title_level = level };

        private static QuerySummary Summary(int orderCount) => new()
        {
            metrics = new Dictionary<string, double>
            {
                ["order_count"] = orderCount,
                ["charge_total"] = 0,
                ["paid_total"] = 0,
                ["refund_total"] = 0,
                ["unpaid_count"] = 0
            },
            groups = new List<QuerySummaryGroup>()
        };

        private static AssistantReply Reply(string text) => new() { text = text };

        [Fact]
        public async Task 未开放的域明确拒答而不是执行查询()
        {
            // 发布顺序要求 API 先只开租赁：旧版小程序不认识 care_order.show_results。
            FakeReqaiClient reqai = new(plan: QueryPlan("care_order.query"));
            FakeQueryExecutor care = new(Summary(3), type: "care_order.query");
            AdminAssistantService service = new(reqai, new[] { (IAdminAssistantQueryExecutor)care }, configuration: null);

            AdminAssistantUnsupportedException error = await Assert.ThrowsAsync<AdminAssistantUnsupportedException>(() =>
                service.AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.False(care.wasCalled);
            Assert.Equal("domain_disabled", error.audit.error);
            Assert.Equal("养护订单列表", error.detail.suggested_page);
        }

        [Fact]
        public async Task 养护查询走养护执行器并下发养护的客户端指令()
        {
            FakeReqaiClient reqai = new(plan: QueryPlan("care_order.query"), final: Reply("共 3 单。"));
            FakeQueryExecutor care = new(Summary(3), type: "care_order.query");

            AdminAssistantExecutionResult execution =
                await Service(reqai, care).AskAsync(Request(), Staff(100), "trace", true, default);

            Assert.True(care.wasCalled);
            Assert.Equal("养护", care.calledDomain!.bizType);
            Assert.Equal("care_order.show_results", Assert.Single(execution.response.actions).type);
            Assert.NotNull(execution.response.context.care_order_query);
            Assert.Null(execution.response.context.rental_order_query);
            Assert.Equal("care_order.query", execution.response.context.active_query_type);
        }

        [Fact]
        public async Task 别的域的条件进不了本域查询()
        {
            // 这正是线上那个 bug 的形状：养护查询里夹带租赁状态，必须在执行器之前被拒。
            FakeReqaiClient reqai = new(plan:
                "{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"care_order.query\"," +
                "\"mode\":\"replace\",\"arguments\":{\"start_date\":\"2026-04-01\",\"end_date\":\"2026-04-30\"," +
                "\"rent_status\":\"未支付\"},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}");
            FakeQueryExecutor care = new(Summary(0), type: "care_order.query");

            await Assert.ThrowsAsync<AdminAssistantOperationException>(() =>
                Service(reqai, care).AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.False(care.wasCalled);
        }

        [Fact]
        public async Task 关键词夹带业务概念时明确拒答而不是去查一个错口径()
        {
            FakeReqaiClient reqai = new(plan:
                "{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"rental_order.query\"," +
                "\"mode\":\"replace\",\"arguments\":{\"start_date\":\"2026-04-01\",\"end_date\":\"2026-04-30\"," +
                "\"keyword\":\"养护\"},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}");
            FakeQueryExecutor query = FakeQuery();

            AdminAssistantUnsupportedException error = await Assert.ThrowsAsync<AdminAssistantUnsupportedException>(() =>
                Service(reqai, query).AskAsync(Request(), Staff(100), "trace", true, default));

            Assert.False(query.wasCalled);
            Assert.Equal("unsupported_filter", error.detail.reason);
            Assert.Equal("租赁订单列表", error.detail.suggested_page);
            Assert.Equal("keyword_rejected", error.audit.error);
        }

        [Fact]
        public async Task 规划器说做不了时保留文字且不执行查询()
        {
            FakeReqaiClient reqai = new(plan:
                "{\"version\":\"1\",\"reply\":{\"text\":\"暂时还不能查这个业务，请到对应页面自行筛选。\",\"citations\":[]}," +
                "\"actions\":[],\"unsupported\":{\"reason\":\"unknown_domain\",\"target_domain\":\"unknown\"," +
                "\"detail\":\"工资单不属于可查询业务\",\"suggested_page\":\"无\"}}");
            FakeQueryExecutor query = FakeQuery();

            AdminAssistantExecutionResult execution =
                await Service(reqai, query).AskAsync(Request(), Staff(200), "trace", true, default);

            Assert.False(query.wasCalled);
            Assert.Empty(execution.response.actions);
            Assert.Contains("暂时还不能查", execution.response.reply.text);
            Assert.Equal("unsupported", execution.audit.validation_result);
            Assert.Equal("unknown_domain", execution.audit.unsupported_reason);
        }

        [Fact]
        public async Task 跨域的增量修改会要求说清查哪个业务()
        {
            // 养护上下文里说「改成五月」，模型却给了租赁的 patch —— 合并出来的条件看着合法、口径是错的。
            FakeReqaiClient reqai = new(plan: PatchPlan("{\"end_date\":\"2026-05-31\"}"));
            AdminAssistantRequest request = Request();
            request.context.active_query_type = "care_order.query";
            request.context.care_order_query = new AdminAssistantQueryState
            {
                start_date = new DateTime(2026, 4, 1), end_date = new DateTime(2026, 4, 30)
            };
            FakeQueryExecutor query = FakeQuery();

            await Assert.ThrowsAsync<AdminAssistantClarificationException>(() =>
                Service(reqai, query).AskAsync(request, Staff(100), "trace", true, default));

            Assert.False(query.wasCalled);
        }

        private static string QueryPlan(string actionType = "rental_order.query") =>
            "{\"version\":\"1\",\"reply\":{\"text\":\"我来查询。\",\"citations\":[]},\"actions\":[{\"id\":\"a1\",\"type\":\"" + actionType +
            "\",\"mode\":\"replace\",\"arguments\":{\"start_date\":\"2026-04-01\",\"end_date\":\"2026-04-30\"},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}";

        private static string TextPlan(string text) =>
            "{\"version\":\"1\",\"reply\":{\"text\":\"" + text + "\",\"citations\":[]},\"actions\":[]}";

        private static string PatchPlan(string arguments) =>
            "{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"rental_order.query\",\"mode\":\"patch\",\"arguments\":" + arguments + ",\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}";

        private static string QueryPlanWithDates(string startDate, string endDate) =>
            "{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"rental_order.query\",\"mode\":\"replace\",\"arguments\":{\"start_date\":\"" + startDate + "\",\"end_date\":\"" + endDate + "\"},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}";

        private static FakeReqaiClient QueryPlanClient() => new(plan: QueryPlan("rental_order.query"));
        private static FakeReqaiClient TextPlanClient() => new(plan: TextPlan("页面说明"));
        private static FakeQueryExecutor FakeQuery() => new(Summary(0));
        private static AdminAssistantService Service(IReqaiAdminAssistantClient reqai, IAdminAssistantQueryExecutor query) =>
            new(reqai, new[] { query }, AllDomainsEnabled());

        /// <summary>线上按域灰度（默认只开租赁），但单元测试要覆盖全部四个域。</summary>
        private static IConfiguration AllDomainsEnabled() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminAssistant:EnabledDomains"] = "all"
            }).Build();

        private static string PrivacyPlan(string fullCell) => JsonSerializer.Serialize(new
        {
            version = "1",
            reply = new
            {
                text = "authorization=Bearer TOP-SECRET-ABC; password=\"quoted password value\"; secret='private phrase'; " +
                    "api-key=API KEY VALUE; token=TOKEN VALUE; Cookie=COOKIE VALUE; OpenID=OPENID VALUE; payment_id=PAYMENT VALUE",
                citations = Array.Empty<string>()
            },
            actions = new[]
            {
                new
                {
                    id = "a1", type = "rental_order.query", mode = "replace",
                    arguments = new
                    {
                        start_date = "2026-04-01", end_date = "2026-04-30", cell_suffix = fullCell,
                        // keyword 现在限长 40 且不许含空格，所以这里用一个短的凭据赋值：
                        // 形状合法、能进到 finalize，正好验证脱敏本身仍然生效。
                        keyword = "token=TOP-SECRET-ABC"
                    },
                    aggregation = new { metrics = new[] { "order_count" }, group_by = Array.Empty<string>() }
                }
            }
        });

        private sealed class FakeReqaiClient : IReqaiAdminAssistantClient
        {
            private readonly string _plan;
            private readonly AssistantReply _final;
            private readonly Exception? _finalizeError;
            public ReqaiFinalizeRequest? finalizeRequest { get; private set; }

            public FakeReqaiClient(string plan, AssistantReply? final = null, Exception? finalizeError = null)
            {
                _plan = plan;
                _final = final ?? Reply("完成。");
                _finalizeError = finalizeError;
            }

            public Task<string> PlanAsync(ReqaiPlanRequest request, CancellationToken cancellationToken) => Task.FromResult(_plan);

            public Task<AssistantReply> FinalizeAsync(ReqaiFinalizeRequest request, CancellationToken cancellationToken)
            {
                finalizeRequest = request;
                if (_finalizeError != null) throw _finalizeError;
                return Task.FromResult(_final);
            }

            public Task<LegacyRentIntent> LegacyRentIntentAsync(string question, CancellationToken cancellationToken) =>
                Task.FromResult(new LegacyRentIntent { status = "unsupported" });

            public Task<AssistantReply> LegacyPageHelpAsync(AdminAssistantRequest request, int staffId, string traceId, CancellationToken cancellationToken) =>
                Task.FromResult(Reply("页面说明"));
        }

        private sealed class FakeQueryExecutor : IAdminAssistantQueryExecutor
        {
            private readonly QuerySummary _summary;
            private readonly Exception? _error;
            public bool wasCalled { get; private set; }
            public AdminAssistantDomain? calledDomain { get; private set; }

            public FakeQueryExecutor(QuerySummary summary, Exception? error = null, string type = "rental_order.query")
            {
                _summary = summary;
                _error = error;
                actionType = type;
            }

            public string actionType { get; }

            public Task<AdminAssistantQueryExecution> ExecuteAsync(AdminAssistantQueryState state,
                AdminAssistantDomain domain, IReadOnlyCollection<string> metrics, IReadOnlyList<string> groupBy,
                CancellationToken cancellationToken)
            {
                wasCalled = true;
                calledDomain = domain;
                if (_error != null) throw _error;
                return Task.FromResult(new AdminAssistantQueryExecution(state, _summary));
            }
        }

        private sealed class StaticHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;

            public StaticHttpClientFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);

            public HttpClient CreateClient(string name) => _client;
        }

        private sealed class CapturingReqaiHandler : HttpMessageHandler
        {
            private readonly string _plan;
            public string? finalizeBody { get; private set; }

            public CapturingReqaiHandler(string plan) => _plan = plan;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/plan", StringComparison.Ordinal))
                    return Task.FromResult(JsonResponse(HttpStatusCode.OK, _plan));

                finalizeBody = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult(JsonResponse(HttpStatusCode.BadGateway, "{\"detail\":\"unavailable\"}"));
            }

            private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}

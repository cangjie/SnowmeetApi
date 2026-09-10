using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        public async Task Finalize只接收完整条件和严格汇总()
        {
            FakeReqaiClient reqai = new(plan: QueryPlan(), final: Reply("完成。"));
            RentalOrderQuerySummary summary = Summary(12);

            await Service(reqai, new FakeQueryExecutor(summary)).AskAsync(Request(), Staff(100), "trace", true, default);

            Assert.NotNull(reqai.finalizeRequest);
            Assert.Equal("查询今年四月租赁订单", reqai.finalizeRequest!.question);
            Assert.Equal("2026-04-01", reqai.finalizeRequest.query.start_date?.ToString("yyyy-MM-dd"));
            Assert.Equal(12d, reqai.finalizeRequest.summary.metrics["order_count"]);
            Assert.Empty(reqai.finalizeRequest.summary.groups);
        }

        [Fact]
        public async Task 纯文字回复保留现有上下文且不查询()
        {
            AdminAssistantRequest request = Request();
            request.context.rental_order_query = new RentalOrderQueryState
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

        private static RentalOrderQuerySummary Summary(int orderCount) => new(
            new Dictionary<string, double>
            {
                ["order_count"] = orderCount,
                ["charge_total"] = 0,
                ["paid_total"] = 0,
                ["refund_total"] = 0,
                ["unpaid_count"] = 0
            },
            new List<RentalOrderQuerySummaryGroup>());

        private static AssistantReply Reply(string text) => new() { text = text };

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
        private static AdminAssistantService Service(FakeReqaiClient reqai, FakeQueryExecutor query) => new(reqai, query);

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

        private sealed class FakeQueryExecutor : IRentalOrderQueryExecutor
        {
            private readonly RentalOrderQuerySummary _summary;
            private readonly Exception? _error;
            public bool wasCalled { get; private set; }

            public FakeQueryExecutor(RentalOrderQuerySummary summary, Exception? error = null)
            {
                _summary = summary;
                _error = error;
            }

            public Task<RentalOrderQueryExecution> ExecuteAsync(RentalOrderQueryState state,
                IReadOnlyCollection<string> metrics, IReadOnlyList<string> groupBy, CancellationToken cancellationToken)
            {
                wasCalled = true;
                if (_error != null) throw _error;
                return Task.FromResult(new RentalOrderQueryExecution(state, _summary));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;
using SnowmeetApi.Services.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class AdminAssistantLegacyAdapterTests
    {
        [Fact]
        public async Task 开关关闭时查询先走旧意图并转换为v1响应()
        {
            FakeReqaiClient reqai = new(ReadyIntent("2026-04-01", "2026-04-30"));

            AdminAssistantExecutionResult execution = await Service(reqai).AskAsync(Request(), Staff(100), "trace", false, default);

            Assert.Equal("1", execution.response.version);
            Assert.Equal("rental_order.show_results", Assert.Single(execution.response.actions).type);
            Assert.Equal(1, reqai.legacyIntentCalls);
            Assert.Equal(0, reqai.planCalls);
            Assert.Equal("accepted", execution.audit.validation_result);
            Assert.Equal(3d, execution.audit.summary!.metrics["order_count"]);
        }

        [Fact]
        public async Task 开关关闭时unsupported意图回退旧页面帮助()
        {
            FakeReqaiClient reqai = new(new LegacyRentIntent { status = "unsupported" }, "这里是页面说明。");

            AdminAssistantExecutionResult execution = await Service(reqai).AskAsync(Request("这个页面怎么用"), Staff(200), "trace", false, default);

            Assert.Equal("这里是页面说明。", execution.response.reply.text);
            Assert.Empty(execution.response.actions);
            Assert.Equal(1, reqai.legacyIntentCalls);
            Assert.Equal(1, reqai.legacyHelpCalls);
            Assert.Equal(0, reqai.planCalls);
            Assert.Equal("accepted", execution.audit.validation_result);
        }

        private static AdminAssistantService Service(FakeReqaiClient reqai) => new(reqai, new IAdminAssistantQueryExecutor[] { new FakeQueryExecutor() });

        private static AdminAssistantRequest Request(string question = "查询四月租赁订单") => new()
        {
            page_key = "pages/admin/member/member_list",
            question = question,
            context = new AdminAssistantContext()
        };

        private static Staff Staff(int level) => new() { id = 7, title_level = level };

        [Fact]
        public async Task 旧意图把业务域塞进关键词时给出可用的拒答而不是笼统的暂不可用()
        {
            // 线上复现：开关仍关着时，店员在养护页问养护订单，旧意图接口只会把「养护」
            // 塞进 keyword（正是本次要根治的 bug）。新的关键词护栏拦住了它，但拦住之后
            // 不能只回一句「订单查询暂不可用」——那既没解释原因，也没告诉店员去哪儿查。
            FakeReqaiClient reqai = new(new LegacyRentIntent
            {
                status = "ready",
                start_date = DateTime.Parse("2026-04-01"),
                end_date = DateTime.Parse("2026-04-10"),
                keyword = "养护"
            });

            AdminAssistantUnsupportedException error = await Assert.ThrowsAsync<AdminAssistantUnsupportedException>(() =>
                Service(reqai).AskAsync(Request("查询下今年4月上旬的养护订单"), Staff(100), "trace", false, default));

            Assert.Equal("unsupported_filter", error.detail.reason);
            Assert.Equal("租赁订单列表", error.detail.suggested_page);
            Assert.Equal("keyword_rejected", error.audit.error);
        }

        private static LegacyRentIntent ReadyIntent(string startDate, string endDate) => new()
        {
            status = "ready",
            start_date = DateTime.Parse(startDate),
            end_date = DateTime.Parse(endDate)
        };

        private sealed class FakeReqaiClient : IReqaiAdminAssistantClient
        {
            private readonly LegacyRentIntent _intent;
            private readonly string _help;
            public int planCalls { get; private set; }
            public int legacyIntentCalls { get; private set; }
            public int legacyHelpCalls { get; private set; }

            public FakeReqaiClient(LegacyRentIntent intent, string help = "")
            {
                _intent = intent;
                _help = help;
            }

            public Task<string> PlanAsync(ReqaiPlanRequest request, CancellationToken cancellationToken)
            {
                planCalls++;
                throw new InvalidOperationException("structured planner must not be called when disabled");
            }

            public Task<AssistantReply> FinalizeAsync(ReqaiFinalizeRequest request, CancellationToken cancellationToken) =>
                Task.FromResult(new AssistantReply { text = "unused" });

            public Task<LegacyRentIntent> LegacyRentIntentAsync(string question, CancellationToken cancellationToken)
            {
                legacyIntentCalls++;
                return Task.FromResult(_intent);
            }

            public Task<AssistantReply> LegacyPageHelpAsync(AdminAssistantRequest request, int staffId, string traceId,
                CancellationToken cancellationToken)
            {
                legacyHelpCalls++;
                return Task.FromResult(new AssistantReply { text = _help });
            }
        }

        private sealed class FakeQueryExecutor : IAdminAssistantQueryExecutor
        {
            public string actionType => "rental_order.query";

            public Task<AdminAssistantQueryExecution> ExecuteAsync(AdminAssistantQueryState state,
                AdminAssistantDomain domain, IReadOnlyCollection<string> metrics, IReadOnlyList<string> groupBy,
                CancellationToken cancellationToken) =>
                Task.FromResult(new AdminAssistantQueryExecution(state, new QuerySummary
                {
                    metrics = new Dictionary<string, double> { ["order_count"] = 3d },
                    groups = new List<QuerySummaryGroup>()
                }));
        }
    }
}

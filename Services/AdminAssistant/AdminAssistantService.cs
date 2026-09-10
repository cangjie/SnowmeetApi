using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public sealed class AdminAssistantService : IAdminAssistantService
    {
        private readonly IReqaiAdminAssistantClient _reqai;
        private readonly IRentalOrderQueryExecutor _query;

        public AdminAssistantService(IReqaiAdminAssistantClient reqai, IRentalOrderQueryExecutor query)
        {
            _reqai = reqai;
            _query = query;
        }

        public async Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff,
            string traceId, bool structuredEnabled, CancellationToken cancellationToken)
        {
            if (!structuredEnabled)
                throw new InvalidOperationException("结构化管理员助手未启用");

            string plannerJson;
            ReqaiPlanResponse plan;
            try
            {
                plannerJson = await _reqai.PlanAsync(BuildPlanRequest(request, staff, traceId), cancellationToken);
                plan = AdminAssistantProtocolRules.ParsePlan(ProtocolPayload(plannerJson));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner, error);
            }

            if (plan.actions.Count == 0)
            {
                RequireLevel(staff, 200);
                return new AdminAssistantExecutionResult
                {
                    response = TextOnly(traceId, plan.reply!, request.context),
                    audit = new AdminAssistantAuditData
                    {
                        planner_json = plannerJson,
                        validation_result = "accepted",
                        query = null,
                        summary = null,
                        error = null
                    }
                };
            }

            RequireLevel(staff, 100);
            ReqaiPlanAction action = plan.actions.Single();
            RentalOrderQueryState state;
            try
            {
                state = AdminAssistantProtocolRules.Merge(action.mode,
                    request.context?.rental_order_query ?? new RentalOrderQueryState(), action.arguments);
                AdminAssistantProtocolRules.ValidateQuery(state);
            }
            catch (AdminAssistantClarificationException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error);
            }

            RentalOrderQueryExecution execution;
            try
            {
                execution = await _query.ExecuteAsync(state, action.aggregation.metrics, action.aggregation.group_by, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error);
            }

            AssistantReply finalReply;
            bool finalizationFailed = false;
            try
            {
                finalReply = await _reqai.FinalizeAsync(BuildFinalizeRequest(request, traceId, plan.reply, execution, action.aggregation), cancellationToken);
                if (string.IsNullOrWhiteSpace(finalReply.text)) throw new ReqaiException();
            }
            catch (Exception error) when (error is ReqaiException or HttpRequestException or TaskCanceledException)
            {
                finalizationFailed = true;
                finalReply = DeterministicReply(execution.summary);
            }

            return new AdminAssistantExecutionResult
            {
                response = CompletedQuery(traceId, action.id, finalReply, execution),
                audit = new AdminAssistantAuditData
                {
                    planner_json = plannerJson,
                    validation_result = "accepted",
                    query = state,
                    summary = execution.summary,
                    error = finalizationFailed ? "finalize_failed" : null
                }
            };
        }

        private static ReqaiPlanRequest BuildPlanRequest(AdminAssistantRequest request, Staff staff, string traceId) => new()
        {
            version = "1",
            page_key = request.page_key,
            question = request.question,
            conversation = request.conversation.TakeLast(20).ToList(),
            context = request.context ?? new AdminAssistantContext(),
            staff_id = staff.id,
            trace_id = traceId,
            current_date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)),
            timezone = "Asia/Shanghai"
        };

        private static ReqaiFinalizeRequest BuildFinalizeRequest(AdminAssistantRequest request, string traceId,
            AssistantReply? plannerReply, RentalOrderQueryExecution execution, AggregationRequest aggregation) => new()
        {
            version = "1",
            trace_id = traceId,
            question = request.question,
            planner_reply = plannerReply,
            query = execution.state,
            aggregation = aggregation,
            summary = ToReqaiSummary(execution.summary)
        };

        private static ReqaiQuerySummary ToReqaiSummary(RentalOrderQuerySummary summary) => new()
        {
            metrics = new Dictionary<string, double>(summary.metrics),
            groups = summary.groups.Select(group => new ReqaiQuerySummaryGroup
            {
                rent_status = ValueOrNull(group.keys, "rent_status"),
                shop = ValueOrNull(group.keys, "shop"),
                biz_date = DateOnly.TryParse(ValueOrNull(group.keys, "biz_date"), out DateOnly date) ? date : null,
                metrics = new Dictionary<string, double>(group.metrics)
            }).ToList()
        };

        private static string? ValueOrNull(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value) ? value : null;

        private static AdminAssistantResponse TextOnly(string traceId, AssistantReply reply, AdminAssistantContext? context) => new()
        {
            version = "1",
            trace_id = traceId,
            reply = reply,
            actions = new List<ClientAssistantAction>(),
            context = context ?? new AdminAssistantContext()
        };

        private static AdminAssistantResponse CompletedQuery(string traceId, string actionId, AssistantReply reply,
            RentalOrderQueryExecution execution) => new()
        {
            version = "1",
            trace_id = traceId,
            reply = reply,
            actions = new List<ClientAssistantAction>
            {
                new ClientAssistantAction
                {
                    id = actionId,
                    state = execution.state,
                    summary = execution.summary
                }
            },
            context = new AdminAssistantContext { rental_order_query = execution.state }
        };

        private static AssistantReply DeterministicReply(RentalOrderQuerySummary summary)
        {
            double count = summary.metrics.TryGetValue("order_count", out double orderCount) ? orderCount : 0;
            double unpaid = summary.metrics.TryGetValue("unpaid_count", out double unpaidCount) ? unpaidCount : 0;
            return new AssistantReply
            {
                text = unpaid > 0
                    ? $"查询完成，共 {count:0} 单，其中未支付 {unpaid:0} 单。"
                    : $"查询完成，共 {count:0} 单。"
            };
        }

        private static void RequireLevel(Staff staff, int requiredLevel)
        {
            if (staff.title_level < requiredLevel) throw new AdminAssistantPermissionException();
        }

        private static string ProtocolPayload(string plannerJson)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(plannerJson);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("规划响应必须是对象");

                HashSet<string> allowed = new(StringComparer.Ordinal) { "version", "reply", "actions", "model", "effort" };
                HashSet<string> seen = new(StringComparer.Ordinal);
                using MemoryStream stream = new();
                using Utf8JsonWriter writer = new(stream);
                writer.WriteStartObject();
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                        throw new InvalidOperationException("规划响应包含不支持的字段");
                    if (property.Name is "model" or "effort") continue;
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.Flush();
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException("规划响应格式无效", error);
            }
        }
    }
}

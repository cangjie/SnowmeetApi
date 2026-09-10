using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public sealed class AdminAssistantService : IAdminAssistantService
    {
        private static readonly string[] DefaultMetrics = { "order_count", "charge_total", "paid_total", "refund_total", "unpaid_count" };
        private static readonly Regex PhoneLike = new(@"(?<!\d)(?:\+?86[-\s]?)?1\d{10}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex SensitiveValue = new("\\b(?:service[_-]?token|access[_-]?token|refresh[_-]?token|token|cookie|openid|payment(?:[_-]?(?:id|no|token))?|transaction(?:[_-]?id)?)\\b\\s*(?:[:=]\\s*|\"\\s*:\\s*\")(?:(?:\"[^\"]*\")|[^\\s,;，；}]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SensitiveMarker = new(@"\b(?:service[_-]?token|access[_-]?token|refresh[_-]?token|token|cookie|openid|payment(?:[_-]?(?:id|no|token))?|transaction(?:[_-]?id)?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
                return await AskLegacyAsync(request, staff, traceId, cancellationToken);

            string? plannerJson = null;
            ReqaiPlanResponse plan;
            try
            {
                plannerJson = await _reqai.PlanAsync(BuildPlanRequest(request, staff, traceId), cancellationToken);
                plan = AdminAssistantProtocolRules.ParsePlan(ProtocolPayload(plannerJson));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner, error,
                    Audit(plannerJson, "rejected", null, null, "planner_failed"));
            }

            if (plan.actions.Count == 0)
            {
                RequireLevel(staff, 200, Audit(plannerJson, "rejected", null, null, "permission_denied"));
                return new AdminAssistantExecutionResult
                {
                    response = TextOnly(traceId, plan.reply!, request.context),
                    audit = Audit(plannerJson, "accepted", null, null, null)
                };
            }

            RequireLevel(staff, 100, Audit(plannerJson, "rejected", null, null, "permission_denied"));
            ReqaiPlanAction action = plan.actions.Single();
            RentalOrderQueryState? state = null;
            try
            {
                state = AdminAssistantProtocolRules.Merge(action.mode,
                    request.context?.rental_order_query ?? new RentalOrderQueryState(), action.arguments);
                AdminAssistantProtocolRules.ValidateQuery(state);
            }
            catch (AdminAssistantClarificationException error)
            {
                throw new AdminAssistantClarificationException(error.Message,
                    Audit(plannerJson, "clarification_required", state, null, "clarification_required"));
            }
            catch (Exception error)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error,
                    Audit(plannerJson, "rejected", state, null, "query_validation_failed"));
            }

            RentalOrderQueryExecution execution;
            try
            {
                execution = await _query.ExecuteAsync(state!, action.aggregation.metrics, action.aggregation.group_by, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error,
                    Audit(plannerJson, "accepted", state, null, "execution_failed"));
            }

            AssistantReply finalReply;
            bool finalizationFailed = false;
            try
            {
                finalReply = await _reqai.FinalizeAsync(BuildFinalizeRequest(request, traceId, plan.reply, execution, action.aggregation), cancellationToken);
                if (string.IsNullOrWhiteSpace(finalReply.text)) throw new ReqaiException();
            }
            catch (Exception)
            {
                finalizationFailed = true;
                finalReply = DeterministicReply(execution.summary);
            }

            return new AdminAssistantExecutionResult
            {
                response = CompletedQuery(traceId, action.id, finalReply, execution),
                    audit = Audit(plannerJson, "accepted", state, execution.summary,
                        finalizationFailed ? "finalize_failed" : null)
            };
        }

        private async Task<AdminAssistantExecutionResult> AskLegacyAsync(AdminAssistantRequest request, Staff staff,
            string traceId, CancellationToken cancellationToken)
        {
            LegacyRentIntent intent;
            string? legacyIntentJson = null;
            try
            {
                intent = await _reqai.LegacyRentIntentAsync(request.question, cancellationToken);
                legacyIntentJson = JsonSerializer.Serialize(intent);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner, error,
                    Audit(legacyIntentJson, "rejected", null, null, "legacy_intent_failed"));
            }

            if (intent.status == "ready")
            {
                RequireLevel(staff, 100, Audit(legacyIntentJson, "rejected", null, null, "permission_denied"));
                RentalOrderQueryState state = LegacyIntentToState(intent);
                try
                {
                    AdminAssistantProtocolRules.ValidateQuery(state);
                }
                catch (AdminAssistantClarificationException error)
                {
                    throw new AdminAssistantClarificationException(error.Message,
                        Audit(legacyIntentJson, "clarification_required", state, null, "clarification_required"));
                }
                catch (Exception error)
                {
                    throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error,
                        Audit(legacyIntentJson, "rejected", state, null, "query_validation_failed"));
                }

                try
                {
                    RentalOrderQueryExecution execution = await _query.ExecuteAsync(state, DefaultMetrics, Array.Empty<string>(), cancellationToken);
                    return new AdminAssistantExecutionResult
                    {
                        response = CompletedQuery(traceId, "legacy_query", DeterministicReply(execution.summary), execution),
                        audit = Audit(legacyIntentJson, "accepted", state, execution.summary, null)
                    };
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error,
                        Audit(legacyIntentJson, "accepted", state, null, "execution_failed"));
                }
            }

            if (intent.status == "clarification_required")
            {
                return new AdminAssistantExecutionResult
                {
                    response = TextOnly(traceId, new AssistantReply { text = intent.clarification ?? "请补充查询日期范围。" }, request.context),
                    audit = Audit(legacyIntentJson, "clarification_required", null, null, "clarification_required")
                };
            }

            if (intent.status != "unsupported")
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner,
                    new InvalidOperationException("旧查询意图无效"), Audit(legacyIntentJson, "rejected", null, null, "legacy_intent_invalid"));
            }

            RequireLevel(staff, 200, Audit(legacyIntentJson, "rejected", null, null, "permission_denied"));
            try
            {
                AssistantReply help = await _reqai.LegacyPageHelpAsync(request, staff.id, traceId, cancellationToken);
                return new AdminAssistantExecutionResult
                {
                    response = TextOnly(traceId, help, request.context),
                    audit = Audit(legacyIntentJson, "accepted", null, null, null)
                };
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Planner, error,
                    Audit(legacyIntentJson, "accepted", null, null, "legacy_help_failed"));
            }
        }

        private static RentalOrderQueryState LegacyIntentToState(LegacyRentIntent intent) => new()
        {
            start_date = intent.start_date,
            end_date = intent.end_date,
            shop = intent.shop,
            rent_status = intent.rent_status,
            is_test = intent.is_test,
            is_entertain = intent.is_entertain,
            have_discount = intent.have_discount,
            use_card = intent.use_card,
            cell_suffix = intent.cell_suffix,
            keyword = intent.keyword
        };

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
            question = RedactFreeText(request.question) ?? string.Empty,
            planner_reply = PrivacySafeReply(plannerReply),
            query = PrivacySafeQuery(execution.state),
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

        private static void RequireLevel(Staff staff, int requiredLevel, AdminAssistantAuditData audit)
        {
            if (staff.title_level < requiredLevel) throw new AdminAssistantPermissionException(audit);
        }

        private static AdminAssistantAuditData Audit(string? plannerJson, string validationResult,
            RentalOrderQueryState? state, RentalOrderQuerySummary? summary, string? error) => new()
        {
            planner_json = RedactFreeText(plannerJson),
            validation_result = validationResult,
            query = state == null ? null : PrivacySafeQuery(state),
            summary = summary,
            error = error
        };

        private static AssistantReply? PrivacySafeReply(AssistantReply? reply) => reply == null ? null : new AssistantReply
        {
            text = RedactFreeText(reply.text) ?? "[已隐去敏感信息]",
            citations = new List<JsonElement>()
        };

        private static RentalOrderQueryState PrivacySafeQuery(RentalOrderQueryState state) => new()
        {
            start_date = state.start_date,
            end_date = state.end_date,
            shop = state.shop,
            rent_status = state.rent_status,
            is_test = state.is_test,
            is_entertain = state.is_entertain,
            have_discount = state.have_discount,
            use_card = state.use_card,
            has_retail = state.has_retail,
            cell_suffix = LastFour(state.cell_suffix),
            keyword = RedactFreeText(state.keyword)
        };

        private static string? LastFour(string? value) => value == null ? null : value.Length <= 4 ? value : value[^4..];

        private static string? RedactFreeText(string? value)
        {
            if (value == null) return null;
            string withoutPhones = PhoneLike.Replace(value, "[已隐去手机号]");
            string withoutSensitiveValues = SensitiveValue.Replace(withoutPhones, "[已隐去敏感信息]");
            return SensitiveMarker.Replace(withoutSensitiveValues, "[已隐去敏感信息]");
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

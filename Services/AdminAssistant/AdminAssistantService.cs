using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public sealed class AdminAssistantService : IAdminAssistantService
    {
        private static readonly string[] DefaultMetrics = { "order_count", "charge_total", "paid_total", "refund_total", "unpaid_count" };
        private static readonly Regex PhoneLike = new(@"(?<!\d)(?:\+?86[\s-]*)?1[3-9]\d(?:[\s-]*\d{4}){2}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex SensitiveCredential = new("(?<![\\p{L}\\p{N}_-])(?:authorization|password|secret|api[_-]?key|service[_-]?token|access[_-]?token|refresh[_-]?token|token|cookie|openid|payment(?:[_-]?(?:id|no|token))?|transaction(?:[_-]?id)?)\\b\\s*(?:=\\s*|:\\s*|\"\\s*:\\s*\")(?:\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[^\\r\\n,;，；}\\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly IReqaiAdminAssistantClient _reqai;
        private readonly IReadOnlyDictionary<string, IAdminAssistantQueryExecutor> _executors;
        private readonly IReadOnlySet<string> _enabledDomains;

        public AdminAssistantService(IReqaiAdminAssistantClient reqai,
            IEnumerable<IAdminAssistantQueryExecutor> executors,
            IConfiguration? configuration = null)
        {
            _reqai = reqai;
            _executors = executors.ToDictionary(item => item.actionType, StringComparer.Ordinal);
            _enabledDomains = ReadEnabledDomains(configuration);
        }

        /// <summary>
        /// 按域灰度。默认只开租赁：小程序要先发布才认识别的域的 show_results，
        /// 提前放开会让旧版小程序收到不认识的 action 直接报「当前版本暂不支持」。
        /// appsettings.json 不随代码发布，所以默认值必须写在代码里。
        /// </summary>
        private static IReadOnlySet<string> ReadEnabledDomains(IConfiguration? configuration)
        {
            string? configured = configuration?["AdminAssistant:EnabledDomains"];
            if (string.IsNullOrWhiteSpace(configured))
                return new HashSet<string>(StringComparer.Ordinal) { "rental" };
            if (configured.Trim() == "all")
                return AdminAssistantDomains.All.Values.Select(item => item.domain).ToHashSet(StringComparer.Ordinal);
            return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
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

            // 明确的拒答：保留 reqai 的文字（里面已经带了「去哪个页面自己筛」），
            // 不执行任何查询。有这条路，做不了的请求才不会被降级成做得了的请求。
            if (plan.unsupported != null)
            {
                RequireLevel(staff, 200, Audit(plannerJson, "rejected", null, null, null, "permission_denied"));
                return new AdminAssistantExecutionResult
                {
                    response = TextOnly(traceId, plan.reply!, request.context),
                    audit = new AdminAssistantAuditData
                    {
                        planner_json = RedactFreeText(plannerJson),
                        validation_result = "unsupported",
                        unsupported_reason = plan.unsupported.reason,
                        target_domain = plan.unsupported.target_domain
                    }
                };
            }

            if (plan.actions.Count == 0)
            {
                RequireLevel(staff, 200, Audit(plannerJson, "rejected", null, null, null, "permission_denied"));
                return new AdminAssistantExecutionResult
                {
                    response = TextOnly(traceId, plan.reply!, request.context),
                    audit = Audit(plannerJson, "accepted", null, null, null, null)
                };
            }

            RequireLevel(staff, 100, Audit(plannerJson, "rejected", null, null, null, "permission_denied"));
            ReqaiPlanAction action = plan.actions.Single();
            AdminAssistantDomain domain = AdminAssistantDomains.Require(action.type);
            if (!_enabledDomains.Contains(domain.domain))
            {
                throw new AdminAssistantUnsupportedException(
                    "暂时还不能用自然语言查询" + domain.label + "，请到" + domain.pageHint + "页自行筛选。",
                    new AdminAssistantUnsupported
                    {
                        reason = "unsupported_query_type",
                        target_domain = domain.domain,
                        detail = domain.label + "查询尚未开放",
                        suggested_page = domain.pageHint
                    },
                    Audit(plannerJson, "unsupported", action.type, null, null, "domain_disabled"));
            }

            AdminAssistantQueryState? state = null;
            try
            {
                RequireSameDomainForPatch(action, request.context);
                state = AdminAssistantProtocolRules.Merge(action.mode,
                    request.context?.Get(domain.contextKey) ?? new AdminAssistantQueryState(), action.arguments);
                AdminAssistantProtocolRules.ValidateQuery(state, domain);
            }
            catch (AdminAssistantClarificationException error)
            {
                throw new AdminAssistantClarificationException(error.Message,
                    Audit(plannerJson, "clarification_required", action.type, state, null, "clarification_required"));
            }
            catch (AdminAssistantKeywordException error)
            {
                // 模型把业务概念塞进了 keyword。这是越界，不是故障：明确拒答，别去查一个错口径。
                throw new AdminAssistantUnsupportedException(
                    "这个条件我暂时没法表达，所以没有执行查询。请到" + domain.pageHint + "页自行筛选。",
                    new AdminAssistantUnsupported
                    {
                        reason = "unsupported_filter",
                        target_domain = domain.domain,
                        detail = error.Message,
                        suggested_page = domain.pageHint
                    },
                    Audit(plannerJson, "unsupported", action.type, state, null, "keyword_rejected"));
            }
            catch (Exception error)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error,
                    Audit(plannerJson, "rejected", action.type, state, null, "query_validation_failed"), domain.label);
            }

            if (!_executors.TryGetValue(action.type, out IAdminAssistantQueryExecutor? executor))
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution,
                    new InvalidOperationException("缺少 " + action.type + " 的执行器"),
                    Audit(plannerJson, "rejected", action.type, state, null, "executor_missing"), domain.label);
            }

            AdminAssistantQueryExecution execution;
            try
            {
                execution = await executor.ExecuteAsync(state!, domain, action.aggregation.metrics,
                    action.aggregation.group_by, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new AdminAssistantOperationException(AdminAssistantFailureStage.Execution, error,
                    Audit(plannerJson, "accepted", action.type, state, null, "execution_failed"), domain.label);
            }

            AssistantReply finalReply;
            bool finalizationFailed = false;
            try
            {
                finalReply = await _reqai.FinalizeAsync(
                    BuildFinalizeRequest(request, traceId, plan.reply, execution, action.aggregation, domain), cancellationToken);
                if (string.IsNullOrWhiteSpace(finalReply.text)) throw new ReqaiException();
            }
            catch (Exception)
            {
                finalizationFailed = true;
                finalReply = DeterministicReply(execution.summary, domain);
            }

            return new AdminAssistantExecutionResult
            {
                response = CompletedQuery(traceId, action.id, finalReply, execution, domain, request.context),
                audit = Audit(plannerJson, "accepted", action.type, state, execution.summary,
                    finalizationFailed ? "finalize_failed" : null)
            };
        }

        /// <summary>
        /// 多域之后新出现的错误类别：养护上下文里说「改成五月」必须仍然是养护。
        /// 跨域的 patch 会拿另一个域的条件去合并，合出来的东西看着合法、口径却是错的。
        /// </summary>
        private static void RequireSameDomainForPatch(ReqaiPlanAction action, AdminAssistantContext? context)
        {
            if (action.mode != "patch") return;
            string? active = context?.active_query_type;
            if (active != null && active != action.type)
                throw new AdminAssistantClarificationException("请说明这次要查哪个业务的订单。");
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
                AdminAssistantDomain legacyDomain = AdminAssistantDomains.Require("rental_order.query");
                AdminAssistantQueryState state = LegacyIntentToState(intent);
                try
                {
                    AdminAssistantProtocolRules.ValidateQuery(state, legacyDomain);
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
                    IAdminAssistantQueryExecutor legacyExecutor = _executors[legacyDomain.actionType];
                    AdminAssistantQueryExecution execution = await legacyExecutor.ExecuteAsync(
                        state, legacyDomain, DefaultMetrics, Array.Empty<string>(), cancellationToken);
                    return new AdminAssistantExecutionResult
                    {
                        response = CompletedQuery(traceId, "legacy_query",
                            DeterministicReply(execution.summary, legacyDomain), execution, legacyDomain, request.context),
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

        private static AdminAssistantQueryState LegacyIntentToState(LegacyRentIntent intent) => new()
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

        private static ReqaiPlanRequest BuildPlanRequest(AdminAssistantRequest request, Staff staff, string traceId)
        {
            AdminAssistantContext context = request.context ?? new AdminAssistantContext();
            Dictionary<string, object?> payload = new(StringComparer.Ordinal);
            AdminAssistantDomain? active = AdminAssistantDomains.Find(context.active_query_type);
            if (active != null && context.Get(active.contextKey) != null)
            {
                payload["active_query"] = new Dictionary<string, object?>
                {
                    ["type"] = active.actionType,
                    ["arguments"] = DomainShapedQuery(PrivacySafeQuery(context.Get(active.contextKey)!), active)
                };
            }
            // 旧版 reqai 只认 rental_order_query。发版顺序是 小程序 → API → reqai，
            // 中间那段时间线上跑的还是旧 reqai，所以租赁的上下文按老键再带一份。
            if (context.rental_order_query != null)
            {
                payload["rental_order_query"] = DomainShapedQuery(
                    PrivacySafeQuery(context.rental_order_query),
                    AdminAssistantDomains.Require("rental_order.query"));
            }

            return new ReqaiPlanRequest
            {
                version = "1",
                page_key = request.page_key,
                question = request.question,
                conversation = request.conversation.TakeLast(20).ToList(),
                context = payload,
                staff_id = staff.id,
                trace_id = traceId,
                current_date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)),
                timezone = "Asia/Shanghai"
            };
        }

        private static ReqaiFinalizeRequest BuildFinalizeRequest(AdminAssistantRequest request, string traceId,
            AssistantReply? plannerReply, AdminAssistantQueryExecution execution, AggregationRequest aggregation,
            AdminAssistantDomain domain) => new()
        {
            version = "1",
            trace_id = traceId,
            question = RedactFreeText(request.question) ?? string.Empty,
            planner_reply = PrivacySafeReply(plannerReply),
            executed = new ReqaiExecutedQuery
            {
                type = domain.actionType,
                query = DomainShapedQuery(PrivacySafeQuery(execution.state), domain),
                aggregation = aggregation,
                summary = ToReqaiSummary(execution.summary, domain)
            }
        };

        /// <summary>
        /// 只保留本域有的字段。reqai 那边每个域的模型都是 extra=forbid 的，
        /// 多带一个别域的 null 字段整个请求就会被拒。
        /// </summary>
        private static Dictionary<string, object?> DomainShapedQuery(
            AdminAssistantQueryState state, AdminAssistantDomain domain)
        {
            Dictionary<string, object?> shaped = new(StringComparer.Ordinal);
            foreach (string field in domain.fields)
            {
                object? value = state.Read(field);
                shaped[field] = value is DateTime date ? date.ToString("yyyy-MM-dd") : value;
            }
            return shaped;
        }

        private static ReqaiQuerySummary ToReqaiSummary(QuerySummary summary, AdminAssistantDomain domain) => new()
        {
            metrics = new Dictionary<string, double>(summary.metrics),
            groups = summary.groups.Select(group =>
            {
                Dictionary<string, object?> shaped = new(StringComparer.Ordinal);
                foreach (string key in domain.groupBy)
                    shaped[key] = group.keys.TryGetValue(key, out string? value) ? value : null;
                shaped["metrics"] = new Dictionary<string, double>(group.metrics);
                return shaped;
            }).ToList()
        };

        private static AdminAssistantResponse TextOnly(string traceId, AssistantReply reply, AdminAssistantContext? context) => new()
        {
            version = "1",
            trace_id = traceId,
            reply = reply,
            actions = new List<ClientAssistantAction>(),
            context = context ?? new AdminAssistantContext()
        };

        private static AdminAssistantResponse CompletedQuery(string traceId, string actionId, AssistantReply reply,
            AdminAssistantQueryExecution execution, AdminAssistantDomain domain, AdminAssistantContext? previous)
        {
            // 保留其它域已有的条件：店员在域之间来回切换时，各自的筛选不该被冲掉。
            AdminAssistantContext context = new()
            {
                active_query_type = domain.actionType,
                rental_order_query = previous?.rental_order_query,
                care_order_query = previous?.care_order_query,
                retail_order_query = previous?.retail_order_query,
                ski_pass_query = previous?.ski_pass_query
            };
            context.Set(domain.contextKey, execution.state);

            return new AdminAssistantResponse
            {
                version = "1",
                trace_id = traceId,
                reply = reply,
                actions = new List<ClientAssistantAction>
                {
                    new ClientAssistantAction
                    {
                        id = actionId,
                        type = domain.clientActionType,
                        state = execution.state,
                        summary = execution.summary
                    }
                },
                context = context
            };
        }

        private static AssistantReply DeterministicReply(QuerySummary summary, AdminAssistantDomain domain)
        {
            double count = summary.metrics.TryGetValue(domain.countMetric, out double primary) ? primary : 0;
            double unpaid = summary.metrics.TryGetValue("unpaid_count", out double unpaidCount) ? unpaidCount : 0;
            return new AssistantReply
            {
                text = unpaid > 0
                    ? $"查询完成，{domain.label}共 {count:0} {domain.unit}，其中未支付 {unpaid:0} {domain.unit}。"
                    : $"查询完成，{domain.label}共 {count:0} {domain.unit}。"
            };
        }

        private static void RequireLevel(Staff staff, int requiredLevel, AdminAssistantAuditData audit)
        {
            if (staff.title_level < requiredLevel) throw new AdminAssistantPermissionException(audit);
        }

        private static AdminAssistantAuditData Audit(string? plannerJson, string validationResult,
            string? actionType, AdminAssistantQueryState? state, QuerySummary? summary, string? error) => new()
        {
            planner_json = RedactFreeText(plannerJson),
            validation_result = validationResult,
            action_type = actionType,
            query = state == null ? null : PrivacySafeQuery(state),
            summary = summary,
            error = error
        };

        private static AdminAssistantAuditData Audit(string? plannerJson, string validationResult,
            AdminAssistantQueryState? state, QuerySummary? summary, string? error) =>
            Audit(plannerJson, validationResult, null, state, summary, error);

        private static AssistantReply? PrivacySafeReply(AssistantReply? reply) => reply == null ? null : new AssistantReply
        {
            text = RedactFreeText(reply.text) ?? "[已隐去敏感信息]",
            citations = new List<JsonElement>()
        };

        private static AdminAssistantQueryState PrivacySafeQuery(AdminAssistantQueryState state) => new()
        {
            start_date = state.start_date,
            end_date = state.end_date,
            shop = state.shop,
            is_test = state.is_test,
            is_entertain = state.is_entertain,
            have_discount = state.have_discount,
            cell_suffix = LastFour(state.cell_suffix),
            rent_status = state.rent_status,
            use_card = state.use_card,
            has_retail = state.has_retail,
            keyword = RedactFreeText(state.keyword),
            is_summer_care = state.is_summer_care,
            retail_type = state.retail_type
        };

        private static string? LastFour(string? value) => value == null ? null : value.Length <= 4 ? value : value[^4..];

        private static string? RedactFreeText(string? value)
        {
            if (value == null) return null;
            string withoutPhones = PhoneLike.Replace(value, "[已隐去手机号]");
            return SensitiveCredential.Replace(withoutPhones, "[已隐去敏感信息]");
        }

        private static string ProtocolPayload(string plannerJson)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(plannerJson);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("规划响应必须是对象");

                HashSet<string> allowed = new(StringComparer.Ordinal)
                    { "version", "reply", "actions", "unsupported", "model", "effort" };
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

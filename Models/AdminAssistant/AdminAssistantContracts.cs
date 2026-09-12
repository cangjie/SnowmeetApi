using System;
using System.Collections.Generic;
using System.Text.Json;
using SnowmeetApi.Helpers;

namespace SnowmeetApi.Models.AdminAssistant
{
    public sealed class AdminAssistantRequest
    {
        public string version { get; set; } = "1";
        public string page_key { get; set; } = "";
        public string question { get; set; } = "";
        public List<AssistantConversationMessage> conversation { get; set; } = new();
        public AdminAssistantContext context { get; set; } = new();
    }

    public sealed class AssistantConversationMessage
    {
        public string role { get; set; } = "";
        public string content { get; set; } = "";
    }

    /// <summary>
    /// 每个业务域各存一份查询条件，另外记住「当前进行中的是哪个域」。
    ///
    /// 分域保存而不是只留一份，是为了让店员在域之间来回切换时各自的条件还在；
    /// active_query_type 则决定 patch（「改成五月」）落在谁身上 —— 多域之后这是
    /// 一个全新的错误类别，必须显式表达，不能靠猜。
    /// </summary>
    public sealed class AdminAssistantContext
    {
        public string? active_query_type { get; set; }
        public AdminAssistantQueryState? rental_order_query { get; set; }
        public AdminAssistantQueryState? care_order_query { get; set; }
        public AdminAssistantQueryState? retail_order_query { get; set; }
        public AdminAssistantQueryState? ski_pass_query { get; set; }

        public AdminAssistantQueryState? Get(string contextKey) => contextKey switch
        {
            "rental_order_query" => rental_order_query,
            "care_order_query" => care_order_query,
            "retail_order_query" => retail_order_query,
            "ski_pass_query" => ski_pass_query,
            _ => null
        };

        public void Set(string contextKey, AdminAssistantQueryState? value)
        {
            switch (contextKey)
            {
                case "rental_order_query": rental_order_query = value; break;
                case "care_order_query": care_order_query = value; break;
                case "retail_order_query": retail_order_query = value; break;
                case "ski_pass_query": ski_pass_query = value; break;
            }
        }
    }

    public sealed class AdminAssistantResponse
    {
        public string version { get; set; } = "1";
        public string trace_id { get; set; } = "";
        public AssistantReply reply { get; set; } = new();
        public List<ClientAssistantAction> actions { get; set; } = new();
        public AdminAssistantContext context { get; set; } = new();
    }

    public sealed class AssistantReply
    {
        public string text { get; set; } = "";
        public List<JsonElement> citations { get; set; } = new();
    }

    /// <summary>
    /// 下发给小程序的客户端指令。入站的 &lt;域&gt;.query 绝不原样下发，
    /// 统一换成经过校验与执行之后签发的 &lt;域&gt;.show_results。
    /// </summary>
    public sealed class ClientAssistantAction
    {
        public const string CompletedStatus = "completed";

        public string id { get; init; } = "";
        public string type { get; init; } = "";
        public string status { get; } = CompletedStatus;
        public AdminAssistantQueryState state { get; init; } = new();
        public QuerySummary summary { get; init; } = new();
    }

    public sealed class ReqaiPlanResponse
    {
        public string version { get; set; } = "1";
        public AssistantReply? reply { get; set; }
        public List<ReqaiPlanAction> actions { get; set; } = new();

        /// <summary>reqai 明确表示做不了。有它，做不了的请求才不会被降级成做得了的请求。</summary>
        public AdminAssistantUnsupported? unsupported { get; set; }
    }

    public sealed class AdminAssistantUnsupported
    {
        public string reason { get; set; } = "";
        public string target_domain { get; set; } = "";
        public string detail { get; set; } = "";
        public string suggested_page { get; set; } = "";
    }

    public sealed class ReqaiPlanAction
    {
        public string id { get; set; } = "";
        public string type { get; set; } = "rental_order.query";
        public string mode { get; set; } = "replace";
        public AdminAssistantQueryPatch arguments { get; set; } = new();
        public AggregationRequest aggregation { get; set; } = new();
    }

    public sealed class AggregationRequest
    {
        public List<string> metrics { get; set; } = new() { "order_count" };
        public List<string> group_by { get; set; } = new();
    }

    public sealed class QuerySummary
    {
        public Dictionary<string, double> metrics { get; set; } = new();
        public List<QuerySummaryGroup> groups { get; set; } = new();
    }

    public sealed class QuerySummaryGroup
    {
        public Dictionary<string, string> keys { get; set; } = new();
        public Dictionary<string, double> metrics { get; set; } = new();
    }

    /// <summary>
    /// 四个业务域共用的超集查询条件。
    ///
    /// 用超集而不是四个独立类型，是因为这个对象要原样回传给小程序并被它当作筛选状态；
    /// 域之间的隔离由 AdminAssistantDomains 的字段白名单在解析阶段保证 —— 不属于本域的
    /// 字段在 ParseArguments 就被拒了，永远到不了这里。
    /// </summary>
    public sealed class AdminAssistantQueryState
    {
        public DateTime? start_date { get; set; }
        public DateTime? end_date { get; set; }
        public string? shop { get; set; }
        public bool? is_test { get; set; }
        public bool? is_entertain { get; set; }
        public bool? have_discount { get; set; }
        public string? cell_suffix { get; set; }
        public string? rent_status { get; set; }
        public bool? use_card { get; set; }
        public bool? has_retail { get; set; }
        public string? keyword { get; set; }
        public bool? is_summer_care { get; set; }
        public string? retail_type { get; set; }

        public object? Read(string field) => field switch
        {
            "start_date" => start_date,
            "end_date" => end_date,
            "shop" => shop,
            "is_test" => is_test,
            "is_entertain" => is_entertain,
            "have_discount" => have_discount,
            "cell_suffix" => cell_suffix,
            "rent_status" => rent_status,
            "use_card" => use_card,
            "has_retail" => has_retail,
            "keyword" => keyword,
            "is_summer_care" => is_summer_care,
            "retail_type" => retail_type,
            _ => null
        };
    }

    public sealed class AdminAssistantQueryPatch
    {
        public HashSet<string> Specified { get; } = new(StringComparer.Ordinal);
        public DateTime? start_date { get; set; }
        public DateTime? end_date { get; set; }
        public string? shop { get; set; }
        public bool? is_test { get; set; }
        public bool? is_entertain { get; set; }
        public bool? have_discount { get; set; }
        public string? cell_suffix { get; set; }
        public string? rent_status { get; set; }
        public bool? use_card { get; set; }
        public bool? has_retail { get; set; }
        public string? keyword { get; set; }
        public bool? is_summer_care { get; set; }
        public string? retail_type { get; set; }
    }

    public interface IAdminAssistantAuditedFailure
    {
        AdminAssistantAuditData audit { get; }
    }

    public sealed class AdminAssistantClarificationException : Exception, IAdminAssistantAuditedFailure
    {
        public AdminAssistantAuditData audit { get; }

        public AdminAssistantClarificationException(string message) : this(message, new AdminAssistantAuditData()) { }

        public AdminAssistantClarificationException(string message, AdminAssistantAuditData audit) : base(message)
        {
            this.audit = audit;
        }
    }

    /// <summary>reqai 或本地校验判定这次请求超出能力范围：明确拒答，并告诉店员去哪里自己筛。</summary>
    public sealed class AdminAssistantUnsupportedException : Exception, IAdminAssistantAuditedFailure
    {
        public AdminAssistantAuditData audit { get; }
        public AdminAssistantUnsupported detail { get; }

        public AdminAssistantUnsupportedException(string message, AdminAssistantUnsupported detail,
            AdminAssistantAuditData audit) : base(message)
        {
            this.detail = detail;
            this.audit = audit;
        }
    }

    public sealed class AdminAssistantPermissionException : Exception, IAdminAssistantAuditedFailure
    {
        public AdminAssistantAuditData audit { get; }

        public AdminAssistantPermissionException() : this(new AdminAssistantAuditData()) { }

        public AdminAssistantPermissionException(AdminAssistantAuditData audit) : base("没有权限")
        {
            this.audit = audit;
        }
    }

    public enum AdminAssistantFailureStage
    {
        Planner,
        Execution
    }

    public sealed class AdminAssistantOperationException : Exception, IAdminAssistantAuditedFailure
    {
        public AdminAssistantFailureStage stage { get; }
        public AdminAssistantAuditData audit { get; }

        /// <summary>执行阶段失败时带上业务域，文案才不会在查养护时说「租赁订单查询暂不可用」。</summary>
        public string? domainLabel { get; }

        public AdminAssistantOperationException(AdminAssistantFailureStage stage, Exception innerException)
            : this(stage, innerException, new AdminAssistantAuditData())
        {
        }

        public AdminAssistantOperationException(AdminAssistantFailureStage stage, Exception innerException,
            AdminAssistantAuditData audit, string? domainLabel = null)
            : base(BuildMessage(stage, domainLabel), innerException)
        {
            this.stage = stage;
            this.audit = audit;
            this.domainLabel = domainLabel;
        }

        public static string BuildMessage(AdminAssistantFailureStage stage, string? domainLabel) =>
            stage == AdminAssistantFailureStage.Planner
                ? "管理员助手规划暂不可用"
                : (domainLabel ?? "订单") + "查询暂不可用";
    }

    public sealed class AdminAssistantAuditData
    {
        public string? planner_json { get; init; }
        public string validation_result { get; init; } = "";
        public string? action_type { get; init; }
        public AdminAssistantQueryState? query { get; init; }
        public QuerySummary? summary { get; init; }
        public string? unsupported_reason { get; init; }
        public string? target_domain { get; init; }
        public string? error { get; init; }
    }

    public sealed class ReqaiPlanRequest
    {
        public string version { get; init; } = "1";
        public string page_key { get; init; } = "";
        public string question { get; init; } = "";
        public List<AssistantConversationMessage> conversation { get; init; } = new();

        /// <summary>按域展开的上下文，形状由 AdminAssistantService 组装，reqai 侧只当普通 dict 读。</summary>
        public Dictionary<string, object?> context { get; init; } = new();
        public int staff_id { get; init; }
        public string trace_id { get; init; } = "";
        public DateOnly current_date { get; init; }
        public string timezone { get; init; } = "Asia/Shanghai";
    }

    public sealed class ReqaiFinalizeRequest
    {
        public string version { get; init; } = "1";
        public string trace_id { get; init; } = "";
        public string question { get; init; } = "";
        public AssistantReply? planner_reply { get; init; }

        /// <summary>把域、条件、聚合、汇总打包在一起，reqai 那边才能交叉校验它们同属一个域。</summary>
        public ReqaiExecutedQuery executed { get; init; } = new();
    }

    /// <summary>
    /// 发给 reqai 的执行结果。query 与 group 的键都按业务域裁剪：
    /// reqai 的每域模型是 extra=forbid 的，多带一个别域的 null 字段就会被整体拒绝。
    /// </summary>
    public sealed class ReqaiExecutedQuery
    {
        public string type { get; init; } = "";
        public Dictionary<string, object?> query { get; init; } = new();
        public AggregationRequest aggregation { get; init; } = new();
        public ReqaiQuerySummary summary { get; init; } = new();
    }

    public sealed class ReqaiQuerySummary
    {
        public Dictionary<string, double> metrics { get; init; } = new();
        public List<Dictionary<string, object?>> groups { get; init; } = new();
    }

    public sealed class LegacyRentIntent
    {
        public string status { get; init; } = "";
        public DateTime? start_date { get; init; }
        public DateTime? end_date { get; init; }
        public string? shop { get; init; }
        public string? rent_status { get; init; }
        public bool? is_test { get; init; }
        public bool? is_entertain { get; init; }
        public bool? have_discount { get; init; }
        public bool? use_card { get; init; }
        public string? cell_suffix { get; init; }
        public string? keyword { get; init; }
        public string? clarification { get; init; }
    }
}

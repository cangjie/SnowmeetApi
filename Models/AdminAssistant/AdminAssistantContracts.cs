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

    public sealed class AdminAssistantContext
    {
        public RentalOrderQueryState? rental_order_query { get; set; }
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

    public sealed class ClientAssistantAction
    {
        public const string ShowResultsType = "rental_order.show_results";
        public const string CompletedStatus = "completed";

        public string id { get; init; } = "";
        public string type { get; } = ShowResultsType;
        public string status { get; } = CompletedStatus;
        public RentalOrderQueryState state { get; init; } = new();
        public RentalOrderQuerySummary summary { get; init; } = new(new Dictionary<string, double>(), new List<RentalOrderQuerySummaryGroup>());
    }

    public sealed class ReqaiPlanResponse
    {
        public string version { get; set; } = "1";
        public AssistantReply? reply { get; set; }
        public List<ReqaiPlanAction> actions { get; set; } = new();
    }

    public sealed class ReqaiPlanAction
    {
        public string id { get; set; } = "";
        public string type { get; set; } = "rental_order.query";
        public string mode { get; set; } = "replace";
        public RentalOrderQueryPatch arguments { get; set; } = new();
        public AggregationRequest aggregation { get; set; } = new();
    }

    public sealed class AggregationRequest
    {
        public List<string> metrics { get; set; } = new() { "order_count" };
        public List<string> group_by { get; set; } = new();
    }

    public sealed class QuerySummary
    {
        public Dictionary<string, decimal> metrics { get; set; } = new();
        public List<QuerySummaryGroup> groups { get; set; } = new();
    }

    public sealed class QuerySummaryGroup
    {
        public Dictionary<string, string> keys { get; set; } = new();
        public Dictionary<string, decimal> metrics { get; set; } = new();
    }

    public sealed class RentalOrderQueryState
    {
        public DateTime? start_date { get; set; }
        public DateTime? end_date { get; set; }
        public string? shop { get; set; }
        public string? rent_status { get; set; }
        public bool? is_test { get; set; }
        public bool? is_entertain { get; set; }
        public bool? have_discount { get; set; }
        public bool? use_card { get; set; }
        public bool? has_retail { get; set; }
        public string? cell_suffix { get; set; }
        public string? keyword { get; set; }
    }

    public sealed class RentalOrderQueryPatch
    {
        public HashSet<string> Specified { get; } = new(StringComparer.Ordinal);
        public DateTime? start_date { get; set; }
        public DateTime? end_date { get; set; }
        public string? shop { get; set; }
        public string? rent_status { get; set; }
        public bool? is_test { get; set; }
        public bool? is_entertain { get; set; }
        public bool? have_discount { get; set; }
        public bool? use_card { get; set; }
        public bool? has_retail { get; set; }
        public string? cell_suffix { get; set; }
        public string? keyword { get; set; }
    }

    public sealed class AdminAssistantClarificationException : Exception
    {
        public AdminAssistantClarificationException(string message) : base(message) { }
    }

    public sealed class AdminAssistantPermissionException : Exception
    {
        public AdminAssistantPermissionException() : base("没有权限") { }
    }

    public enum AdminAssistantFailureStage
    {
        Planner,
        Execution
    }

    public sealed class AdminAssistantOperationException : Exception
    {
        public AdminAssistantFailureStage stage { get; }

        public AdminAssistantOperationException(AdminAssistantFailureStage stage, Exception innerException)
            : base(stage == AdminAssistantFailureStage.Planner ? "管理员助手规划暂不可用" : "租赁订单查询暂不可用", innerException)
        {
            this.stage = stage;
        }
    }

    public sealed class AdminAssistantAuditData
    {
        public string? planner_json { get; init; }
        public string validation_result { get; init; } = "";
        public RentalOrderQueryState? query { get; init; }
        public RentalOrderQuerySummary? summary { get; init; }
        public string? error { get; init; }
    }

    public sealed class ReqaiPlanRequest
    {
        public string version { get; init; } = "1";
        public string page_key { get; init; } = "";
        public string question { get; init; } = "";
        public List<AssistantConversationMessage> conversation { get; init; } = new();
        public AdminAssistantContext context { get; init; } = new();
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
        public RentalOrderQueryState query { get; init; } = new();
        public AggregationRequest aggregation { get; init; } = new();
        public ReqaiQuerySummary summary { get; init; } = new();
    }

    public sealed class ReqaiQuerySummary
    {
        public Dictionary<string, double> metrics { get; init; } = new();
        public List<ReqaiQuerySummaryGroup> groups { get; init; } = new();
    }

    public sealed class ReqaiQuerySummaryGroup
    {
        public string? rent_status { get; init; }
        public string? shop { get; init; }
        public DateOnly? biz_date { get; init; }
        public Dictionary<string, double> metrics { get; init; } = new();
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

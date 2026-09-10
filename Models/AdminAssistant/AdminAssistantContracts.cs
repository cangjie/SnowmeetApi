using System;
using System.Collections.Generic;
using System.Text.Json;

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
        public string id { get; set; } = "";
        public string type { get; set; } = "rental_order.show_results";
        public string status { get; set; } = "completed";
        public RentalOrderQueryState state { get; set; } = new();
        public QuerySummary summary { get; set; } = new();
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
}

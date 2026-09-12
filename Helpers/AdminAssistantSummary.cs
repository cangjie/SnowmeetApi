using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 聚合用的一行。
    ///
    /// 永远是「一个订单一行」，即使雪票域的数据源是票券表 —— 票券按订单去重后再折算成
    /// TicketCount，否则一单多票会把金额重复累加，算出一个看起来合理的错数字。
    /// </summary>
    public sealed record AssistantAggregateRow(
        int OrderId,
        string? Shop,
        DateTime BizDate,
        string? RentStatus,
        double ChargeTotal,
        double PaidTotal,
        double RefundTotal,
        bool IsUnpaid,
        int TicketCount = 1);

    public static class AdminAssistantSummary
    {
        public static QuerySummary Build(
            IReadOnlyCollection<AssistantAggregateRow> rows,
            AdminAssistantDomain domain,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy)
        {
            Validate(domain, metrics, groupBy);
            return new QuerySummary
            {
                metrics = BuildMetrics(rows, metrics),
                groups = BuildGroups(rows, groupBy, metrics)
            };
        }

        private static Dictionary<string, double> BuildMetrics(
            IEnumerable<AssistantAggregateRow> rows,
            IReadOnlyCollection<string> metrics)
        {
            List<AssistantAggregateRow> values = rows.ToList();
            Dictionary<string, double> result = new();
            if (metrics.Contains("order_count")) result["order_count"] = values.Count;
            if (metrics.Contains("ticket_count")) result["ticket_count"] = values.Sum(row => row.TicketCount);
            if (metrics.Contains("charge_total")) result["charge_total"] = values.Sum(row => row.ChargeTotal);
            if (metrics.Contains("paid_total")) result["paid_total"] = values.Sum(row => row.PaidTotal);
            if (metrics.Contains("refund_total")) result["refund_total"] = values.Sum(row => row.RefundTotal);
            if (metrics.Contains("unpaid_count")) result["unpaid_count"] = values.Count(row => row.IsUnpaid);
            return result;
        }

        private static List<QuerySummaryGroup> BuildGroups(
            IReadOnlyCollection<AssistantAggregateRow> rows,
            IReadOnlyList<string> groupBy,
            IReadOnlyCollection<string> metrics)
        {
            if (groupBy.Count == 0) return new List<QuerySummaryGroup>();

            return rows
                .GroupBy(row => groupBy.Select(group => GroupValue(row, group)).ToArray(), StringArrayComparer.Instance)
                .Select(group => new QuerySummaryGroup
                {
                    keys = groupBy.Select((name, index) => new KeyValuePair<string, string>(name, group.Key[index]))
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    metrics = BuildMetrics(group, metrics)
                })
                .ToList();
        }

        private static string GroupValue(AssistantAggregateRow row, string group) => group switch
        {
            "shop" => row.Shop ?? "",
            "rent_status" => row.RentStatus ?? "",
            "biz_date" => row.BizDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("聚合分组不支持")
        };

        private static void Validate(
            AdminAssistantDomain domain,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy)
        {
            if (metrics.Any(metric => !domain.metrics.Contains(metric)))
                throw new InvalidOperationException("聚合指标不支持");
            if (groupBy.Any(group => !domain.groupBy.Contains(group)))
                throw new InvalidOperationException("聚合分组不支持");
        }

        private sealed class StringArrayComparer : IEqualityComparer<string[]>
        {
            public static readonly StringArrayComparer Instance = new();

            public bool Equals(string[]? x, string[]? y) => x != null && y != null && x.SequenceEqual(y, StringComparer.Ordinal);

            public int GetHashCode(string[] values)
            {
                HashCode hash = new();
                foreach (string value in values) hash.Add(value, StringComparer.Ordinal);
                return hash.ToHashCode();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SnowmeetApi.Helpers
{
    public sealed record RentalOrderAggregateRow(
        int OrderId,
        string Shop,
        DateTime BizDate,
        string RentStatus,
        double ChargeTotal,
        double PaidTotal,
        double RefundTotal,
        bool IsUnpaid);

    public sealed record RentalOrderQuerySummary(
        Dictionary<string, double> metrics,
        List<RentalOrderQuerySummaryGroup> groups);

    public sealed record RentalOrderQuerySummaryGroup(
        Dictionary<string, string> keys,
        Dictionary<string, double> metrics);

    public static class RentalOrderAssistantSummary
    {
        public static RentalOrderQuerySummary Build(
            IReadOnlyCollection<RentalOrderAggregateRow> rows,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy)
        {
            Validate(metrics, groupBy);
            return new RentalOrderQuerySummary(BuildMetrics(rows, metrics), BuildGroups(rows, groupBy, metrics));
        }

        private static Dictionary<string, double> BuildMetrics(
            IEnumerable<RentalOrderAggregateRow> rows,
            IReadOnlyCollection<string> metrics)
        {
            List<RentalOrderAggregateRow> values = rows.ToList();
            Dictionary<string, double> result = new();
            if (metrics.Contains("order_count")) result["order_count"] = values.Count;
            if (metrics.Contains("charge_total")) result["charge_total"] = values.Sum(row => row.ChargeTotal);
            if (metrics.Contains("paid_total")) result["paid_total"] = values.Sum(row => row.PaidTotal);
            if (metrics.Contains("refund_total")) result["refund_total"] = values.Sum(row => row.RefundTotal);
            if (metrics.Contains("unpaid_count")) result["unpaid_count"] = values.Count(row => row.IsUnpaid);
            return result;
        }

        private static List<RentalOrderQuerySummaryGroup> BuildGroups(
            IReadOnlyCollection<RentalOrderAggregateRow> rows,
            IReadOnlyList<string> groupBy,
            IReadOnlyCollection<string> metrics)
        {
            if (groupBy.Count == 0) return new List<RentalOrderQuerySummaryGroup>();

            return rows
                .GroupBy(row => groupBy.Select(group => GroupValue(row, group)).ToArray(), StringArrayComparer.Instance)
                .Select(group => new RentalOrderQuerySummaryGroup(
                    groupBy.Select((name, index) => new KeyValuePair<string, string>(name, group.Key[index]))
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                    BuildMetrics(group, metrics)))
                .ToList();
        }

        private static string GroupValue(RentalOrderAggregateRow row, string group) => group switch
        {
            "shop" => row.Shop,
            "rent_status" => row.RentStatus,
            "biz_date" => row.BizDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("聚合分组不支持")
        };

        private static void Validate(IReadOnlyCollection<string> metrics, IReadOnlyList<string> groupBy)
        {
            HashSet<string> supportedMetrics = new(StringComparer.Ordinal)
            {
                "order_count", "charge_total", "paid_total", "refund_total", "unpaid_count"
            };
            HashSet<string> supportedGroups = new(StringComparer.Ordinal) { "shop", "rent_status", "biz_date" };
            if (metrics.Any(metric => !supportedMetrics.Contains(metric))) throw new InvalidOperationException("聚合指标不支持");
            if (groupBy.Any(group => !supportedGroups.Contains(group))) throw new InvalidOperationException("聚合分组不支持");
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

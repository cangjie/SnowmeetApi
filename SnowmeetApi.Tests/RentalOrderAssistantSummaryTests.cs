using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class RentalOrderAssistantSummaryTests
    {
        [Fact]
        public void 汇总不截断200单并可按状态门店分组()
        {
            string[] allMetrics = { "order_count", "charge_total", "paid_total", "refund_total", "unpaid_count" };
            List<AssistantAggregateRow> rows = Enumerable.Range(1, 250)
                .Select(i => new AssistantAggregateRow(
                    i,
                    i % 2 == 0 ? "万龙" : "南山",
                    D("2026-04-01"),
                    i % 3 == 0 ? "未支付" : "全部归还",
                    100,
                    80,
                    5,
                    i % 3 == 0))
                .ToList();

            QuerySummary summary = AdminAssistantSummary.Build(rows, AdminAssistantDomains.Require("rental_order.query"), allMetrics, new[] { "shop", "rent_status" });

            Assert.Equal(250, summary.metrics["order_count"]);
            Assert.Equal(25000d, summary.metrics["charge_total"]);
            Assert.Equal(4, summary.groups.Count);
        }

        private static DateTime D(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);
    }
}

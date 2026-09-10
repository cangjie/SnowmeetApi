using System;
using System.Globalization;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class AdminAssistantProtocolRulesTests
    {
        [Fact]
        public void Patch只修改出现的字段并允许null清除()
        {
            RentalOrderQueryState current = new()
            {
                start_date = D("2026-04-01"), end_date = D("2026-04-30"), shop = "万龙", rent_status = "未支付"
            };

            RentalOrderQueryPatch patch = AdminAssistantProtocolRules.ParseArguments("{\"shop\":null,\"rent_status\":\"全部归还\"}");
            RentalOrderQueryState merged = AdminAssistantProtocolRules.Merge("patch", current, patch);

            Assert.Null(merged.shop);
            Assert.Equal("全部归还", merged.rent_status);
            Assert.Equal(D("2026-04-01"), merged.start_date);
        }

        [Fact]
        public void Replace从空状态开始只保留出现的字段()
        {
            RentalOrderQueryState current = new() { start_date = D("2026-04-01"), shop = "万龙", is_test = true };

            RentalOrderQueryState merged = AdminAssistantProtocolRules.Merge(
                "replace", current, AdminAssistantProtocolRules.ParseArguments("{\"end_date\":\"2026-04-30\"}"));

            Assert.Null(merged.start_date);
            Assert.Null(merged.shop);
            Assert.Null(merged.is_test);
            Assert.Equal(D("2026-04-30"), merged.end_date);
        }

        [Fact]
        public void 未知action和参数被拒绝()
        {
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ParsePlan(PlanJson("refund.execute")));
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ParseArguments("{\"sql\":\"select 1\"}"));
        }

        [Theory]
        [InlineData("{\"version\":\"1\",\"reply\":null,\"actions\":[],\"route\":\"/refund\"}")]
        [InlineData("{\"version\":\"1\",\"reply\":{\"text\":\"ok\",\"unsafe\":true},\"actions\":[]}")]
        [InlineData("{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"rental_order.query\",\"mode\":\"replace\",\"arguments\":{},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[],\"sql\":\"x\"}}]}")]
        public void 任意协议层未知字段都被拒绝(string json)
        {
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ParsePlan(json));
        }

        [Fact]
        public void 重复的协议字段被拒绝而不是任意取值()
        {
            const string json = "{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"refund.execute\",\"type\":\"rental_order.query\",\"mode\":\"replace\",\"arguments\":{},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}";

            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ParsePlan(json));
        }

        [Fact]
        public void 计划保留显式参数字段并限制聚合白名单()
        {
            ReqaiPlanResponse plan = AdminAssistantProtocolRules.ParsePlan(PlanJson("rental_order.query"));

            Assert.Single(plan.actions);
            Assert.Contains("start_date", plan.actions[0].arguments.Specified);
            Assert.Equal("order_count", plan.actions[0].aggregation.metrics[0]);
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ParsePlan(PlanJson("rental_order.query").Replace("order_count", "customer_rows")));
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ParsePlan(PlanJson("rental_order.query").Replace("\"group_by\":[]", "\"group_by\":[\"shop\",\"shop\"]")));
        }

        [Fact]
        public void 查询缺少日期抛出可澄清异常()
        {
            Assert.Throws<AdminAssistantClarificationException>(() => AdminAssistantProtocolRules.ValidateQuery(new RentalOrderQueryState()));
        }

        [Theory]
        [InlineData("2026-04-30", "2026-04-01")]
        [InlineData("2026-04-01", "2027-04-02")]
        public void 查询拒绝倒置或超过365天的日期(string start, string end)
        {
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ValidateQuery(new RentalOrderQueryState
            {
                start_date = D(start), end_date = D(end)
            }));
        }

        [Fact]
        public void 查询日期靠近DateTime上界且不超过365天时有效()
        {
            AdminAssistantProtocolRules.ValidateQuery(new RentalOrderQueryState
            {
                start_date = D("9999-01-01"), end_date = D("9999-12-31")
            });
        }

        [Fact]
        public void 查询拒绝非法状态和手机号后缀()
        {
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ValidateQuery(new RentalOrderQueryState
            {
                start_date = D("2026-04-01"), end_date = D("2026-04-30"), rent_status = "已退款"
            }));
            Assert.Throws<InvalidOperationException>(() => AdminAssistantProtocolRules.ValidateQuery(new RentalOrderQueryState
            {
                start_date = D("2026-04-01"), end_date = D("2026-04-30"), cell_suffix = "123a"
            }));
        }

        private static DateTime D(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);

        private static string PlanJson(string actionType) =>
            "{\"version\":\"1\",\"reply\":null,\"actions\":[{\"id\":\"a1\",\"type\":\"" + actionType +
            "\",\"mode\":\"replace\",\"arguments\":{\"start_date\":\"2026-04-01\",\"end_date\":\"2026-04-30\"},\"aggregation\":{\"metrics\":[\"order_count\"],\"group_by\":[]}}]}";
    }
}

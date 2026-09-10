using System.Collections.Generic;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class OrderQueryRulesTests
    {
        [Fact]
        public void 手机号后缀只保留匹配的订单()
        {
            List<Order> orders = new()
            {
                new Order { id = 1, contact_num = "13800137788" },
                new Order { id = 2, contact_num = "13900136699" }
            };

            List<Order> filtered = OrderQueryRules.FilterByCustomerCellSuffix(orders, "7788");

            Assert.Single(filtered);
            Assert.Equal(1, filtered[0].id);
        }

        [Fact]
        public void 未指定手机号时保留全部订单()
        {
            List<Order> orders = new()
            {
                new Order { id = 1, contact_num = "13800137788" },
                new Order { id = 2, contact_num = "13900136699" }
            };

            List<Order> filtered = OrderQueryRules.FilterByCustomerCellSuffix(orders, null);

            Assert.Equal(2, filtered.Count);
        }
    }
}

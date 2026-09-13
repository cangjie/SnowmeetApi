using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 守住「下发形状」这条跨仓契约。
    ///
    /// 服务端内部用四域共用的超集类搬运条件，小程序却按域严格校验、拒绝本域没有的字段。
    /// 两者曾经不一致：下发时把超集的 13 个键全发了出去，小程序把整条响应判为非法，
    /// 表现是「暂时无法获得回答」而服务端日志一切正常——四个域全都跳不了。
    /// 注册表一致性由跨仓脚本核对，这里核对的是真正发出去的那串 JSON。
    /// </summary>
    public class AdminAssistantWireTests
    {
        private static AdminAssistantQueryState FullyPopulated() => new()
        {
            start_date = new System.DateTime(2026, 3, 21),
            end_date = new System.DateTime(2026, 3, 31),
            shop = "万龙服务中心",
            is_test = false,
            is_entertain = false,
            have_discount = true,
            cell_suffix = "7788",
            rent_status = "未支付",
            use_card = true,
            has_retail = true,
            keyword = "张伟",
            is_summer_care = true,
            retail_type = "养护卡类"
        };

        [Theory]
        [InlineData("rental_order.query")]
        [InlineData("care_order.query")]
        [InlineData("retail_order.query")]
        [InlineData("ski_pass.query")]
        public void 下发条件的键集合恰好是本域字段(string actionType)
        {
            AdminAssistantDomain domain = AdminAssistantDomains.Require(actionType);

            Dictionary<string, object?> wire = AdminAssistantWire.State(FullyPopulated(), domain);

            Assert.Equal(domain.fields.OrderBy(name => name), wire.Keys.OrderBy(name => name));
        }

        [Theory]
        [InlineData("care_order.query", "rent_status")]
        [InlineData("care_order.query", "retail_type")]
        [InlineData("rental_order.query", "is_summer_care")]
        [InlineData("ski_pass.query", "shop")]
        public void 别的域的字段不会被带进下发条件(string actionType, string foreignField)
        {
            AdminAssistantDomain domain = AdminAssistantDomains.Require(actionType);

            Dictionary<string, object?> wire = AdminAssistantWire.State(FullyPopulated(), domain);

            Assert.DoesNotContain(foreignField, wire.Keys);
        }

        [Fact]
        public void 日期按日下发不带时分秒()
        {
            AdminAssistantDomain domain = AdminAssistantDomains.Require("care_order.query");

            Dictionary<string, object?> wire = AdminAssistantWire.State(FullyPopulated(), domain);

            Assert.Equal("2026-03-21", wire["start_date"]);
            Assert.Equal("2026-03-31", wire["end_date"]);
        }

        [Fact]
        public void 下发上下文每个域一个键且各自按域裁剪()
        {
            AdminAssistantContext context = new()
            {
                active_query_type = "care_order.query",
                care_order_query = FullyPopulated()
            };

            Dictionary<string, object?> wire = AdminAssistantWire.Context(context);

            Assert.Equal("care_order.query", wire["active_query_type"]);
            foreach (AdminAssistantDomain domain in AdminAssistantDomains.All.Values)
                Assert.Contains(domain.contextKey, wire.Keys);
            Assert.Null(wire["rental_order_query"]);

            Dictionary<string, object?> care = Assert.IsType<Dictionary<string, object?>>(wire["care_order_query"]);
            Assert.Equal(AdminAssistantDomains.Require("care_order.query").fields.OrderBy(name => name),
                care.Keys.OrderBy(name => name));
        }

        [Fact]
        public void 序列化后的响应不含别域字段()
        {
            AdminAssistantDomain domain = AdminAssistantDomains.Require("care_order.query");
            AdminAssistantResponse response = new()
            {
                trace_id = "trace",
                reply = new AssistantReply { text = "查询完成，养护订单共 221 单。" },
                actions = new List<ClientAssistantAction>
                {
                    new() { id = "a1", type = domain.clientActionType, state = AdminAssistantWire.State(FullyPopulated(), domain) }
                },
                context = AdminAssistantWire.Context(new AdminAssistantContext
                {
                    active_query_type = domain.actionType,
                    care_order_query = FullyPopulated()
                })
            };

            string json = JsonSerializer.Serialize(response);

            Assert.DoesNotContain("rent_status", json);
            Assert.DoesNotContain("has_retail", json);
            Assert.DoesNotContain("retail_type", json);
            Assert.Contains("is_summer_care", json);
        }
    }
}

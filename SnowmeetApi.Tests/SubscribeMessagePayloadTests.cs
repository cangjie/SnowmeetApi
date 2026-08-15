using System.Collections.Generic;
using System.Text.Json;
using SnowmeetApi.Helpers;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 订阅消息请求体的构造。单独拎出来测，是因为项目里既有的微信 JSON 全是手工字符串拼接，
    // 券名里只要出现一个引号就会拼出非法 JSON —— 这里必须走真正的序列化。
    public class SubscribeMessagePayloadTests
    {
        private static JsonElement Build(Dictionary<string, string> data, string state = "formal")
        {
            string json = SubscribeMessageHelper.BuildPayload(
                "oHdTn5W_test", "TPLID", "pages/mine/ticket/ticket_list", data, state);
            return JsonDocument.Parse(json).RootElement;
        }

        [Fact]
        public void 带上收件人模板和落地页()
        {
            JsonElement root = Build(new Dictionary<string, string> { { "thing1", "免费打蜡券" } });
            Assert.Equal("oHdTn5W_test", root.GetProperty("touser").GetString());
            Assert.Equal("TPLID", root.GetProperty("template_id").GetString());
            Assert.Equal("pages/mine/ticket/ticket_list", root.GetProperty("page").GetString());
        }

        [Fact]
        public void 每个字段都包成value对象()
        {
            JsonElement root = Build(new Dictionary<string, string>
            {
                { "thing1", "免费打蜡券" },
                { "time2", "2026年8月14日～2027年4月30日" },
                { "thing3", "对方已领取，回赠您一张同款券" }
            });
            JsonElement d = root.GetProperty("data");
            Assert.Equal("免费打蜡券", d.GetProperty("thing1").GetProperty("value").GetString());
            Assert.Equal("2026年8月14日～2027年4月30日", d.GetProperty("time2").GetProperty("value").GetString());
            Assert.Equal("对方已领取，回赠您一张同款券", d.GetProperty("thing3").GetProperty("value").GetString());
        }

        [Fact]
        public void 券名里的引号被正确转义而不是拼出非法JSON()
        {
            JsonElement root = Build(new Dictionary<string, string> { { "thing1", "他说\"免费\"打蜡券" } });
            Assert.Equal("他说\"免费\"打蜡券",
                root.GetProperty("data").GetProperty("thing1").GetProperty("value").GetString());
        }

        [Fact]
        public void 中文不被转成unicode转义序列()
        {
            string json = SubscribeMessageHelper.BuildPayload("o1", "t1", "p1",
                new Dictionary<string, string> { { "thing1", "免费打蜡券" } }, "formal");
            Assert.Contains("免费打蜡券", json);
        }

        [Fact]
        public void 可以指定体验版投递()
        {
            JsonElement root = Build(new Dictionary<string, string> { { "thing1", "x" } }, "trial");
            Assert.Equal("trial", root.GetProperty("miniprogram_state").GetString());
        }
    }
}

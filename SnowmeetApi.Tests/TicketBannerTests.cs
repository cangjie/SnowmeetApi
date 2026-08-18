using SnowmeetApi.Helpers;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 券种 banner：按券名关键词判定配色与短标签。
    //
    // 为什么不用 ticket_template.type：它有 9 种取值但粒度不对——「养护券」下面同时装着
    // 打蜡、修刃、双项、代金，而这几种恰恰是要区分的；反过来「租赁券」「消费券」又只有
    // 一两个模板。按券名关键词判定能覆盖现有全部 18 个模板，新增模板也自动适配、不用改代码。
    //
    // 用例就是生产库里全部 18 个模板名（2026-08-15 全量核对）。
    public class TicketBannerTests
    {
        private static string Cls(string name) { return TicketTransferRules.ResolveBanner(name).Cls; }
        private static string Label(string name) { return TicketTransferRules.ResolveBanner(name).Label; }

        [Theory]
        // 双项优先级最高：既修刃又打蜡的、名字里直接写双项的，都归双项
        [InlineData("养护双项", "dual", "双项")]
        [InlineData("非雪季赠双项", "dual", "双项")]      // 含"非雪季"但双项优先
        [InlineData("万龙店修板打蜡券", "dual", "双项")]   // 修板 + 打蜡 = 双项
        // 单项养护
        [InlineData("万龙店修板券", "edge", "修刃")]
        [InlineData("免费打蜡券", "wax", "机打蜡")]
        // 代金类
        [InlineData("满减券", "cash", "满减")]
        [InlineData("养护代金券", "cash", "代金")]
        [InlineData("觅计划消费券", "cash", "消费")]
        [InlineData("老顾客优惠券", "cash", "优惠")]
        [InlineData("22-23雪季内购券", "cash", "优惠")]
        // 体验/租赁
        [InlineData("试滑券", "pass", "试滑")]
        [InlineData("大疆体验券", "pass", "体验")]
        // 非雪季（不含双项时）
        [InlineData("非雪季养护券", "wax", "非雪季")]
        // 兜底：标签取券名去掉"券"字、最多 4 字
        [InlineData("养护券", "wax", "养护")]
        [InlineData("暖宝券", "wax", "暖宝")]
        [InlineData("毛线帽券", "wax", "毛线帽")]
        [InlineData("新手礼包", "wax", "新手礼包")]
        [InlineData("儿童成长计划", "wax", "儿童成长")]
        public void 全部十八个真实模板名(string name, string cls, string label)
        {
            Assert.Equal(cls, Cls(name));
            Assert.Equal(label, Label(name));
        }

        [Fact]
        public void 热打蜡走暖橙()
        {
            // 目前没有这个模板，但设计稿留了位；将来加「热打蜡券」要能自动命中
            Assert.Equal("hotwax", Cls("手工热打蜡券"));
            Assert.Equal("热打蜡", Label("热蜡券"));
        }

        [Fact]
        public void 课程走石墨()
        {
            Assert.Equal("course", Cls("教练课程券"));
        }

        [Fact]
        public void 空名不抛异常()
        {
            Assert.Equal("wax", Cls(null));
            Assert.Equal("优惠", Label(null));
            Assert.Equal("优惠", Label(""));
        }

        [Fact]
        public void 每个券种的样式类都不同()
        {
            string[] all = { Cls("养护双项"), Cls("万龙店修板券"), Cls("免费打蜡券"),
                             Cls("满减券"), Cls("试滑券"), Cls("热打蜡券"), Cls("教练课程券") };
            Assert.Equal(7, new System.Collections.Generic.HashSet<string>(all).Count);
        }
    }
}

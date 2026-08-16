using System;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 顾客端券列表卡片上的有效期文案与临期标记。
    // 跟时间字段一样，格式化在服务端做——前端不做日期解析。
    public class FormatExpireTests
    {
        [Fact]
        public void 有到期日时输出年月日不补零()
        {
            // 设计稿的写法是 2026.4.1，不是 2026.04.01
            Assert.Equal("2026.4.1", TicketTransferRules.FormatExpire(new DateTime(2026, 4, 1)));
        }

        [Fact]
        public void 没有到期日显示长期有效()
        {
            Assert.Equal("长期有效", TicketTransferRules.FormatExpire(null));
        }

        [Fact]
        public void 哨兵值9999也算长期有效()
        {
            // GenerateTicketByAction 对"模板没设到期日"写的是 DateTime.MaxValue
            Assert.Equal("长期有效", TicketTransferRules.FormatExpire(DateTime.MaxValue));
        }
    }

    public class IsExpiringSoonTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 15, 12, 0, 0);

        private static bool Soon(int used, DateTime? expire)
        {
            return TicketTransferRules.IsExpiringSoon(
                new Ticket() { code = "x", used = used, expire_date = expire }, Now);
        }

        [Fact]
        public void 三十天内到期算临期()
        {
            Assert.True(Soon(0, Now.AddDays(10)));
        }

        [Fact]
        public void 正好三十天算临期()
        {
            Assert.True(Soon(0, Now.AddDays(30)));
        }

        [Fact]
        public void 超过三十天不算()
        {
            Assert.False(Soon(0, Now.AddDays(31)));
        }

        [Fact]
        public void 已核销的不提醒()
        {
            Assert.False(Soon(1, Now.AddDays(3)));
        }

        [Fact]
        public void 已经过期的不算临期()
        {
            // 过期券归"已过期"状态处理，不该再顶着"即将到期"的红字
            Assert.False(Soon(0, Now.AddDays(-1)));
        }

        [Fact]
        public void 没有到期日的不算临期()
        {
            Assert.False(Soon(0, null));
        }

        [Fact]
        public void 哨兵值9999不算临期()
        {
            Assert.False(Soon(0, DateTime.MaxValue));
        }
    }
}

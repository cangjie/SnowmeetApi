using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 优惠券管理后台「默认范围 = 未过期 ∪ 已核销」的 EF 可翻译谓词。
    // 反面（被排除的）= 已过期 且 从未核销 = 废券。生产库里废券有 9174 张，
    // 不默认滤掉列表没法看。
    public class NotWastedFilterTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 15, 12, 0, 0);

        private static bool Kept(int used, DateTime? expire)
        {
            var f = TicketTransferRules.NotWastedFilter(Now).Compile();
            return f(new Ticket() { code = "x", used = used, expire_date = expire });
        }

        [Fact]
        public void 未核销且未过期保留()
        {
            Assert.True(Kept(0, Now.AddDays(30)));
        }

        [Fact]
        public void 已核销且未过期保留()
        {
            Assert.True(Kept(1, Now.AddDays(30)));
        }

        [Fact]
        public void 已核销即使过期也保留()
        {
            // 用掉的券是有效业务记录，不算废券
            Assert.True(Kept(1, Now.AddDays(-100)));
        }

        [Fact]
        public void 未核销且已过期是废券要排除()
        {
            Assert.False(Kept(0, Now.AddDays(-1)));
        }

        [Fact]
        public void 没有到期日的视为长期有效保留()
        {
            Assert.True(Kept(0, null));
        }

        [Fact]
        public void 当天到期的当天仍然保留()
        {
            Assert.True(Kept(0, new DateTime(2026, 8, 15, 0, 0, 0)));
        }

        [Fact]
        public void 昨天到期的排除()
        {
            Assert.False(Kept(0, new DateTime(2026, 8, 14, 23, 59, 59)));
        }
    }

    // NotExpiredFilter 是 IsNotExpired 的 EF 可翻译版本。
    // 两者必须逐项等价——「同一份口径两种形态」如果漂了，就会出现
    // 列表里能看到、详情里说过期这种自相矛盾。
    public class NotExpiredFilterConsistencyTests
    {
        [Fact]
        public void 与内存版逐项一致()
        {
            DateTime now = new DateTime(2026, 8, 15, 12, 0, 0);
            DateTime?[] cases = new DateTime?[]
            {
                null,
                new DateTime(2026, 8, 15, 0, 0, 0),      // 当天 00:00
                new DateTime(2026, 8, 15, 23, 59, 59),   // 当天 23:59
                new DateTime(2026, 8, 14, 23, 59, 59),   // 昨天
                new DateTime(2026, 8, 16, 0, 0, 0),      // 明天
                new DateTime(2027, 4, 30, 23, 59, 59),
                new DateTime(2020, 1, 1),
                DateTime.MaxValue                         // GenerateTicketByAction 的"无到期日"哨兵
            };
            var expr = TicketTransferRules.NotExpiredFilter(now).Compile();
            foreach (DateTime? c in cases)
            {
                Ticket t = new Ticket() { code = "x", expire_date = c };
                Assert.Equal(TicketTransferRules.IsNotExpired(t, now), expr(t));
            }
        }
    }

    // 券在管理后台列表上的状态标签。
    // 优先级：已核销 > 已过期 > 分享中 > 未使用。写反了肉眼很难发现，
    // 而 WXML 不支持方法调用，这套文案只能在服务端派生。
    public class DescribeStateTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 15, 12, 0, 0);

        private static string Label(int used, int shared, DateTime? expire)
        {
            return TicketAdminRules.DescribeState(
                new Ticket() { code = "x", used = used, shared = shared, expire_date = expire }, Now).Label;
        }

        [Fact]
        public void 未使用()
        {
            Assert.Equal("未使用", Label(0, 0, Now.AddDays(30)));
        }

        [Fact]
        public void 分享中()
        {
            Assert.Equal("分享中", Label(0, 1, Now.AddDays(30)));
        }

        [Fact]
        public void 已过期()
        {
            Assert.Equal("已过期", Label(0, 0, Now.AddDays(-1)));
        }

        [Fact]
        public void 已核销优先于已过期()
        {
            Assert.Equal("已核销", Label(1, 0, Now.AddDays(-1)));
        }

        [Fact]
        public void 已核销优先于分享中()
        {
            Assert.Equal("已核销", Label(1, 1, Now.AddDays(30)));
        }

        [Fact]
        public void 已过期优先于分享中()
        {
            // 分享中但已经过期了，对店员来说"过期"是更要紧的信息
            Assert.Equal("已过期", Label(0, 1, Now.AddDays(-1)));
        }

        [Fact]
        public void 每种状态都有独立的样式类()
        {
            var seen = new List<string>();
            foreach (var t in new[]
            {
                new Ticket() { code="a", used=1 },
                new Ticket() { code="b", expire_date = Now.AddDays(-1) },
                new Ticket() { code="c", shared=1, expire_date = Now.AddDays(30) },
                new Ticket() { code="d", expire_date = Now.AddDays(30) }
            })
            {
                seen.Add(TicketAdminRules.DescribeState(t, Now).Cls);
            }
            Assert.Equal(4, seen.Distinct().Count());
            Assert.DoesNotContain(seen, s => string.IsNullOrEmpty(s));
        }
    }

    public class ClampPagingTests
    {
        [Fact]
        public void 正常值不动()
        {
            Assert.Equal((3, 20), TicketAdminRules.ClampPaging(3, 20));
        }

        [Fact]
        public void 页码小于一回到第一页()
        {
            Assert.Equal((1, 20), TicketAdminRules.ClampPaging(0, 20));
            Assert.Equal((1, 20), TicketAdminRules.ClampPaging(-5, 20));
        }

        [Fact]
        public void 每页条数非法回到默认二十()
        {
            Assert.Equal((1, 20), TicketAdminRules.ClampPaging(1, 0));
            Assert.Equal((1, 20), TicketAdminRules.ClampPaging(1, -1));
        }

        [Fact]
        public void 每页条数上限一百()
        {
            // 明细视图分页后要按 code 批量捞 log，参数个数不能失控
            Assert.Equal((1, 20), TicketAdminRules.ClampPaging(1, 101));
            Assert.Equal((1, 100), TicketAdminRules.ClampPaging(1, 100));
        }
        // ── 发券来源（staff_id 为空时的兜底文案）────────────────────────
        // ticket.staff_id 2026-08-19 之前全库 NULL（CreateTicket 收了参数没存），
        // 历史券只能从 create_memo / channel 反推来源。期望值取自近一年实际分布。

        [Theory]
        [InlineData("养护完成赠送", "", "养护完成自动发放")]   // 1909 张
        [InlineData("非雪季养护", "", "非雪季养护下单")]        // 336 张
        [InlineData("非雪季赠双项", "", "非雪季养护下单")]      // 26 张
        [InlineData("转赠被领取回赠", "", "转赠回赠（系统）")]  // 4 张
        [InlineData("扫码领取", "", "顾客扫码领取")]
        [InlineData("买雪票增券", "", "买雪票赠券")]
        [InlineData("unipay_69798", "", "养护订单赠券")]
        [InlineData("体验订单获得,ID:12", "", "体验订单赠券")]
        [InlineData("养护订单获得,ID:12", "", "养护订单赠券")]
        public void 发券来源_按create_memo识别(string memo, string channel, string expected)
        {
            Assert.Equal(expected, TicketAdminRules.DescribeIssueSource(memo, channel));
        }

        [Theory]
        [InlineData("7353")]
        [InlineData("963")]
        public void 发券来源_纯数字memo是雪票id(string memo)
        {
            // 买雪票赠券那条路 create_memo 存的是 skiPass.id，近一年 963 张
            Assert.Equal("买雪票赠券", TicketAdminRules.DescribeIssueSource(memo, ""));
        }

        [Fact]
        public void 发券来源_memo为空时退回channel()
        {
            Assert.Equal("店员发放", TicketAdminRules.DescribeIssueSource("", "店员发放"));
            Assert.Equal("店员发放", TicketAdminRules.DescribeIssueSource(null, "店员发放"));
        }

        [Fact]
        public void 发券来源_两者都空时不留白()
        {
            Assert.Equal("系统发放", TicketAdminRules.DescribeIssueSource("", ""));
            Assert.Equal("系统发放", TicketAdminRules.DescribeIssueSource(null, null));
        }

        [Fact]
        public void 发券来源_认不出的memo原样显示()
        {
            Assert.Equal("管理员赠送", TicketAdminRules.DescribeIssueSource("管理员赠送", ""));
        }
    }
}

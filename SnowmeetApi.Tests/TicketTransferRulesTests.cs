using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 优惠券转赠回赠 / 领取上限的纯逻辑。
    // 这几条规则以后最容易被改错的是雪季末的 4-30 / 5-1 边界，所以边界单独立用例。
    public class SeasonEndDateTests
    {
        [Fact]
        public void 五月及以后发的券顺延到次年四月三十日()
        {
            Assert.Equal(new DateTime(2027, 4, 30, 23, 59, 59),
                TicketTransferRules.SeasonEndDate(new DateTime(2026, 8, 14, 11, 55, 0)));
        }

        [Fact]
        public void 一月发的券到当年四月三十日()
        {
            Assert.Equal(new DateTime(2027, 4, 30, 23, 59, 59),
                TicketTransferRules.SeasonEndDate(new DateTime(2027, 1, 15, 9, 0, 0)));
        }

        [Fact]
        public void 四月三十日当天仍算本雪季()
        {
            Assert.Equal(new DateTime(2027, 4, 30, 23, 59, 59),
                TicketTransferRules.SeasonEndDate(new DateTime(2027, 4, 30, 18, 0, 0)));
        }

        [Fact]
        public void 五月一日已进入下个雪季周期()
        {
            Assert.Equal(new DateTime(2028, 4, 30, 23, 59, 59),
                TicketTransferRules.SeasonEndDate(new DateTime(2027, 5, 1, 0, 0, 1)));
        }
    }

    // 订阅消息 thing 类型字段上限 20 个字符，超了微信直接报 47003
    public class TruncateThingTests
    {
        [Fact]
        public void 不超长的原样返回()
        {
            Assert.Equal("免费打蜡券", TicketTransferRules.TruncateThing("免费打蜡券"));
        }

        [Fact]
        public void 恰好二十字不截断()
        {
            string twenty = new string('券', 20);
            Assert.Equal(twenty, TicketTransferRules.TruncateThing(twenty));
        }

        [Fact]
        public void 超过二十字截到二十()
        {
            string twentyOne = new string('券', 21);
            Assert.Equal(new string('券', 20), TicketTransferRules.TruncateThing(twentyOne));
        }

        [Fact]
        public void 空值返回空串不抛异常()
        {
            Assert.Equal("", TicketTransferRules.TruncateThing(null));
        }
    }

    // time2 是 time 类型，只到日、用 ～ 连接时间段
    public class FormatValidityRangeTests
    {
        [Fact]
        public void 起止都有时输出时间段()
        {
            Assert.Equal("2026年8月14日～2027年4月30日",
                TicketTransferRules.FormatValidityRange(
                    new DateTime(2026, 8, 14, 11, 55, 0),
                    new DateTime(2027, 4, 30, 23, 59, 59)));
        }

        [Fact]
        public void 没有起始日时只输出到期日()
        {
            Assert.Equal("2027年4月30日",
                TicketTransferRules.FormatValidityRange(null, new DateTime(2027, 4, 30, 23, 59, 59)));
        }
    }

    // 领取上限的计数口径。用 Expression 而不是普通方法，是为了同一份条件既能给 EF 翻译成 SQL、
    // 又能在这里编译成委托对内存集合断言——避免"测的是一套、线上跑的是另一套"。
    public class UsableTicketFilterTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 14, 12, 0, 0);
        private const int Me = 15506;
        private const int Tpl = 12;

        private static Ticket Make(string code, int? memberId = Me, int templateId = Tpl,
            int valid = 1, int used = 0, int shared = 0, DateTime? expire = null)
        {
            return new Ticket
            {
                code = code,
                member_id = memberId,
                template_id = templateId,
                valid = valid,
                used = used,
                shared = shared,
                expire_date = expire
            };
        }

        private static List<string> Filter(params Ticket[] tickets)
        {
            var predicate = TicketTransferRules.UsableTicketFilter(Me, Tpl, Now).Compile();
            return tickets.Where(predicate).Select(t => t.code).ToList();
        }

        [Fact]
        public void 未使用且未过期的计入()
        {
            Assert.Equal(new[] { "a" }, Filter(Make("a", expire: Now.AddDays(30))));
        }

        [Fact]
        public void 没有到期日的视为未过期计入()
        {
            Assert.Equal(new[] { "a" }, Filter(Make("a", expire: null)));
        }

        [Fact]
        public void 分享中的券仍在自己名下计入()
        {
            Assert.Equal(new[] { "a" }, Filter(Make("a", shared: 1, expire: Now.AddDays(30))));
        }

        [Fact]
        public void 已核销的不计入()
        {
            Assert.Empty(Filter(Make("a", used: 1, expire: Now.AddDays(30))));
        }

        [Fact]
        public void 已失效的不计入()
        {
            Assert.Empty(Filter(Make("a", valid: 0, expire: Now.AddDays(30))));
        }

        [Fact]
        public void 已过期的不计入()
        {
            Assert.Empty(Filter(Make("a", expire: Now.AddDays(-1))));
        }

        [Fact]
        public void 别的模板不计入()
        {
            Assert.Empty(Filter(Make("a", templateId: 16, expire: Now.AddDays(30))));
        }

        [Fact]
        public void 别人名下的不计入()
        {
            Assert.Empty(Filter(Make("a", memberId: 11386, expire: Now.AddDays(30))));
        }

        [Fact]
        public void 混合集合只挑出可用的()
        {
            Assert.Equal(new[] { "keep1", "keep2" }, Filter(
                Make("keep1", expire: Now.AddDays(30)),
                Make("used", used: 1, expire: Now.AddDays(30)),
                Make("keep2", expire: null),
                Make("expired", expire: Now.AddDays(-1)),
                Make("otherTpl", templateId: 16),
                Make("otherMember", memberId: 11386)));
        }
    }
}

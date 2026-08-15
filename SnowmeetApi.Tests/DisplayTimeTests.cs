using System;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 券列表上那行时间该显示什么。
    // ⚠️ 不能用 ticket.accepted_time —— 它名字像"接受时间"，实际只在建券时赋值、
    // 转赠接受时不更新，生产库里 12164 张券它全都等于 create_date。真正的接受时间
    // 只在 ticket_log.transact_time 里。
    public class DisplayTimeTests
    {
        private static readonly DateTime Created = new DateTime(2026, 3, 1, 9, 0, 0);
        private static readonly DateTime Shared = new DateTime(2026, 8, 15, 16, 0, 0);
        private static readonly DateTime Used = new DateTime(2026, 8, 16, 10, 30, 0);
        private static readonly DateTime Accepted = new DateTime(2026, 8, 15, 17, 19, 10);

        private static Ticket T()
        {
            return new Ticket()
            {
                code = "x", create_date = Created, accepted_time = Created,
                shared_time = Shared, used_time = Used
            };
        }

        [Fact]
        public void 未使用且是转赠得来的显示领取时间()
        {
            var r = TicketTransferRules.ResolveDisplayTime(T(), TicketListContext.Unused, Accepted);
            Assert.Equal(Accepted, r.Time);
            Assert.Equal("领取时间", r.Label);
        }

        [Fact]
        public void 未使用且非转赠得来的显示获得时间()
        {
            var r = TicketTransferRules.ResolveDisplayTime(T(), TicketListContext.Unused, null);
            Assert.Equal(Created, r.Time);
            Assert.Equal("获得时间", r.Label);
        }

        [Fact]
        public void 已使用显示核销时间()
        {
            var r = TicketTransferRules.ResolveDisplayTime(T(), TicketListContext.Used, null);
            Assert.Equal(Used, r.Time);
            Assert.Equal("核销时间", r.Label);
        }

        [Fact]
        public void 已分享未被接受显示分享时间()
        {
            var r = TicketTransferRules.ResolveDisplayTime(T(), TicketListContext.SharedPending, null);
            Assert.Equal(Shared, r.Time);
            Assert.Equal("分享时间", r.Label);
        }

        [Fact]
        public void 已分享且对方已接受显示对方领取时间()
        {
            var r = TicketTransferRules.ResolveDisplayTime(T(), TicketListContext.SharedAccepted, Accepted);
            Assert.Equal(Accepted, r.Time);
            Assert.Equal("对方领取时间", r.Label);
        }

        [Fact]
        public void 已使用但没有核销时间时不显示这一行()
        {
            Ticket t = T();
            t.used_time = null;
            var r = TicketTransferRules.ResolveDisplayTime(t, TicketListContext.Used, null);
            Assert.Null(r.Time);
            Assert.Equal("", r.Text);
        }

        [Fact]
        public void 时间预格式化成字符串避免iOS解析问题()
        {
            var r = TicketTransferRules.ResolveDisplayTime(T(), TicketListContext.Used, null);
            // iOS 上 new Date('2026-08-16 10:30:00') 会得到 Invalid Date，
            // 所以后端直接下发格式化好的字符串，前端不做任何日期解析
            Assert.Equal("2026-08-16 10:30", r.Text);
        }
    }
}

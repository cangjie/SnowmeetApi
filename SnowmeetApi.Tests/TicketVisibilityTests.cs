using System;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 「我的优惠券 - 未使用」列表的过期可见性。
    // 历史上这里的比较符写反了（保留的是已过期的券），2026-08-14 修正。
    // 粒度按天：当天到期的券当天仍然可用、仍然要显示。
    public class TicketVisibilityTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 14, 12, 0, 0);

        private static bool NotExpired(DateTime? expire)
        {
            return TicketTransferRules.IsNotExpired(new Ticket() { expire_date = expire }, Now);
        }

        [Fact]
        public void 没有到期日的视为长期有效()
        {
            Assert.True(NotExpired(null));
        }

        [Fact]
        public void 到期日在未来的显示()
        {
            Assert.True(NotExpired(new DateTime(2027, 4, 30, 23, 59, 59)));
        }

        [Fact]
        public void 当天到期的当天仍然显示()
        {
            Assert.True(NotExpired(new DateTime(2026, 8, 14, 0, 0, 0)));
        }

        [Fact]
        public void 昨天到期的不再显示()
        {
            Assert.False(NotExpired(new DateTime(2026, 8, 13, 23, 59, 59)));
        }
    }
}

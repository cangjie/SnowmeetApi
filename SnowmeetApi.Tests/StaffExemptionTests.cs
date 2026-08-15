using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 领取上限对店员豁免：店员要测试/内部使用，不该被"名下同款券最多 3 张"卡住。
    // title_level 四档：100=店员 / 200=店长 / 300=系统管理员 / 1000=超管。
    public class StaffExemptionTests
    {
        [Fact]
        public void 不是店员时限制照常生效()
        {
            Assert.False(TicketTransferRules.IsExemptFromReceiveLimit(null));
        }

        [Fact]
        public void 店员豁免()
        {
            Assert.True(TicketTransferRules.IsExemptFromReceiveLimit(new Staff() { title_level = 100 }));
        }

        [Fact]
        public void 店长和超管一样豁免()
        {
            Assert.True(TicketTransferRules.IsExemptFromReceiveLimit(new Staff() { title_level = 200 }));
            Assert.True(TicketTransferRules.IsExemptFromReceiveLimit(new Staff() { title_level = 1000 }));
        }

        [Fact]
        public void 有员工记录但等级不够的不豁免()
        {
            Assert.False(TicketTransferRules.IsExemptFromReceiveLimit(new Staff() { title_level = 99 }));
        }
    }
}

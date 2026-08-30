using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class TicketShareRulesTests
    {
        private static TicketShareBatch B(string type = TicketShareBatch.SharePersonal,
            int? maxClaims = 1, int claimCount = 0, int valid = 1)
        {
            return new TicketShareBatch()
            {
                id = 1, template_id = 12, staff_id = 9,
                share_type = type, max_claims = maxClaims, claim_count = claimCount, valid = valid
            };
        }

        [Fact]
        public void 状态_撤回优先于领完()
        {
            // 撤回是人为终止，比"领完"更该被看见
            Assert.Equal("已撤回", TicketShareRules.DescribeBatchState(
                B(claimCount: 1, valid: 0)).Label);
        }

        [Fact]
        public void 状态_个人分享领了一张就是领完()
        {
            Assert.Equal("已领完", TicketShareRules.DescribeBatchState(B(claimCount: 1)).Label);
        }

        [Fact]
        public void 状态_群分享不限人数永远是分享中()
        {
            Assert.Equal("分享中", TicketShareRules.DescribeBatchState(
                B(type: TicketShareBatch.ShareGroup, maxClaims: null, claimCount: 99)).Label);
        }

        [Fact]
        public void 状态_空对象不抛异常()
        {
            Assert.Equal("", TicketShareRules.DescribeBatchState(null).Label);
        }

        [Theory]
        [InlineData("personal", "小程序卡片")]
        [InlineData("group", "海报")]
        [InlineData("qrcode", "海报")]
        [InlineData("", "小程序卡片")]
        [InlineData(null, "小程序卡片")]
        public void 分享方式文案_只分卡片与海报两类(string type, string expected)
        {
            Assert.Equal(expected, TicketShareRules.DescribeShareType(type));
        }

        [Fact]
        public void 进度_个人分享报分母()
        {
            Assert.Equal("已领 0 / 1 张", TicketShareRules.DescribeProgress(B()));
        }

        [Fact]
        public void 进度_群分享不限人数不报分母()
        {
            Assert.Equal("已领 5 张（不限人数，每人一张）", TicketShareRules.DescribeProgress(
                B(type: TicketShareBatch.ShareGroup, maxClaims: null, claimCount: 5)));
        }

        [Theory]
        [InlineData(1, 0, 1, true)]    // 个人，没人领
        [InlineData(1, 1, 1, false)]   // 个人，领完了
        [InlineData(1, 0, 0, false)]   // 已撤回
        [InlineData(null, 99, 1, true)] // 群，不限人数
        public void 能否领取(int? maxClaims, int claimCount, int valid, bool expected)
        {
            Assert.Equal(expected, TicketShareRules.IsClaimable(
                B(maxClaims: maxClaims, claimCount: claimCount, valid: valid)));
        }

        [Fact]
        public void 能否领取_空对象为假()
        {
            Assert.False(TicketShareRules.IsClaimable(null));
        }
    }
}

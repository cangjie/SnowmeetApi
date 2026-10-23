using SnowmeetApi.Helpers;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class PayMethodRulesTests
    {
        [Theory]
        [InlineData("苍杰微信转账")]
        [InlineData("王政微信转账")]
        [InlineData("肖志强工作微信转账")]
        [InlineData("微信转账")]
        [InlineData(" 苍杰微信转账 ")]
        public void 以微信转账结尾的是个人微信转账(string payMethod)
        {
            Assert.True(PayMethodRules.IsPersonalWechatTransfer(payMethod));
        }

        [Theory]
        [InlineData("微信支付")]
        [InlineData("支付宝转账")]
        [InlineData("苍杰支付宝")]
        [InlineData("银行转账")]
        [InlineData("现金")]
        [InlineData("")]
        [InlineData(null)]
        public void 其他支付方式不受影响(string? payMethod)
        {
            Assert.False(PayMethodRules.IsPersonalWechatTransfer(payMethod));
        }
    }
}

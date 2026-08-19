using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class CareProductRulesTests
    {
        private static Product P(int valid = 1, int hidden = 0)
        {
            return new Product() { id = 1, name = "双项", sale_price = 230, valid = valid, hidden = hidden };
        }

        [Fact]
        public void 状态_停用优先于隐藏()
        {
            Assert.Equal("已停用", CareProductRules.DescribeState(P(valid: 0, hidden: 1)).Label);
        }

        [Fact]
        public void 状态_隐藏()
        {
            Assert.Equal("已隐藏", CareProductRules.DescribeState(P(hidden: 1)).Label);
        }

        [Fact]
        public void 状态_在售()
        {
            Assert.Equal("在售", CareProductRules.DescribeState(P()).Label);
        }

        [Fact]
        public void 状态_空对象不抛异常()
        {
            Assert.Equal("", CareProductRules.DescribeState(null).Label);
        }

        [Theory]
        [InlineData(null, 1, 230.0, "请填写商品名称")]
        [InlineData("  ", 1, 230.0, "请填写商品名称")]
        [InlineData("双项", null, 230.0, "请选择门店")]
        [InlineData("双项", 0, 230.0, "请选择门店")]
        [InlineData("双项", 1, null, "请填写价格")]
        [InlineData("双项", 1, -1.0, "价格不能为负")]
        public void 校验_逐项拦截(string name, int? shopId, double? price, string expected)
        {
            Assert.Equal(expected, CareProductRules.Validate(name, shopId, price));
        }

        [Fact]
        public void 校验_零元通过()
        {
            // 「免费打蜡升级」这类 0 元项确实存在，不能一刀切拒掉
            Assert.Null(CareProductRules.Validate("免费打蜡升级", 1, 0));
        }

        [Fact]
        public void 校验_正常通过()
        {
            Assert.Null(CareProductRules.Validate("双项", 1, 230));
        }

        [Theory]
        [InlineData("双项", true)]
        [InlineData("单项", true)]
        [InlineData("双项加急", true)]
        [InlineData("单项加急", true)]
        [InlineData(" 双项 ", true)]
        [InlineData("非雪季养护", false)]
        [InlineData("免费打蜡升级", false)]
        [InlineData("万龙修刃打蜡次日取", false)]
        [InlineData(null, false)]
        public void 定价可识别名_与CarePricingRules同一套(string name, bool expected)
        {
            Assert.Equal(expected, CareProductRules.IsPricingRecognizedName(name));
        }
    }
}

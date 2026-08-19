using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 养护服务费的「care 服务组合 → 商品」映射。
    ///
    /// 2026-08-19 之前是靠**商品名关键词**猜的（IndexOf("修刃打蜡") / IndexOf("立等") 那一串）。
    /// 商品目录改名成 双项/单项/双项加急/单项加急 之后那套关键词一个都匹配不上，
    /// 会让所有非次卡养护单的服务费静默变成 0。
    ///
    /// 新口径由用户 2026-08-19 拍板：**修刃与热打蜡各算一项，机打蜡和刮蜡不计费**。
    /// 下面的期望值全部取自生产库近两个雪季的实付价格（万龙服务中心 4400+ 单）。
    /// </summary>
    public class CarePricingRulesTests
    {
        private static Care C(int edge = 0, int wax = 0, int freeWax = 0, int unwax = 0,
            int urgent = 0, string bizType = null, string summer = null)
        {
            return new Care()
            {
                need_edge = edge, need_wax = wax, free_wax = freeWax,
                need_unwax = unwax, urgent = urgent, biz_type = bizType, summer = summer
            };
        }

        private static Product P(int id, string name, double price)
        {
            return new Product() { id = id, name = name, sale_price = price, valid = 1 };
        }

        // 万龙服务中心的在售商品（142/143 已 valid=0，不进列表）
        private static List<Product> Wanlong()
        {
            return new List<Product>()
            {
                P(139, "双项", 230), P(140, "单项", 170),
                P(137, "双项加急", 280), P(138, "单项加急", 220),
                P(715, "非雪季养护", 330)
            };
        }

        // 南山 / 崇礼旗舰店只配了双项和单项，没有加急
        private static List<Product> Nanshan()
        {
            return new List<Product>() { P(677, "双项", 200), P(678, "单项", 100) };
        }

        // ── 计费项数 ───────────────────────────────────────────────────────

        [Theory]
        [InlineData(1, 1, 0, 0, 2)]   // 修刃+热蜡           生产 1528 单 → 230（双项）
        [InlineData(1, 1, 1, 0, 2)]   // 修刃+热蜡+机蜡      生产  344 单 → 230，机蜡没加钱
        [InlineData(1, 1, 0, 1, 2)]   // 修刃+热蜡+刮蜡      生产  376 单，刮蜡不计费
        [InlineData(0, 1, 0, 0, 1)]   // 只热蜡              生产 1069 单 → 170（单项）
        [InlineData(0, 1, 1, 0, 1)]   // 热蜡+机蜡           生产  237 单 → 170
        [InlineData(1, 0, 0, 0, 1)]   // 只修刃              生产  739 单 → 170
        [InlineData(1, 0, 1, 0, 1)]   // 修刃+机蜡           生产    6 单
        [InlineData(0, 0, 1, 0, 0)]   // 只机蜡              生产   11 单 → 0 元
        [InlineData(0, 0, 0, 1, 0)]   // 只刮蜡              生产    6 单 → 0 元
        [InlineData(0, 0, 1, 1, 0)]   // 机蜡+刮蜡           两项都不计费
        [InlineData(0, 0, 0, 0, 0)]   // 什么都不做
        public void 计费项数_只数修刃与热打蜡(int edge, int wax, int freeWax, int unwax, int expected)
        {
            Assert.Equal(expected, CarePricingRules.CountChargeableItems(
                C(edge: edge, wax: wax, freeWax: freeWax, unwax: unwax)));
        }

        [Fact]
        public void 计费项数_care为空不抛异常()
        {
            Assert.Equal(0, CarePricingRules.CountChargeableItems(null));
        }

        // ── 商品名派生 ─────────────────────────────────────────────────────

        [Theory]
        [InlineData(1, 1, 0, "双项")]
        [InlineData(1, 1, 1, "双项加急")]
        [InlineData(0, 1, 0, "单项")]
        [InlineData(0, 1, 1, "单项加急")]
        [InlineData(1, 0, 0, "单项")]
        [InlineData(1, 0, 1, "单项加急")]
        [InlineData(0, 0, 0, "")]
        [InlineData(0, 0, 1, "")]     // 没有计费项时，加急也不产生商品
        public void 商品名_按项数加加急后缀(int edge, int wax, int urgent, string expected)
        {
            Assert.Equal(expected, CarePricingRules.ResolveProductName(
                C(edge: edge, wax: wax, urgent: urgent)));
        }

        // ── 非雪季两种走法 ────────────────────────────────────────────────
        // CareController.ApplyDefaultServices：券17 → summer="now" 且服务项清零；
        //                                      券18 → summer="later" 且打开修刃+热蜡。
        // 两者 biz_type 都是「非雪季养护」，只看 biz_type 会把 later 也误判成整包。

        [Fact]
        public void 非雪季_直接寄存走非雪季养护商品()
        {
            Assert.Equal("非雪季养护", CarePricingRules.ResolveProductName(
                C(summer: "now", bizType: "非雪季养护")));
        }

        [Fact]
        public void 非雪季_先双项晚点寄存按双项算()
        {
            // 券18 会把修刃+热蜡打开，这次做的就是双项，寄存留到下次凭券17 来
            Assert.Equal("双项", CarePricingRules.ResolveProductName(
                C(edge: 1, wax: 1, unwax: 1, summer: "later", bizType: "非雪季养护")));
        }

        [Fact]
        public void 非雪季_只有bizType且无服务项时仍按整包()
        {
            // 老数据有 biz_type 已写、summer 还没落的情况
            Assert.Equal("非雪季养护",
                CarePricingRules.ResolveProductName(C(bizType: "非雪季养护")));
        }

        [Fact]
        public void 非雪季_bizType配着服务项时按服务项算()
        {
            Assert.Equal("双项",
                CarePricingRules.ResolveProductName(C(edge: 1, wax: 1, bizType: "非雪季养护")));
        }

        [Fact]
        public void 非雪季_匹配到715真商品而不是假商品()
        {
            // 关键回归：只有匹配到真商品，模板17 配在 715 上的一口价 0 才能生效
            Product p = CarePricingRules.MatchProduct(Wanlong(), C(summer: "now", bizType: "非雪季养护"));
            Assert.NotNull(p);
            Assert.Equal(715, p.id);
            Assert.Equal(330, p.sale_price);
        }

        // ── 商品匹配 ───────────────────────────────────────────────────────

        [Fact]
        public void 匹配_双项命中230()
        {
            Product p = CarePricingRules.MatchProduct(Wanlong(), C(edge: 1, wax: 1));
            Assert.Equal(139, p.id);
            Assert.Equal(230, p.sale_price);
        }

        [Fact]
        public void 匹配_机打蜡不影响双项判定()
        {
            // 生产 344 单「修刃+热蜡+机蜡」实付 230，与不带机蜡的 1528 单同价
            Assert.Equal(230, CarePricingRules.MatchProduct(
                Wanlong(), C(edge: 1, wax: 1, freeWax: 1)).sale_price);
        }

        [Fact]
        public void 匹配_加急走加急商品()
        {
            Product p = CarePricingRules.MatchProduct(Wanlong(), C(edge: 1, wax: 1, urgent: 1));
            Assert.Equal(137, p.id);
            Assert.Equal(280, p.sale_price);
        }

        [Fact]
        public void 匹配_店里没配加急商品时回退非加急价()
        {
            // 南山/崇礼只有双项和单项。用户 2026-08-19 拍板：回退，不能静默变 0 元
            Product p = CarePricingRules.MatchProduct(Nanshan(), C(edge: 1, wax: 1, urgent: 1));
            Assert.Equal(677, p.id);
            Assert.Equal(200, p.sale_price);
        }

        [Fact]
        public void 匹配_单项加急回退()
        {
            Assert.Equal(678, CarePricingRules.MatchProduct(
                Nanshan(), C(wax: 1, urgent: 1)).id);
        }

        [Theory]
        [InlineData(0, 0, 1, 0)]   // 只机蜡
        [InlineData(0, 0, 0, 1)]   // 只刮蜡
        [InlineData(0, 0, 0, 0)]   // 什么都不做
        public void 匹配_没有计费项时返回null(int edge, int wax, int freeWax, int unwax)
        {
            Assert.Null(CarePricingRules.MatchProduct(
                Wanlong(), C(edge: edge, wax: wax, freeWax: freeWax, unwax: unwax)));
        }

        [Fact]
        public void 匹配_非雪季养护命中330()
        {
            Assert.Equal(715, CarePricingRules.MatchProduct(
                Wanlong(), C(summer: "now", bizType: "非雪季养护")).id);
        }

        [Fact]
        public void 匹配_商品名两侧空格不影响命中()
        {
            List<Product> ps = new List<Product>() { P(1, " 双项 ", 230) };
            Assert.Equal(1, CarePricingRules.MatchProduct(ps, C(edge: 1, wax: 1)).id);
        }

        [Fact]
        public void 匹配_列表为空或为null不抛异常()
        {
            Assert.Null(CarePricingRules.MatchProduct(null, C(edge: 1, wax: 1)));
            Assert.Null(CarePricingRules.MatchProduct(new List<Product>(), C(edge: 1, wax: 1)));
        }

        [Fact]
        public void 匹配_该店没配对应商品时返回null()
        {
            // 只配了单项的店接到双项单：宁可返回 null（上层算 0 并可被发现），
            // 也不能拿单项价冒充双项价去收钱
            List<Product> only = new List<Product>() { P(140, "单项", 170) };
            Assert.Null(CarePricingRules.MatchProduct(only, C(edge: 1, wax: 1)));
        }
        // ── 老规则兜底 ─────────────────────────────────────────────────────
        // 表里没配规则时回退到 2026-08-18 之前写死在 CareController 里的那套：
        // 老顾客券（模板16）双项减 30、单项减 20，不看门店。

        [Theory]
        [InlineData(1, 1, 30.0)]   // 双项
        [InlineData(1, 0, 20.0)]   // 只修刃
        [InlineData(0, 1, 20.0)]   // 只热蜡
        [InlineData(0, 0, 0.0)]    // 无计费项
        public void 老规则兜底_券16按项数减(int edge, int wax, double expected)
        {
            Assert.Equal(expected, CarePricingRules.LegacyTicketDiscount(
                16, C(edge: edge, wax: wax), 230));
        }

        [Fact]
        public void 老规则兜底_机打蜡与刮蜡不影响项数()
        {
            // 老代码判的是 need_edge/need_wax，与新口径一致，机蜡刮蜡都不参与
            Assert.Equal(30, CarePricingRules.LegacyTicketDiscount(
                16, C(edge: 1, wax: 1, freeWax: 1, unwax: 1), 230));
        }

        [Theory]
        [InlineData(12)]
        [InlineData(17)]
        [InlineData(18)]
        [InlineData(0)]
        public void 老规则兜底_只对券16生效(int templateId)
        {
            Assert.Equal(0, CarePricingRules.LegacyTicketDiscount(
                templateId, C(edge: 1, wax: 1), 230));
        }

        [Fact]
        public void 老规则兜底_减免不超过原价()
        {
            Assert.Equal(10, CarePricingRules.LegacyTicketDiscount(16, C(edge: 1, wax: 1), 10));
            Assert.Equal(0, CarePricingRules.LegacyTicketDiscount(16, C(edge: 1, wax: 1), 0));
        }

        [Fact]
        public void 老规则兜底_care为空不抛异常()
        {
            Assert.Equal(0, CarePricingRules.LegacyTicketDiscount(16, null, 230));
        }
    }
}

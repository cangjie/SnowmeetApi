using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 优惠券模板三个「死字段」被接活时的口径回归。
    ///
    /// 2026-08-18 之前 available_days / discount_rate / discount_amount 全库零读取方，
    /// 发券只抄 template.expire_date、养护定价只读 fixed_price + 一处 template_id==16 硬编码。
    /// 这组测试就是那次替换的验收线：**替换后必须与替换前逐项等价**。
    /// </summary>
    public class TicketTemplateRulesTests
    {
        private static TicketTemplate Tpl(int? availableDays = null, DateTime? expire = null,
            string name = "养护券", string type = "养护券", string bizType = "养护")
        {
            return new TicketTemplate()
            {
                id = 99, name = name, type = type, memo = "备注", biz_type = bizType,
                miniapp_recept_path = "", available_days = availableDays, expire_date = expire
            };
        }

        private static ProductTicketTemplate Rule(int productId = 1, double? fixedPrice = null,
            double? rate = null, double? amount = null)
        {
            return new ProductTicketTemplate()
            {
                product_id = productId, ticket_template_id = 99, valid = true,
                fixed_price = fixedPrice, discount_rate = rate, discount_amount = amount
            };
        }

        // ── ValidateTemplate ───────────────────────────────────────────────

        [Fact]
        public void Validate_两个有效期字段同时非空_被拒()
        {
            List<string> errors = TicketTemplateRules.ValidateTemplate(
                Tpl(availableDays: 6, expire: new DateTime(2024, 12, 7)));
            Assert.Contains(errors, e => e.Contains("可用天数") && e.Contains("总过期日"));
        }

        [Fact]
        public void Validate_只设可用天数_通过()
        {
            Assert.Empty(TicketTemplateRules.ValidateTemplate(Tpl(availableDays: 200)));
        }

        [Fact]
        public void Validate_只设总过期日_通过()
        {
            Assert.Empty(TicketTemplateRules.ValidateTemplate(Tpl(expire: new DateTime(2035, 12, 31))));
        }

        [Fact]
        public void Validate_两者都为空_通过_语义是永久有效()
        {
            Assert.Empty(TicketTemplateRules.ValidateTemplate(Tpl()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_可用天数非正数_被拒(int days)
        {
            // DB 迁移前用 0 表示"未设置"，迁移后 0 是非法值——留着会静音地把券变成当天过期
            Assert.Contains(TicketTemplateRules.ValidateTemplate(Tpl(availableDays: days)),
                e => e.Contains("可用天数"));
        }

        [Fact]
        public void Validate_名称或类型为空_被拒()
        {
            TicketTemplate t = Tpl(availableDays: 30);
            t.name = "  ";
            t.type = null;
            List<string> errors = TicketTemplateRules.ValidateTemplate(t);
            Assert.Contains(errors, e => e.Contains("名称"));
            Assert.Contains(errors, e => e.Contains("类型"));
        }

        [Fact]
        public void Validate_业务类型为空_被拒()
        {
            // 取值域只有 零售/养护/租赁/餐饮 四个，没有"不参与开单"这一档，所以必须选一个
            Assert.Contains(TicketTemplateRules.ValidateTemplate(Tpl(availableDays: 30, bizType: null)),
                e => e.Contains("业务类型"));
            Assert.Contains(TicketTemplateRules.ValidateTemplate(Tpl(availableDays: 30, bizType: " ")),
                e => e.Contains("业务类型"));
        }

        [Fact]
        public void Validate_业务类型不在取值域_被拒()
        {
            Assert.Contains(TicketTemplateRules.ValidateTemplate(Tpl(availableDays: 30, bizType: "雪票")),
                e => e.Contains("业务类型"));
        }

        [Theory]
        [InlineData("零售")]
        [InlineData("养护")]
        [InlineData("租赁")]
        [InlineData("餐饮")]
        public void Validate_四个业务类型都通过(string bizType)
        {
            // 与 [order].type 对齐：生产库订单类型就是这四个（外加雪票/聚合两个不发券的）
            Assert.Empty(TicketTemplateRules.ValidateTemplate(Tpl(availableDays: 30, bizType: bizType)));
        }

        [Fact]
        public void Validate_模板为空_只报一条_不抛异常()
        {
            Assert.Single(TicketTemplateRules.ValidateTemplate(null));
        }

        // ── ResolveTicketExpireDate ────────────────────────────────────────

        [Fact]
        public void Expire_模板设了固定截止日_原样返回()
        {
            DateTime expire = new DateTime(2035, 12, 31);
            Assert.Equal(expire, TicketTemplateRules.ResolveTicketExpireDate(
                Tpl(expire: expire), new DateTime(2026, 8, 18, 10, 30, 0)));
        }

        [Fact]
        public void Expire_按可用天数_算到启用日加N天当天的最后一秒()
        {
            // available_days=6，2026-08-18 启用 → 8/24 23:59:59 失效（第 7 天零点起过期）
            Assert.Equal(new DateTime(2026, 8, 24, 23, 59, 59),
                TicketTemplateRules.ResolveTicketExpireDate(
                    Tpl(availableDays: 6), new DateTime(2026, 8, 18, 10, 30, 0)));
        }

        [Fact]
        public void Expire_按可用天数_启用日的时分秒被抹掉()
        {
            // 早上 0:01 领的券和晚上 23:59 领的券必须同一天到期，不然同一天发的券有效期差一天
            DateTime early = TicketTemplateRules.ResolveTicketExpireDate(
                Tpl(availableDays: 30), new DateTime(2026, 8, 18, 0, 1, 0));
            DateTime late = TicketTemplateRules.ResolveTicketExpireDate(
                Tpl(availableDays: 30), new DateTime(2026, 8, 18, 23, 59, 0));
            Assert.Equal(early, late);
        }

        [Fact]
        public void Expire_两者都为空_返回MaxValue_维持改造前口径()
        {
            Assert.Equal(DateTime.MaxValue,
                TicketTemplateRules.ResolveTicketExpireDate(Tpl(), new DateTime(2026, 8, 18)));
        }

        [Fact]
        public void Expire_模板为空_返回MaxValue_不抛异常()
        {
            Assert.Equal(DateTime.MaxValue,
                TicketTemplateRules.ResolveTicketExpireDate(null, new DateTime(2026, 8, 18)));
        }

        [Fact]
        public void Expire_两者都设时_固定截止日优先()
        {
            // ValidateTemplate 会拦下这种模板，但存量数据里有 5 个，发券路径不能因此炸
            DateTime expire = new DateTime(2035, 12, 31);
            Assert.Equal(expire, TicketTemplateRules.ResolveTicketExpireDate(
                Tpl(availableDays: 6, expire: expire), new DateTime(2026, 8, 18)));
        }

        [Fact]
        public void Expire_结果跨过雪季末也不截断()
        {
            // available_days=2000（模板 16/17/18 的实际值）≈ 5.5 年，不该被任何雪季逻辑改写
            Assert.Equal(new DateTime(2032, 2, 8, 23, 59, 59),
                TicketTemplateRules.ResolveTicketExpireDate(
                    Tpl(availableDays: 2000), new DateTime(2026, 8, 18)));
        }

        // ── ResolveProductDiscount ─────────────────────────────────────────

        [Fact]
        public void Discount_一口价_直接定价且不产生减免()
        {
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(fixedPrice: 120), 230);
            Assert.Equal(120, charge);
            Assert.Equal(0, discount);
        }

        [Fact]
        public void Discount_模板12的脏数据_一口价仍然赢()
        {
            // product_ticket_template id 1/2/3：fixed_price=120/80/50 且 discount_amount=1.0。
            // 改造前只读 fixed_price，改造后优先级必须保证结论不变
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(fixedPrice: 120, amount: 1.0), 230);
            Assert.Equal(120, charge);
            Assert.Equal(0, discount);
        }

        [Theory]
        [InlineData(230, 30)]   // 139 万龙修刃打蜡（双项）
        [InlineData(170, 20)]   // 140 万龙修刃 / 143 万龙打蜡，同价同减
        public void Discount_立减金额_等价于券16原来的硬编码(double basePrice, double amount)
        {
            // 改造前：CareController 里 if (ticket.template_id == 16) 双项减 30 / 单项减 20
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(amount: amount), basePrice);
            Assert.Equal(basePrice, charge);
            Assert.Equal(amount, discount);
        }

        [Fact]
        public void Discount_立减金额超过原价_减到零不倒找钱()
        {
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(amount: 500), 170);
            Assert.Equal(170, charge);
            Assert.Equal(170, discount);
        }

        [Fact]
        public void Discount_折扣率_八折等于减两成()
        {
            // 200 * (1 - 0.8) 的浮点结果是 39.999999999999993，算钱必须收到分
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(rate: 0.8), 200);
            Assert.Equal(200, charge);
            Assert.Equal(40, discount);
        }

        [Fact]
        public void Discount_折扣率_减免额收到分()
        {
            var (_, discount) = TicketTemplateRules.ResolveProductDiscount(Rule(rate: 0.85), 170);
            Assert.Equal(25.5, discount);
        }

        [Fact]
        public void Discount_折扣率优先于立减金额()
        {
            var (_, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(rate: 0.5, amount: 10), 200);
            Assert.Equal(100, discount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1.5)]
        [InlineData(-0.2)]
        public void Discount_折扣率越界_按不打折处理(double rate)
        {
            // 0 折（免费）请用一口价 0 表达；>1 或负数是录错，宁可不减也不能算出负价
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(rate: rate), 200);
            Assert.Equal(200, charge);
            Assert.Equal(0, discount);
        }

        [Fact]
        public void Discount_规则为空_原价无减免()
        {
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(null, 230);
            Assert.Equal(230, charge);
            Assert.Equal(0, discount);
        }

        [Fact]
        public void Discount_三个字段全空_原价无减免()
        {
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(Rule(), 230);
            Assert.Equal(230, charge);
            Assert.Equal(0, discount);
        }

        [Fact]
        public void Discount_一口价为零_是免费而不是未设置()
        {
            // 模板 17/18（非雪季）配的就是 fixed_price = 0
            var (charge, discount) = TicketTemplateRules.ResolveProductDiscount(
                Rule(fixedPrice: 0), 330);
            Assert.Equal(0, charge);
            Assert.Equal(0, discount);
        }

        // ── MatchProductRule ───────────────────────────────────────────────

        [Fact]
        public void Match_按商品ID精确命中()
        {
            List<ProductTicketTemplate> rules = new List<ProductTicketTemplate>()
            { Rule(139, fixedPrice: 120), Rule(140, fixedPrice: 80) };
            Assert.Equal(80, TicketTemplateRules.MatchProductRule(rules, 140).fixed_price);
        }

        [Fact]
        public void Match_product_id为0是通配兜底()
        {
            // CareController 原来就是 p.product_id == product.id || p.product_id == 0，语义保留
            List<ProductTicketTemplate> rules = new List<ProductTicketTemplate>() { Rule(0, fixedPrice: 50) };
            Assert.Equal(50, TicketTemplateRules.MatchProductRule(rules, 999).fixed_price);
        }

        [Fact]
        public void Match_精确命中优先于通配兜底()
        {
            List<ProductTicketTemplate> rules = new List<ProductTicketTemplate>()
            { Rule(0, fixedPrice: 50), Rule(139, fixedPrice: 120) };
            Assert.Equal(120, TicketTemplateRules.MatchProductRule(rules, 139).fixed_price);
        }

        [Fact]
        public void Match_跳过已软删的规则()
        {
            ProductTicketTemplate dead = Rule(139, fixedPrice: 120);
            dead.valid = false;
            Assert.Null(TicketTemplateRules.MatchProductRule(new List<ProductTicketTemplate>() { dead }, 139));
        }

        [Fact]
        public void Match_列表为空或为null_返回null()
        {
            Assert.Null(TicketTemplateRules.MatchProductRule(null, 139));
            Assert.Null(TicketTemplateRules.MatchProductRule(new List<ProductTicketTemplate>(), 139));
        }

        // ── DescribeValidity（列表页展示口径）─────────────────────────────

        [Fact]
        public void Describe_三种有效期口径各自成句()
        {
            Assert.Equal("启用后 200 天", TicketTemplateRules.DescribeValidity(Tpl(availableDays: 200)));
            Assert.Equal("2035-12-31 截止",
                TicketTemplateRules.DescribeValidity(Tpl(expire: new DateTime(2035, 12, 31))));
            Assert.Equal("永久有效", TicketTemplateRules.DescribeValidity(Tpl()));
        }

        [Fact]
        public void Describe_两者都设时明确标出冲突()
        {
            // 存量 5 个模板是这个状态，列表上必须一眼看出来该去修
            Assert.Contains("冲突", TicketTemplateRules.DescribeValidity(
                Tpl(availableDays: 6, expire: new DateTime(2024, 12, 7))));
        }
    }
}

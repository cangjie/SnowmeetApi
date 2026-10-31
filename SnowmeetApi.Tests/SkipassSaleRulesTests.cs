using System;
using System.Collections.Generic;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 万龙雪票售卖规则（2026-10-29 用户拍板）：
    /// 价格同万龙自我游门市价、不再加价；只有店员能订票，顾客只能看价格，且看不到进货价。
    /// </summary>
    public class SkipassSaleRulesTests
    {
        // ---------- 谁能订票 ----------

        [Fact]
        public void 会话没有对应店员_不能订票()
        {
            Assert.False(SkipassSaleRules.CanReserve(null));
        }

        [Fact]
        public void 在职店员_能订票()
        {
            Assert.True(SkipassSaleRules.CanReserve(new Staff { id = 28, valid = 1 }));
        }

        [Fact]
        public void 已停用店员_不能订票()
        {
            Assert.False(SkipassSaleRules.CanReserve(new Staff { id = 28, valid = 0 }));
        }

        [Fact]
        public void 只有店员看得到进货价()
        {
            Assert.False(SkipassSaleRules.CanSeeCost(null));
            Assert.False(SkipassSaleRules.CanSeeCost(new Staff { id = 1, valid = 0 }));
            Assert.True(SkipassSaleRules.CanSeeCost(new Staff { id = 1, valid = 1 }));
        }

        // ---------- 成交价 ----------

        [Fact]
        public void 成交价取门市价_不再加价()
        {
            // 去年同一票种：结算价 299、门市价 579，成交价曾是 295
            Assert.Equal(579, SkipassSaleRules.ResolveDealPrice(579, 299, 20));
        }

        [Fact]
        public void 门市价为0或缺失_回退结算价加价()
        {
            Assert.Equal(319, SkipassSaleRules.ResolveDealPrice(0, 299, 20));
            Assert.Equal(319, SkipassSaleRules.ResolveDealPrice(null, 299, 20));
            Assert.Equal(319, SkipassSaleRules.ResolveDealPrice(-1, 299, 20));
        }

        // ---------- 价格行要不要更新 ----------

        static SkipassDailyPrice Row(double settle, double sale, double market)
        {
            return new SkipassDailyPrice { settlementPrice = settle, salePrice = sale, marketPrice = market };
        }

        [Fact]
        public void 三个价都没变_不更新()
        {
            Assert.False(SkipassSaleRules.PriceChanged(Row(299, 299, 579), 299, 299, 579));
        }

        [Fact]
        public void 只有门市价变了_也要更新()
        {
            // 旧逻辑只比结算价，这种情况会一直用旧门市价
            Assert.True(SkipassSaleRules.PriceChanged(Row(299, 299, 579), 299, 299, 599));
        }

        [Fact]
        public void 只有建议售价或结算价变了_也要更新()
        {
            Assert.True(SkipassSaleRules.PriceChanged(Row(299, 299, 579), 299, 309, 579));
            Assert.True(SkipassSaleRules.PriceChanged(Row(299, 299, 579), 289, 299, 579));
        }

        [Fact]
        public void 小数精度误差_不算变化()
        {
            Assert.False(SkipassSaleRules.PriceChanged(Row(371.07, 399, 399), 371.07000000001, 399, 399));
        }

        // ---------- 顾客看不到进货价 ----------

        [Fact]
        public void 脱敏去掉结算价和建议售价_保留成交价和门市价()
        {
            var rows = new List<SkipassDailyPrice>
            {
                new SkipassDailyPrice { settlementPrice = 299, salePrice = 299, marketPrice = 579, deal_price = 579 },
                new SkipassDailyPrice { settlementPrice = 349, salePrice = 349, marketPrice = 659, deal_price = 659 },
            };
            SkipassSaleRules.RedactDailyPrices(rows);
            foreach (var r in rows)
            {
                Assert.Equal(0, r.settlementPrice);
                Assert.Equal(0, r.salePrice);
            }
            Assert.Equal(579, rows[0].marketPrice);
            Assert.Equal(579, rows[0].deal_price);
            Assert.Equal(659, rows[1].deal_price);
        }

        [Fact]
        public void 脱敏接受空列表()
        {
            SkipassSaleRules.RedactDailyPrices(null);
            SkipassSaleRules.RedactDailyPrices(new List<SkipassDailyPrice>());
        }

        // ---------- 大好河山账号里哪些是万龙的雪票/次卡 ----------

        [Theory]
        [InlineData("【万龙】全雪区日场1天雪票-11.6")]
        [InlineData("【万龙】全雪区日场4小时雪票(含全套装备)-11.6")]
        [InlineData("【万龙】全雪区下午场+夜场滑雪票-11.6")]
        [InlineData("【万龙预售2026-27】夜场3次卡(不含雪具)限平日使用")]
        [InlineData("【万龙预售2026-27】11月日场3次卡(不含雪具)周末使用")]
        public void 万龙滑雪场的雪票和次卡_收(string name)
        {
            Assert.True(SkipassSaleRules.IsWanlongLiftProduct(name, "【崇礼】万龙滑雪场"));
        }

        [Theory]
        [InlineData("【万龙&常规】盛龙公寓-1住1滑-简约景观标1晚+1张日场雪票（自带板）", "张家口万龙滑雪场盛龙公寓")]
        [InlineData("【万龙&常规】国际公寓-双人1住1滑-豪华标间（含双早+2张日场雪票+温泉）（自带板）", "张家口万龙滑雪场国际酒店公寓")]
        [InlineData("【万龙&常规】龙宫酒店-双人1住1滑-低层大床房（含双早+2张日场雪票+温泉）（自带板）", "张家口万龙滑雪场龙宫酒店")]
        [InlineData("【万龙&常规】双龙酒店-双人1住1滑-雪景标间（含双早+2张日场雪票+温泉）（自带板）", "张家口万龙滑雪场双龙酒店")]
        public void 万龙的酒店套餐_不收(string name, string viewName)
        {
            Assert.False(SkipassSaleRules.IsWanlongLiftProduct(name, viewName));
        }

        [Theory]
        [InlineData("全季卡（日场26-27）", "【崇礼】翠云山森林风景区")]
        [InlineData("【常规&云顶】云顶大酒店-双人1住1滑-园景大床房（自带板）12.4—2.27", "张家口云顶大酒店")]
        [InlineData("【万龙】全雪区日场1天雪票-11.6", null)]
        public void 别的雪场或没有景区信息_不收(string name, string? viewName)
        {
            Assert.False(SkipassSaleRules.IsWanlongLiftProduct(name, viewName));
        }

        [Fact]
        public void 景区是万龙但名字带住或和号_也不收()
        {
            Assert.False(SkipassSaleRules.IsWanlongLiftProduct("【万龙】1住1滑套餐", "【崇礼】万龙滑雪场"));
            Assert.False(SkipassSaleRules.IsWanlongLiftProduct("【万龙&常规】雪票", "【崇礼】万龙滑雪场"));
            Assert.False(SkipassSaleRules.IsWanlongLiftProduct("【万龙＆常规】雪票", "【崇礼】万龙滑雪场"));
        }

        [Fact]
        public void 可卖的来源是万龙自我游和大好河山()
        {
            Assert.Equal(new[] { "万龙自我游", "大好河山" }, SkipassSaleRules.SellableSources);
        }

        // ---------- 按月查价格 ----------

        [Fact]
        public void 月初序列_第一个是起始日本身_之后每月1号()
        {
            var months = SkipassSaleRules.MonthStarts(new DateTime(2026, 10, 29, 15, 30, 0), 4);
            Assert.Equal(new[]
            {
                new DateTime(2026, 10, 29), new DateTime(2026, 11, 1), new DateTime(2026, 12, 1), new DateTime(2027, 1, 1)
            }, months);
        }

        [Fact]
        public void 月初序列_跨年和1号起始()
        {
            var months = SkipassSaleRules.MonthStarts(new DateTime(2026, 12, 1), 3);
            Assert.Equal(new[] { new DateTime(2026, 12, 1), new DateTime(2027, 1, 1), new DateTime(2027, 2, 1) }, months);
            Assert.Single(SkipassSaleRules.MonthStarts(new DateTime(2026, 12, 5), 1));
            Assert.Empty(SkipassSaleRules.MonthStarts(new DateTime(2026, 12, 5), 0));
        }

        // ---------- 新商品的雪季起止日 ----------

        [Theory]
        [InlineData("【26-27雪季】1天日场自带板滑雪票", 2026, 2027)]
        [InlineData("【万龙预售2026-27】11月日场3次卡(不含雪具)平日使用", 2026, 2027)]
        [InlineData("【25-26雪季节日专享】1.5天含雪具套票", 2025, 2026)]
        public void 商品名里有雪季_按它算(string name, int startYear, int endYear)
        {
            var (start, end) = SkipassSaleRules.ResolveSeasonDates(name, new DateTime(2026, 10, 29));
            Assert.Equal(new DateTime(startYear, 10, 1), start);
            Assert.Equal(new DateTime(endYear, 6, 1), end);
        }

        [Fact]
        public void 日期区间不是连续年份_不会误判成雪季()
        {
            // 「10.30-11.4」「12.4—2.27」都不是雪季
            var (start, end) = SkipassSaleRules.ResolveSeasonDates("尝鲜周 10.30-11.4 套餐 30-11", new DateTime(2026, 10, 29));
            Assert.Equal(new DateTime(2026, 10, 1), start);
            Assert.Equal(new DateTime(2027, 6, 1), end);
        }

        [Theory]
        [InlineData(2026, 10, 29, 2026)]
        [InlineData(2026, 7, 1, 2026)]
        [InlineData(2027, 2, 1, 2026)]
        [InlineData(2026, 6, 30, 2025)]
        public void 商品名没写雪季_按今天所在雪季(int y, int m, int d, int expectStartYear)
        {
            var (start, end) = SkipassSaleRules.ResolveSeasonDates("某某雪票", new DateTime(y, m, d));
            Assert.Equal(new DateTime(expectStartYear, 10, 1), start);
            Assert.Equal(new DateTime(expectStartYear + 1, 6, 1), end);
        }

        [Fact]
        public void 商品名为空_按今天所在雪季()
        {
            var (start, _) = SkipassSaleRules.ResolveSeasonDates(null, new DateTime(2026, 10, 29));
            Assert.Equal(new DateTime(2026, 10, 1), start);
        }

        [Theory]
        [InlineData(1, 1, "万龙", null, null, true)]      // 取消已确认、有效、没退过款：要自动退
        [InlineData(2, 1, "万龙", null, null, false)]     // 取消中：等自我游确认
        [InlineData(0, 1, "万龙", null, null, false)]     // 没取消
        [InlineData(3, 1, "万龙", null, null, false)]     // 申请取消
        [InlineData(1, 0, "万龙", null, null, false)]     // 无效记录
        [InlineData(1, 1, "南山", null, null, false)]     // 南山不走这条
        [InlineData(1, 1, " 南山 ", null, null, false)]
        [InlineData(1, 1, "万龙", 1, null, false)]        // 已经发起过退款
        [InlineData(1, 1, "万龙", null, 309.0, false)]    // 已经退款成功
        public void 取消确认后是否需要自动退款(int isCancel, int valid, string resort, int? haveRefund, double? refundAmount, bool expect)
        {
            Assert.Equal(expect, SkipassSaleRules.NeedsAutoRefund(isCancel, valid, resort, haveRefund, refundAmount));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 万龙雪票（自我游）的售卖规则。只放不碰数据库的判定，便于单测。
    //   1. 只有在职店员能订票，顾客只能看价格（ReserveSkiPass 入口强制，页面隐藏支付栏只是体验）；
    //   2. 成交价 = 自我游门市价 marketPrice，不再加价；门市价缺失（<=0）时回退到「结算价 + 加价」；
    //   3. 价格行变化判定看结算价、建议售价、门市价任一个，不能只看结算价；
    //   4. 非店员看不到进货价（商品 cost、每日结算价、建议售价），也查不了自我游预存款。
    public static class SkipassSaleRules
    {
        public const string STAFF_ONLY_MESSAGE = "仅限店员订票";

        // 门市价缺失时沿用的加价（与旧同步逻辑的默认值一致）
        public const double DEFAULT_MARGIN = 20;

        const double PRICE_EPSILON = 0.005;

        // staff 来自 Util.GetStaffBySessionKey：会话无效、不是店员或已离职都是 null
        public static bool CanReserve(Staff? staff)
        {
            return staff != null && staff.valid == 1;
        }

        public static bool CanSeeCost(Staff? staff)
        {
            return CanReserve(staff);
        }

        public static double ResolveDealPrice(double? marketPrice, double settlementPrice, double margin)
        {
            if (marketPrice != null && marketPrice.Value > 0)
            {
                return marketPrice.Value;
            }
            return settlementPrice + margin;
        }

        public static bool PriceChanged(SkipassDailyPrice existing, double settlementPrice, double salePrice, double marketPrice)
        {
            return Differs(existing.settlementPrice, settlementPrice)
                || Differs(existing.salePrice, salePrice)
                || Differs(existing.marketPrice, marketPrice);
        }

        static bool Differs(double a, double b)
        {
            return Math.Abs(a - b) > PRICE_EPSILON;
        }

        // 顾客看到的价格行不带进货价和建议售价；deal_price、marketPrice 保留
        public static void RedactDailyPrices(IEnumerable<SkipassDailyPrice>? prices)
        {
            if (prices == null) return;
            foreach (SkipassDailyPrice p in prices)
            {
                p.settlementPrice = 0;
                p.salePrice = 0;
            }
        }

        static readonly Regex SeasonRegex = new Regex(@"(?<!\d)(?:20)?(\d{2})\s*[-—~至]\s*(?:20)?(\d{2})(?!\d)");

        // 雪季起止日：商品名里写了「26-27」「2026-27」这类连续两年就按它算（10-1 ～ 次年 6-1，沿用旧商品的惯例），
        // 否则按今天所在雪季（7 月起算新一季）。「10.30-11.4」这类日期区间不是连续年份，不会误判。
        public static (DateTime Start, DateTime End) ResolveSeasonDates(string? productName, DateTime today)
        {
            if (!string.IsNullOrWhiteSpace(productName))
            {
                foreach (Match m in SeasonRegex.Matches(productName))
                {
                    int a = int.Parse(m.Groups[1].Value);
                    int b = int.Parse(m.Groups[2].Value);
                    if (b == (a + 1) % 100)
                    {
                        return (new DateTime(2000 + a, 10, 1), new DateTime(2000 + a + 1, 6, 1));
                    }
                }
            }
            int startYear = today.Month >= 7 ? today.Year : today.Year - 1;
            return (new DateTime(startYear, 10, 1), new DateTime(startYear + 1, 6, 1));
        }
    }
}

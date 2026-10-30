using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 教练学员卡的纯规则：雪季末、价格计算、价格快照、有效性判断。不碰数据库，便于单测。
    ///
    /// 口径（用户 2026-10-30 拍板）：
    ///   养护  按「门店（万龙服务中心 / 崇礼旗舰店）× 单项/双项」4 格分别设置，每格 无 / 折扣率 / 一口价；
    ///         一口价 = 养护服务费封顶价，价格 = min(原价, 一口价)；折扣率 0.8 = 8 折；
    ///         只作用于服务费（common_charge），维修费不打折，加急加价不打折（只对单/双项的基础价生效）。
    ///   租赁  全店、全项目统一一个折扣率，不设一口价。
    ///   有效期 学员领取当天起，到本雪季 4 月 30 日 23:59:59（与优惠券雪季末一致，可用 Coach:SeasonEndDate 覆盖）。
    ///   价格  领取时写进卡里的快照，之后教练改价只影响新领的卡。
    /// </summary>
    public static class CoachCardRules
    {
        public const string ModeNone = "none";
        public const string ModeRate = "rate";
        public const string ModeFixed = "fixed";

        public const string TierOne = "单项";
        public const string TierTwo = "双项";

        public const string BizCare = "养护";
        public const string BizRental = "租赁";

        /// <summary>价格设置里列出的养护门店。其他店（如已关店的南山）没有教练价。</summary>
        public static readonly string[] CareShops = { "万龙服务中心", "崇礼旗舰店" };

        /// <summary>每位教练同时待领取的卡上限，避免连点分享生成一堆孤儿卡。</summary>
        public const int MaxUnclaimedCards = 50;

        /// <summary>批量发券上限：一次最多教练数 / 每位教练张数 / 总张数。</summary>
        public const int MaxGrantCoaches = 100;
        public const int MaxGrantPerCoach = 50;
        public const int MaxGrantTotal = 2000;

        /// <summary>
        /// 本雪季末。先看配置项 Coach:SeasonEndDate（yyyy-MM-dd，必须晚于现在才生效），
        /// 否则与优惠券一致：4 月 30 日 23:59:59，5 月起算下一个雪季。
        /// </summary>
        public static DateTime SeasonEnd(DateTime now, IConfiguration? config = null)
        {
            string? configured = config?["Coach:SeasonEndDate"];
            if (!string.IsNullOrWhiteSpace(configured)
                && DateTime.TryParseExact(configured.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
            {
                DateTime end = d.Date.AddDays(1).AddSeconds(-1);
                if (end > now)
                {
                    return end;
                }
            }
            return TicketTransferRules.SeasonEndDate(now);
        }

        /// <summary>养护单的档位：修刃、热打蜡各算一项（与 CarePricingRules 口径一致）；没有计费项返回 null。</summary>
        public static string? ResolveTier(Care? care)
        {
            int items = CarePricingRules.CountChargeableItems(care!);
            if (items >= 2)
            {
                return TierTwo;
            }
            return items == 1 ? TierOne : null;
        }

        /// <summary>养护门店归属（万龙体验中心归万龙服务中心）；不在价格设置范围内返回 null。</summary>
        public static string? ResolveCareShop(string? shop)
        {
            string name = CarePricingRules.ResolveCareShop(shop);
            return CareShops.Contains(name) ? name : null;
        }

        public static bool IsValidMode(string? mode) => mode == ModeNone || mode == ModeRate || mode == ModeFixed;

        public static bool IsValidTier(string? tier) => tier == TierOne || tier == TierTwo;

        /// <summary>校验一格养护价格。rate 取 (0, 1]，fixed 取 ≥ 0，none 的 value 忽略。</summary>
        public static bool TryValidateCarePrice(string? mode, double value, out string error)
        {
            error = "";
            if (!IsValidMode(mode))
            {
                error = "价格方式只能是 无 / 折扣率 / 一口价";
                return false;
            }
            if (mode == ModeRate && !(value > 0 && value <= 1))
            {
                error = "折扣率必须大于 0 折且不超过 10 折（填 0.1～1，如 0.8 = 8 折）";
                return false;
            }
            if (mode == ModeFixed && !(value >= 0))
            {
                error = "一口价不能为负数";
                return false;
            }
            return true;
        }

        /// <summary>校验租赁折扣率：NULL = 不优惠；否则 (0, 1]。</summary>
        public static bool TryValidateRentalRate(double? rate, out string error)
        {
            error = "";
            if (rate != null && !(rate.Value > 0 && rate.Value <= 1))
            {
                error = "租赁折扣率必须大于 0 折且不超过 10 折（填 0.1～1，如 0.9 = 9 折）";
                return false;
            }
            return true;
        }

        /// <summary>养护优惠金额。rate：原价 − 原价×折扣率；fixed：原价 − 一口价（一口价高于原价时为 0，不涨价）。</summary>
        public static double CareDiscount(string? mode, double value, double basePrice)
        {
            if (basePrice <= 0 || mode == null || mode == ModeNone)
            {
                return 0;
            }
            double discount = 0;
            if (mode == ModeRate && value > 0 && value <= 1)
            {
                discount = basePrice - Money(basePrice * value);
            }
            else if (mode == ModeFixed && value >= 0)
            {
                discount = basePrice - Math.Min(basePrice, value);
            }
            return Math.Min(Money(Math.Max(discount, 0)), Money(basePrice));
        }

        /// <summary>租赁优惠金额：金额 × (1 − 折扣率)。</summary>
        public static double RentalDiscount(double? rate, double amount)
        {
            if (rate == null || !(rate.Value > 0 && rate.Value < 1) || amount <= 0)
            {
                return 0;
            }
            return Math.Min(Money(amount - Money(amount * rate.Value)), Money(amount));
        }

        public static double Money(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        // ---------- 价格快照 ----------

        public sealed class CarePriceCell
        {
            public string shop { get; set; } = "";
            public string tier { get; set; } = "";
            public string mode { get; set; } = ModeNone;
            public double value { get; set; }
        }

        public static string SerializeCarePrices(IEnumerable<CoachCarePrice> prices)
        {
            var cells = prices
                .Where(p => IsValidMode(p.mode) && p.mode != ModeNone)
                .OrderBy(p => p.shop).ThenBy(p => p.tier)
                .Select(p => new CarePriceCell { shop = p.shop, tier = p.tier, mode = p.mode, value = p.value });
            return JsonConvert.SerializeObject(cells);
        }

        public static List<CarePriceCell> ParseCarePrices(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<CarePriceCell>();
            }
            try
            {
                return JsonConvert.DeserializeObject<List<CarePriceCell>>(json) ?? new List<CarePriceCell>();
            }
            catch (JsonException)
            {
                return new List<CarePriceCell>();
            }
        }

        public static CarePriceCell? FindCell(IEnumerable<CarePriceCell> cells, string? shop, string? tier)
        {
            string? s = ResolveCareShop(shop);
            if (s == null || !IsValidTier(tier))
            {
                return null;
            }
            return cells.FirstOrDefault(c => c.shop == s && c.tier == tier && c.mode != ModeNone);
        }

        // ---------- 有效性 ----------

        /// <summary>这张卡现在能不能给该会员用：已领取、属于该会员、在有效期内。</summary>
        public static bool IsEffective(CoachCard? card, int memberId, DateTime now)
        {
            return card != null
                && card.status == CoachCard.StatusClaimed
                && card.member_id == memberId
                && (card.start_date == null || card.start_date <= now)
                && (card.end_date == null || card.end_date >= now);
        }

        /// <summary>租赁按天计费：这张卡在 date 这一天是否有效（按日期比较，领取当天、截止当天都算）。</summary>
        public static bool IsEffectiveOnDate(CoachCard? card, int memberId, DateTime date)
        {
            return card != null
                && card.status == CoachCard.StatusClaimed
                && card.member_id == memberId
                && (card.start_date == null || card.start_date.Value.Date <= date.Date)
                && (card.end_date == null || card.end_date.Value.Date >= date.Date);
        }

        /// <summary>待领取的卡是否还能领：状态是 issued 且没过领取期限。</summary>
        public static bool IsClaimable(CoachCard card, DateTime now)
        {
            return card.status == CoachCard.StatusIssued && (card.claim_deadline == null || card.claim_deadline >= now);
        }

        /// <summary>24 位随机十六进制串，分享路径里用，不可枚举。</summary>
        public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

        /// <summary>学员显示名：真名 → 微信昵称 → 「学员」。</summary>
        public static string StudentName(string? realName, string? nick)
        {
            if (!string.IsNullOrWhiteSpace(realName))
            {
                return realName.Trim();
            }
            return !string.IsNullOrWhiteSpace(nick) ? nick.Trim() : "学员";
        }

        /// <summary>手机号尾号 4 位；不足 4 位返回空串。</summary>
        public static string PhoneTail(string? cell)
        {
            string digits = new string((cell ?? "").Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? digits.Substring(digits.Length - 4) : "";
        }
    }
}

using System;
using System.Globalization;

namespace SnowmeetApi.Helpers
{
    // 介绍人分账的「预计分账日期」（order_share.plan_date）：哪天自动分账。
    // 夜间 23:00 的 ExecuteWLShare 取 shareDate = 昨天，提交「关单日 <= shareDate」的分账，也就是关单次日起的第一个夜间任务才会提交。
    // 预计分账日期是「这条最早哪天提交」：任务在 D 日 23:00 跑（shareDate = D-1），预计日期 <= D 的才提交；没指定（null）就按原来的规则。
    public static class ShareDateRules
    {
        /// <summary>夜间分账任务每天几点跑（/etc/crontab：0 23 * * *）。</summary>
        public const int RunHour = 23;

        /// <summary>夜间任务用 shareDate 选分账时，预计日期最晚到哪天算到期（任务实际跑的那天）。</summary>
        public static DateTime DueLimit(DateTime shareDate) => shareDate.Date.AddDays(1);

        public static bool IsDue(DateTime? planDate, DateTime shareDate) => planDate == null || planDate.Value.Date <= DueLimit(shareDate);

        /// <summary>
        /// 最早能分的那天，用作预填：下一次夜间任务的日期（现在没到 23:00 就是今天，否则是明天），
        /// 并且不早于关单次日（夜间任务只提交关单日 &lt;= 昨天的）。
        /// </summary>
        public static DateTime EarliestPlanDate(DateTime closeDate, DateTime now)
        {
            DateTime nextRun = now.TimeOfDay < TimeSpan.FromHours(RunHour) ? now.Date : now.Date.AddDays(1);
            DateTime afterClose = closeDate.Date.AddDays(1);
            return nextRun > afterClose ? nextRun : afterClose;
        }

        public static bool TryParse(string? text, out DateTime date)
        {
            date = default;
            return !string.IsNullOrWhiteSpace(text)
                && DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}

using System;
using System.Linq.Expressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 优惠券转赠的纯规则：雪季末、订阅消息字段格式、领取上限计数口径。
    /// 全是无副作用的静态方法，单元测试直接覆盖（SnowmeetApi.Tests/TicketTransferRulesTests.cs）。
    /// </summary>
    public static class TicketTransferRules
    {
        /// <summary>
        /// 本雪季末。与财年 5-01 ~ 4-30 口径一致：5 月及以后算下一个雪季周期。
        /// </summary>
        public static DateTime SeasonEndDate(DateTime now)
        {
            int year = now.Month >= 5 ? now.Year + 1 : now.Year;
            return new DateTime(year, 4, 30, 23, 59, 59);
        }

        /// <summary>
        /// 订阅消息 thing 类型字段上限 20 个字符，超了微信直接报 47003。
        /// </summary>
        public static string TruncateThing(string value, int max = 20)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return value.Length <= max ? value : value.Substring(0, max);
        }

        // 时间段连接符必须是**半角** ~ (U+007E)：微信文档规定的就是这个字符。
        // 用全角 ～ (U+FF5E) 会被判 47003 argument invalid! data.timeN.value invalid
        // ——2026-08-15 真机踩到，中文输入法下极易打成全角，改动这里千万别手滑。
        private const string RangeSeparator = "~";

        /// <summary>
        /// 订阅消息 time 类型字段。只到日，起止用半角 ~ 连接。
        /// </summary>
        public static string FormatValidityRange(DateTime? start, DateTime expire)
        {
            string end = FormatDay(expire);
            return start == null ? end : FormatDay(start.Value) + RangeSeparator + end;
        }

        private static string FormatDay(DateTime d)
        {
            return d.Year + "年" + d.Month + "月" + d.Day + "日";
        }

        /// <summary>
        /// 领取上限对店员豁免：店员要做测试、也会代顾客操作，不该被「名下同款券最多 3 张」卡住。
        /// title_level 四档：100=店员 / 200=店长 / 300=系统管理员 / 1000=超管，
        /// 这里用项目里「至少是店员」的通用门槛 100。
        /// 注意这确实是一个上限旁路：店员账号可以无限领同款券，是业务有意接受的。
        /// </summary>
        public static bool IsExemptFromReceiveLimit(Staff staff)
        {
            return staff != null && staff.title_level >= 100;
        }

        /// <summary>
        /// 券是否还没过期。到期日为空视为长期有效；粒度按天，当天到期的当天仍然可用。
        /// 「我的优惠券 - 未使用」列表用它过滤——历史上那里的比较符写反了，
        /// 保留的是已过期的券、把真正有效的券藏了起来（2026-08-14 修正）。
        /// </summary>
        public static bool IsNotExpired(Ticket ticket, DateTime now)
        {
            return ticket.expire_date == null || ((DateTime)ticket.expire_date).Date >= now.Date;
        }

        /// <summary>
        /// 领取上限的计数口径：名下同模板、真正还能用的券。
        /// 写成 Expression 而不是普通方法，是为了同一份条件既能被 EF 翻译成 SQL、
        /// 又能 Compile() 后在单元测试里对内存集合断言——避免"测的是一套、线上跑的是另一套"。
        /// 注意：分享中（shared=1）的券仍在自己名下，计入；到期日为空视为未过期。
        /// </summary>
        public static Expression<Func<Ticket, bool>> UsableTicketFilter(int memberId, int templateId, DateTime now)
        {
            return t => t.member_id == memberId
                && t.template_id == templateId
                && t.valid == 1
                && t.used != 1
                && (t.expire_date == null || t.expire_date >= now);
        }
    }
}

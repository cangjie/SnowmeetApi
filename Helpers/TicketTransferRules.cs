using System;
using System.Linq.Expressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>券出现在哪个列表里——同一张券在不同 tab 下该显示的时间不一样。</summary>
    public enum TicketListContext
    {
        Unused,          // 未使用
        Used,            // 已使用
        SharedPending,   // 已分享，对方还没接受
        SharedAccepted   // 已分享，对方已接受（券已经不在我名下了）
    }

    /// <summary>券卡片上那行时间的最终展示形态。</summary>
    public class TicketDisplayTime
    {
        public DateTime? Time { get; set; }
        public string Label { get; set; } = "";
        /// <summary>预格式化好的 "yyyy-MM-dd HH:mm"，没有时间时是空串。</summary>
        public string Text { get; set; } = "";
    }

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

        // 到期日为空、或是 GenerateTicketByAction 给"模板没设到期日"写的 DateTime.MaxValue，
        // 都表示这张券不会过期
        private static bool NoExpiry(DateTime? expire)
        {
            return expire == null || ((DateTime)expire).Year >= 9999;
        }

        /// <summary>
        /// 券列表卡片上的有效期文案。格式化在服务端做，前端不做日期解析
        /// （iOS 上 new Date('2026-04-01 00:00:00') 是 Invalid Date）。
        /// </summary>
        public static string FormatExpire(DateTime? expire)
        {
            if (NoExpiry(expire))
            {
                return "长期有效";
            }
            DateTime d = (DateTime)expire;
            return d.Year + "." + d.Month + "." + d.Day;
        }

        /// <summary>
        /// 是否临期（默认 30 天内到期），列表上标红提醒。
        /// 已核销的不提醒；已经过期的归"已过期"状态处理，不该再顶着"即将到期"的红字。
        /// </summary>
        public static bool IsExpiringSoon(Ticket ticket, DateTime now, int days = 30)
        {
            if (ticket.used == 1 || NoExpiry(ticket.expire_date))
            {
                return false;
            }
            DateTime d = ((DateTime)ticket.expire_date).Date;
            return d >= now.Date && d <= now.Date.AddDays(days);
        }

        /// <summary>
        /// 券种 banner：按券名关键词判定配色 class 与短标签。
        ///
        /// 为什么不用 ticket_template.type：它虽然有 9 种取值，但粒度不对——「养护券」下面
        /// 同时装着打蜡、修刃、双项、代金，而这几种恰恰是要区分的；反过来「租赁券」「消费券」
        /// 又只有一两个模板。按券名关键词判定能覆盖现有全部 18 个模板，新增模板自动适配。
        ///
        /// 规则**有序**，先匹配先赢：「双项」必须排在「打蜡」「修刃」前面，否则
        /// 「万龙店修板打蜡券」会被判成单项。
        /// </summary>
        public static TicketStateView ResolveBanner(string templateName)
        {
            string n = (templateName ?? "").Trim();
            bool Has(string k) { return n.Contains(k); }

            // 双项：显式写了「双项」，或同时含修刃和打蜡
            if (Has("双项") || ((Has("修板") || Has("修刃")) && Has("打蜡")))
            {
                return Banner("dual", "双项");
            }
            if (Has("热打蜡") || Has("热蜡"))
            {
                return Banner("hotwax", "热打蜡");
            }
            if (Has("修板") || Has("修刃"))
            {
                return Banner("edge", "修刃");
            }
            if (Has("打蜡"))
            {
                return Banner("wax", "机打蜡");
            }
            if (Has("课程") || Has("教练"))
            {
                return Banner("course", "课程");
            }
            if (Has("试滑"))
            {
                return Banner("pass", "试滑");
            }
            if (Has("体验") || Has("租赁"))
            {
                return Banner("pass", "体验");
            }
            if (Has("满减"))
            {
                return Banner("cash", "满减");
            }
            if (Has("代金"))
            {
                return Banner("cash", "代金");
            }
            if (Has("消费"))
            {
                return Banner("cash", "消费");
            }
            if (Has("优惠") || Has("内购"))
            {
                return Banner("cash", "优惠");
            }
            if (Has("非雪季"))
            {
                return Banner("wax", "非雪季");
            }
            // 兜底：主蓝 + 券名去掉「券」字后最多 4 个字
            string label = n.Replace("券", "").Trim();
            if (label.Length > 4)
            {
                label = label.Substring(0, 4);
            }
            return Banner("wax", label == "" ? "优惠" : label);
        }

        private static TicketStateView Banner(string cls, string label)
        {
            return new TicketStateView() { Cls = cls, Label = label };
        }

        /// <summary>
        /// 哪条 ticket_log 才算「一次真转赠」。
        ///
        /// ticket_log 有 5 个写入方，只有 AcceptTicketCore 那条是真转赠：
        ///   AcceptTicketCore   sender=原持有人 accepter=接受人 memo=分享获得/扫码关注公众号后自动接受  ← 真转赠
        ///   CancelShare        sender=本人     accepter=""     memo=撤回分享
        ///   Use（核销）         sender=""       accepter=店员   memo=核销
        ///   ExperienceController        sender=店员 accepter=顾客 memo=体验订单获得,ID:x
        ///   MaintainLogsController      sender=店员 accepter=顾客 memo=养护订单获得,ID:x
        ///
        /// 光判 accepter 非空且不等于 sender 是不够的：核销日志 sender 是空串、
        /// 发券日志收发本来就是两个人。2026-08-15 生产库实测——只判 accepter 那版命中 4158 条，
        /// 加上 sender 非空是 3580 条，再加 memo 黑名单才是真实的 17 条，差 245 倍。
        ///
        /// 写成 Expression 是为了同一份口径既能被 EF 翻成 SQL（EXISTS/JOIN），
        /// 又能 Compile() 后在单元测试里对内存集合断言。
        /// </summary>
        public static Expression<Func<TicketLog, bool>> TransferLogFilter()
        {
            return l => l.sender_open_id != null && l.sender_open_id != ""
                && l.accepter_open_id != null && l.accepter_open_id != ""
                && l.accepter_open_id != l.sender_open_id
                && l.memo != null && l.memo != ""
                && !l.memo.StartsWith("体验订单获得")
                && !l.memo.StartsWith("养护订单获得")
                && l.memo != "管理员赠送";
        }

        /// <summary>
        /// 券卡片上那行时间显示什么。
        ///
        /// ⚠️ 千万别用 ticket.accepted_time —— 名字像"接受时间"，实际只在建券时赋 DateTime.Now、
        /// 转赠接受时不更新（生产库 12164 张券它全部等于 create_date）。真正的接受时间只存在于
        /// ticket_log.transact_time，所以要由调用方查好了从 transferTime 传进来。
        ///
        /// 时间在这里就格式化成字符串下发：iOS 上 new Date('2026-08-16 10:30:00') 会得到
        /// Invalid Date，让前端自己解析日期是给自己找麻烦。
        /// </summary>
        public static TicketDisplayTime ResolveDisplayTime(Ticket ticket, TicketListContext context,
            DateTime? transferTime)
        {
            DateTime? time;
            string label;
            switch (context)
            {
                case TicketListContext.Used:
                    time = ticket.used_time;
                    label = "核销时间";
                    break;
                case TicketListContext.SharedPending:
                    time = ticket.shared_time;
                    label = "分享时间";
                    break;
                case TicketListContext.SharedAccepted:
                    time = transferTime;
                    label = "对方领取时间";
                    break;
                default:
                    // 未使用：转赠得来的显示领取时间，自己获得的显示获得时间
                    time = transferTime ?? ticket.create_date;
                    label = transferTime != null ? "领取时间" : "获得时间";
                    break;
            }
            return new TicketDisplayTime()
            {
                Time = time,
                Label = time == null ? "" : label,
                Text = time == null ? "" : ((DateTime)time).ToString("yyyy-MM-dd HH:mm")
            };
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
        /// IsNotExpired 的 EF 可翻译版本（两者必须逐项等价，有单测钉住）。
        /// 不在列上套 .Date：那会翻成 CONVERT(date, expire_date) 而废掉索引。
        /// 改成先把 now 切到当天 00:00 再直接比列，语义一样（当天到期的当天仍算未过期）。
        /// </summary>
        public static Expression<Func<Ticket, bool>> NotExpiredFilter(DateTime now)
        {
            DateTime dayStart = now.Date;
            return t => t.expire_date == null || t.expire_date >= dayStart;
        }

        /// <summary>
        /// 优惠券管理后台的默认范围：未过期 ∪ 已核销。
        /// 反面（被排除掉的）= 已过期且从未核销 = 废券，生产库里有 9174 张，
        /// 不默认滤掉列表根本没法看。
        /// </summary>
        public static Expression<Func<Ticket, bool>> NotWastedFilter(DateTime now)
        {
            DateTime dayStart = now.Date;
            return t => t.used == 1 || t.expire_date == null || t.expire_date >= dayStart;
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

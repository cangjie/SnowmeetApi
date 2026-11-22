using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>订单详情页「关闭 / 重开记录」里的一行。</summary>
    public class OrderCloseEvent
    {
        /// <summary>「关闭」或「重开」。</summary>
        public string action { get; set; } = "";
        public DateTime time { get; set; }
        /// <summary>操作的员工；系统自动关单没有员工。</summary>
        public int? staffId { get; set; }
        /// <summary>员工姓名；系统自动关单显示「系统自动」。</summary>
        public string staffName { get; set; } = "";
        /// <summary>系统自动（夜间关单）为 true。</summary>
        public bool auto { get; set; }
        public string? memo { get; set; }
    }

    /// <summary>
    /// 2026-11-22 用户要求：租赁订单可以重开，每次关闭、重开的时间和操作人要能在订单页看到。
    /// 记录来自审计日志 core_data_mod_log（table_name = Order、field_name = closed，current_value 1 = 关闭、0 = 重开）。
    /// 夜间自动关单（RentController.CloseOrder）不写日志，所以：
    ///   · 一次日志都没有、订单现在是关闭状态（有关闭时间）→ 补一行「系统自动」的关闭，时间取订单的关闭时间；
    ///   · 第一次重开时，如果还没有任何关闭日志，重开接口会先把这次自动关单补记成一条日志（OrderController），所以之后的记录是全的。
    /// </summary>
    public static class OrderCloseHistoryRules
    {
        public const string TABLE = "Order";
        public const string FIELD = "closed";
        public const string SCENE_CLOSE = "订单关闭";
        public const string SCENE_REOPEN = "订单重开";
        public const string AUTO_NAME = "系统自动";
        public const string AUTO_CLOSE_MEMO = "系统自动关单（重开前补记）";
        public const string NO_LOG_MEMO = "没有操作记录，按订单的关闭时间显示";

        public static List<OrderCloseEvent> Build(int closed, DateTime? closeDate, IEnumerable<CoreDataModLog> logs)
        {
            List<OrderCloseEvent> events = new List<OrderCloseEvent>();
            foreach (CoreDataModLog log in (logs ?? new List<CoreDataModLog>())
                .Where(l => l.table_name == TABLE && l.field_name == FIELD
                    && (l.current_value == "1" || l.current_value == "0"))
                .OrderBy(l => l.create_date).ThenBy(l => l.id))
            {
                events.Add(new OrderCloseEvent()
                {
                    action = log.current_value == "1" ? "关闭" : "重开",
                    time = log.create_date,
                    staffId = log.staff_id,
                    staffName = StaffName(log),
                    auto = log.staff_id == null,
                    memo = string.IsNullOrWhiteSpace(log.manual_memo) ? null : log.manual_memo
                });
            }
            if (events.Count == 0 && closed == 1 && closeDate != null)
            {
                events.Add(new OrderCloseEvent()
                {
                    action = "关闭",
                    time = closeDate.Value,
                    staffId = null,
                    staffName = AUTO_NAME,
                    auto = true,
                    memo = NO_LOG_MEMO
                });
            }
            return events;
        }

        private static string StaffName(CoreDataModLog log)
        {
            if (log.staff_id == null)
            {
                return AUTO_NAME;
            }
            string name = (log.staff?.name ?? "").Trim();
            return name != "" ? name : "员工#" + log.staff_id.Value.ToString();
        }
    }
}

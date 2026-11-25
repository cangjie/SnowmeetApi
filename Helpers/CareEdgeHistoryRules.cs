using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>养护订单详情页「角度记录」里的一行：一次改角度，或一次重新打印标签存根。</summary>
    public class CareEdgeHistoryItem
    {
        /// <summary>"angle" = 改角度（from → to）；"print" = 重新打印标签存根（to = 这次打在标签上的角度，from = 换下来的旧标签上的角度）。</summary>
        public string kind { get; set; } = "";
        public DateTime time { get; set; }
        public int? staffId { get; set; }
        /// <summary>员工姓名；日志里没有记录操作人时是「未记录」。</summary>
        public string staffName { get; set; } = "";
        public string from { get; set; } = "";
        public string to { get; set; } = "";
        public string? scene { get; set; }
    }

    /// <summary>
    /// 修刃角度的修改历史（用户 2026-11-25 要求）。记录来自审计日志 core_data_mod_log：
    ///   · 改角度：CareController.SetEdgeDegree 写 table_name = care、field_name = edge_degree；
    ///     旧版页面整行保存（UpdateCare → Util.GetUpdateDifferenceLog）写的 table_name 是类名 Care，空值是真 NULL，所以两种都认；
    ///   · 重新打印：MarkEdgeLabelPrinted 写 field_name = label_edge_degree、current_value 为空（清掉「待重打」状态）；
    ///     第一次改角度时记下标签角度那条（current_value 不空）不是一次打印，不算。
    /// 角度为空 / 字符串 "null"（Util.CreateCoreDataModLog 把空写成这个）一律按 89（标签上印的就是 89）；
    /// 前后一样的不算修改（比如 null → 89）。从前到后重放日志，得到打印那一刻标签上的角度。
    /// </summary>
    public static class CareEdgeHistoryRules
    {
        public const string KIND_ANGLE = "angle";
        public const string KIND_PRINT = "print";
        public const string FIELD_ANGLE = "edge_degree";
        public const string FIELD_LABEL = "label_edge_degree";
        public const string NO_STAFF_NAME = "未记录";

        /// <param name="currentEdgeDegree">装备现在的角度，日志里一条改角度都没有时用它当起点。</param>
        public static List<CareEdgeHistoryItem> Build(string? currentEdgeDegree, IEnumerable<CoreDataModLog> logs)
        {
            List<CoreDataModLog> relevant = (logs ?? new List<CoreDataModLog>())
                .Where(l => string.Equals(l.table_name, "care", StringComparison.OrdinalIgnoreCase)
                    && (l.field_name == FIELD_ANGLE || l.field_name == FIELD_LABEL))
                .OrderBy(l => l.create_date).ThenBy(l => l.id).ToList();

            // 起点：第一条改角度日志里「改之前」的角度；一条都没有就是现在的角度
            CoreDataModLog? firstAngle = relevant.FirstOrDefault(l => l.field_name == FIELD_ANGLE);
            string angle = Normalize(firstAngle != null ? firstAngle.prev_value : currentEdgeDegree);

            List<CareEdgeHistoryItem> items = new List<CareEdgeHistoryItem>();
            foreach (CoreDataModLog log in relevant)
            {
                if (log.field_name == FIELD_ANGLE)
                {
                    string from = Normalize(log.prev_value);
                    string to = Normalize(log.current_value);
                    if (from == to) continue;
                    angle = to;
                    items.Add(Item(KIND_ANGLE, log, from, to));
                }
                else if (IsEmpty(log.current_value))
                {
                    items.Add(Item(KIND_PRINT, log, Normalize(log.prev_value), angle));
                }
            }
            // 新的在前；同一时刻按日志 id（后写的在前）
            items.Reverse();
            return items;
        }

        private static CareEdgeHistoryItem Item(string kind, CoreDataModLog log, string from, string to) => new CareEdgeHistoryItem
        {
            kind = kind,
            time = log.create_date,
            staffId = log.staff_id,
            staffName = StaffName(log),
            from = from,
            to = to,
            scene = string.IsNullOrWhiteSpace(log.scene) ? null : log.scene
        };

        private static bool IsEmpty(string? value)
            => value == null || value.Trim().Length == 0 || value.Trim() == "null";

        /// <summary>角度的统一写法：空 → 89；89.0 → 89。</summary>
        private static string Normalize(string? value)
        {
            if (IsEmpty(value)) return CareEdgeLabelRules.DefaultDegree;
            string text = value!.Trim();
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                ? d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                : text;
        }

        private static string StaffName(CoreDataModLog log)
        {
            if (log.staff_id == null) return NO_STAFF_NAME;
            string name = (log.staff?.name ?? "").Trim();
            return name != "" ? name : "员工#" + log.staff_id.Value.ToString();
        }
    }
}

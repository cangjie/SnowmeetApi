using System;
using System.Collections.Generic;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>订单里的一项装备信息和顾客装备列表里对应那件不一样。</summary>
    public sealed class CareEquipmentDiff
    {
        /// <summary>equipment / brand / scale / serials</summary>
        public string field { get; set; } = "";
        public string orderValue { get; set; } = "";
        /// <summary>装备列表里没填时为 null。</summary>
        public string? listValue { get; set; }
    }

    /// <summary>
    /// 养护订单装备和顾客装备列表的比较口径（用户 2026-11-28，查看订单时只做标注）：
    ///   · 订单里没填的项（空、只有分隔竖线、「无」）不比较；
    ///   · 订单填了、装备列表里空着（含「无」），算不一致；
    ///   · 首尾空格和大小写不算差异；左右两个序列号各自整理后再比。
    /// </summary>
    public static class CareEquipmentDiffRules
    {
        public static List<CareEquipmentDiff> Compare(Care care, MemberEquipment equipment)
        {
            var diffs = new List<CareEquipmentDiff>();
            Add(diffs, "equipment", Text(care.equipment), Text(equipment.equipment));
            Add(diffs, "brand", Text(care.brand), Text(equipment.brand));
            Add(diffs, "scale", Text(care.scale), Text(equipment.scale));
            Add(diffs, "serials", Serials(care.serials), Serials(equipment.serials));
            return diffs;
        }

        private static void Add(List<CareEquipmentDiff> diffs, string field, string? orderValue, string? listValue)
        {
            if (orderValue == null || string.Equals(orderValue, listValue, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            diffs.Add(new CareEquipmentDiff { field = field, orderValue = orderValue, listValue = listValue });
        }

        private static string? Text(string? raw)
        {
            string s = (raw ?? "").Trim();
            return s.Length == 0 || s == "无" ? null : s;
        }

        private static string? Serials(string? raw)
        {
            string s = raw ?? "";
            if (!s.Contains('|'))
            {
                return Text(s);
            }
            string[] parts = s.Split('|', 2);
            string left = Text(parts[0]) ?? "";
            string right = Text(parts[1]) ?? "";
            return left.Length == 0 && right.Length == 0 ? null : left + "|" + right;
        }
    }
}

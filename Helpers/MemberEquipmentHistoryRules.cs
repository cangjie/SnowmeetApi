using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    public class MemberEquipmentHistoryItem
    {
        /// <summary>"create" | "update" | "delete" | "merge"</summary>
        public string kind { get; set; } = "";
        public string field { get; set; } = "";
        public string fieldLabel { get; set; } = "";
        public DateTime time { get; set; }
        public int? staffId { get; set; }
        public string staffName { get; set; } = "";
        public string from { get; set; } = "";
        public string to { get; set; } = "";
        public string? fromPhotoUrl { get; set; }
        public string? toPhotoUrl { get; set; }
        public string? scene { get; set; }
        public string? memo { get; set; }
    }

    /// <summary>
    /// 会员装备列表的创建、修改、删除及合并历史（基于 core_data_mod_log，table_name = "member_equipment"）。
    /// 按时间倒序排列（同一时间戳按 id 倒序）。
    /// </summary>
    public static class MemberEquipmentHistoryRules
    {
        public const string TABLE_NAME = "member_equipment";
        public const string NO_STAFF_NAME = "未记录";

        public static List<MemberEquipmentHistoryItem> Build(
            IEnumerable<CoreDataModLog>? logs,
            IReadOnlyDictionary<int, string>? photoUrlById = null)
        {
            List<CoreDataModLog> ordered = (logs ?? Enumerable.Empty<CoreDataModLog>())
                .Where(l => string.Equals(l.table_name, TABLE_NAME, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(l => l.create_date)
                .ThenByDescending(l => l.id)
                .ToList();

            List<MemberEquipmentHistoryItem> items = new List<MemberEquipmentHistoryItem>();
            foreach (CoreDataModLog log in ordered)
            {
                string field = (log.field_name ?? "").Trim();
                if (string.Equals(field, "create", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new MemberEquipmentHistoryItem
                    {
                        kind = "create",
                        field = "create",
                        fieldLabel = "新建装备",
                        time = log.create_date,
                        staffId = log.staff_id,
                        staffName = StaffName(log),
                        from = "",
                        to = CleanValue(log.current_value),
                        scene = CleanNullable(log.scene),
                        memo = CleanNullable(log.manual_memo)
                    });
                    continue;
                }

                if (string.Equals(field, "valid", StringComparison.OrdinalIgnoreCase))
                {
                    string prev = CleanValue(log.prev_value);
                    string curr = CleanValue(log.current_value);
                    if (prev == curr) continue;
                    items.Add(new MemberEquipmentHistoryItem
                    {
                        kind = curr == "0" ? "delete" : "update",
                        field = "valid",
                        fieldLabel = curr == "0" ? "删除装备" : "恢复装备",
                        time = log.create_date,
                        staffId = log.staff_id,
                        staffName = StaffName(log),
                        from = prev == "1" ? "有效" : "已删除",
                        to = curr == "0" ? "已删除" : "有效",
                        scene = CleanNullable(log.scene),
                        memo = CleanNullable(log.manual_memo)
                    });
                    continue;
                }

                if (string.Equals(field, "member_id", StringComparison.OrdinalIgnoreCase))
                {
                    string prev = CleanValue(log.prev_value);
                    string curr = CleanValue(log.current_value);
                    if (prev == curr) continue;
                    items.Add(new MemberEquipmentHistoryItem
                    {
                        kind = "merge",
                        field = "member_id",
                        fieldLabel = "会员合并迁移",
                        time = log.create_date,
                        staffId = log.staff_id,
                        staffName = StaffName(log),
                        from = prev == "" ? "无" : ("会员#" + prev),
                        to = curr == "" ? "无" : ("会员#" + curr),
                        scene = CleanNullable(log.scene),
                        memo = CleanNullable(log.manual_memo)
                    });
                    continue;
                }

                string label = FieldLabel(field);
                if (label == "") continue;

                string fromRaw = CleanValue(log.prev_value);
                string toRaw = CleanValue(log.current_value);
                if (fromRaw == toRaw) continue;

                if (string.Equals(field, "image_id", StringComparison.OrdinalIgnoreCase))
                {
                    string? fromPhoto = ResolvePhotoUrl(fromRaw, photoUrlById);
                    string? toPhoto = ResolvePhotoUrl(toRaw, photoUrlById);
                    items.Add(new MemberEquipmentHistoryItem
                    {
                        kind = "update",
                        field = "image_id",
                        fieldLabel = label,
                        time = log.create_date,
                        staffId = log.staff_id,
                        staffName = StaffName(log),
                        from = fromRaw == "" ? "无照片" : ("照片#" + fromRaw),
                        to = toRaw == "" ? "无照片" : ("照片#" + toRaw),
                        fromPhotoUrl = fromPhoto,
                        toPhotoUrl = toPhoto,
                        scene = CleanNullable(log.scene),
                        memo = CleanNullable(log.manual_memo)
                    });
                    continue;
                }

                items.Add(new MemberEquipmentHistoryItem
                {
                    kind = "update",
                    field = field,
                    fieldLabel = label,
                    time = log.create_date,
                    staffId = log.staff_id,
                    staffName = StaffName(log),
                    from = FormatFieldDisplay(field, fromRaw),
                    to = FormatFieldDisplay(field, toRaw),
                    scene = CleanNullable(log.scene),
                    memo = CleanNullable(log.manual_memo)
                });
            }

            return items;
        }

        private static string FieldLabel(string field) => field switch
        {
            "equipment" => "装备类型",
            "brand" => "品牌",
            "scale" => "长度",
            "serials" => "序列号",
            "image_id" => "装备照片",
            _ => ""
        };

        private static string FormatFieldDisplay(string field, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "未填";
            if (field == "scale")
            {
                return raw.EndsWith("cm", StringComparison.OrdinalIgnoreCase) ? raw : (raw + "cm");
            }
            if (field == "serials" && raw.Contains('|'))
            {
                string[] parts = raw.Split('|', 2);
                string left = parts[0].Trim();
                string right = parts.Length > 1 ? parts[1].Trim() : "";
                if (left == "" && right == "") return "未填";
                return $"左:{(left == "" ? "无" : left)} / 右:{(right == "" ? "无" : right)}";
            }
            return raw;
        }

        private static string? ResolvePhotoUrl(string rawId, IReadOnlyDictionary<int, string>? photoUrlById)
        {
            if (photoUrlById == null || !int.TryParse(rawId, out int id) || id <= 0) return null;
            return photoUrlById.TryGetValue(id, out string? url) && !string.IsNullOrWhiteSpace(url) ? url : null;
        }

        private static string CleanValue(string? v)
        {
            if (v == null) return "";
            string s = v.Trim();
            if (s == "" || s == "null" || s == "|") return "";
            return s;
        }

        private static string? CleanNullable(string? v)
        {
            string s = CleanValue(v);
            return s == "" ? null : s;
        }

        private static string StaffName(CoreDataModLog log)
        {
            if (log.staff_id == null) return NO_STAFF_NAME;
            string name = (log.staff?.name ?? "").Trim();
            return name != "" ? name : ("员工#" + log.staff_id.Value);
        }
    }
}

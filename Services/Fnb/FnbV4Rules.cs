using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public static class FnbV4Rules
{
    public static bool Storage(string? value) => value is "ambient" or "chilled" or "frozen";
    public static byte Dimension(string? value) => value switch { "weight" => 1, "volume" => 2, "count" => 3, _ => 0 };
    public static string BaseUnit(string? measure) => measure switch { "weight" => "g", "volume" => "ml", "count" => "piece", _ => "" };
    public static string RequiredText(string? value, int bytes, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || !FnbText.FitsChineseVarchar(text, bytes)) throw new ArgumentException(label + "为空、过长或含不支持的字符");
        return text;
    }
    public static string? OptionalText(string? value, int bytes, string label) =>
        string.IsNullOrWhiteSpace(value) ? null : RequiredText(value, bytes, label);
    public static string Code(string? value, string label)
    {
        var code = RequiredText(value, 16, label).ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z0-9]{1,16}$", RegexOptions.CultureInvariant)) throw new ArgumentException(label + "须为 1–16 位英文字母或数字");
        return code;
    }
    public static void Days(int? value)
    { if (value is < 0 or > 36500) throw new ArgumentException("天数须在 0–36500 之间"); }
    public static decimal Quantity(decimal value, bool zero = false)
    {
        if (value < 0 || !zero && value == 0 || value > 1_000_000_000_000m || decimal.Round(value, 6) != value)
            throw new ArgumentException("数量须为有效正数（最多 6 位小数）");
        return value;
    }
    public static decimal Ratio(decimal upstreamPerBase, decimal downstreamPerBase)
    {
        var ratio = decimal.Round(Quantity(upstreamPerBase) / Quantity(downstreamPerBase), 6, MidpointRounding.AwayFromZero);
        if (ratio <= 0 || ratio > 1_000_000_000_000m) throw new ArgumentException("形态换算比例超出支持范围");
        return ratio;
    }
    public static void Operation(string? name, decimal yield, decimal hours)
    {
        RequiredText(name, 100, "作业名称");
        Quantity(yield); Quantity(hours, true);
        if (yield > 1) throw new ArgumentException("标准出成率须大于 0 且不大于 1");
    }
    public static (int WarnDays, int? OpenDays) Defaults(FnbItem item, FnbCategory category) =>
        (item.warn_days ?? category.warn_days ?? 1, item.open_days ?? category.open_days);
    // 先食材，再分类；在同一层先季节专用规则，再 all。warm = 6–9 月。
    public static FnbShelfLifeRule? ShelfRule(IEnumerable<FnbShelfLifeRule> rules, int itemId, int categoryId,
        string storage, int productionMonth)
    {
        if (productionMonth is < 1 or > 12 || !Storage(storage)) throw new ArgumentException("生产月份或储存方式无效");
        string season = productionMonth is >= 6 and <= 9 ? "warm" : "cold";
        return rules.Where(x => x.valid && x.storage_type == storage && (x.season == season || x.season == "all") &&
            (x.item_id == itemId || x.item_id == null && x.category_id == categoryId))
            .OrderBy(x => x.item_id == itemId ? 0 : 1).ThenBy(x => x.season == season ? 0 : 1).ThenBy(x => x.id).FirstOrDefault();
    }
}

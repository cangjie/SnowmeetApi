using System;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Helpers;

public static class CareCardRules
{
    // 普通打蜡季卡（含测试商品）提供机打蜡；明确标注热蜡的季卡保留热蜡权益。
    public static bool IsMachineWaxSeasonCard(int? total, string? name) =>
        total == null && (name ?? "").Contains("打蜡", StringComparison.Ordinal)
        && !(name ?? "").Contains("热蜡", StringComparison.Ordinal)
        && !(name ?? "").Contains("热打蜡", StringComparison.Ordinal);

    public static string ServiceMode(CareCardPreviewItem card) =>
        IsMachineWaxSeasonCard(card.total, card.name) ? "machine"
        : card.requiresBinding ? "wax"
        : card.projectCount == 2 || (card.projectCount == null && card.name.Contains("双项")) ? "dual" : "single";

    public static void ApplyDefaultServices(CareCardPreviewItem card, Care care, bool reset = false)
    {
        string mode = ServiceMode(card);
        // 只在选卡/新增装备时给默认项目，后续选择完整保留，卡外项目另行计费。
        if (reset && care.summer == null && !care.warranty && !care.entertain)
        {
            if (mode == "machine") { care.free_wax = 1; care.need_edge = care.need_wax = care.need_unwax = 0; }
            else if (mode == "wax") { care.need_wax = care.need_unwax = 1; care.need_edge = care.free_wax = 0; }
            else if (mode == "dual") { care.need_edge = care.need_wax = care.need_unwax = 1; care.free_wax = 0; }
        }
        if (care.need_edge != 1) care.edge_degree = null;
        else if (string.IsNullOrWhiteSpace(care.edge_degree)) care.edge_degree = "89";
    }

    public static Care PreviewCare(CareCardPreviewItem card, Care original, bool reset = false)
    {
        var care = new Care { id = original.id, is_cancel = original.is_cancel, summer = original.summer,
            need_repair = original.need_repair, repair_charge = original.repair_charge,
            warranty = original.warranty, entertain = original.entertain,
            need_edge = original.need_edge, edge_degree = original.edge_degree,
            need_wax = original.need_wax, free_wax = original.free_wax, need_unwax = original.need_unwax,
            urgent = original.urgent, discount = original.discount, repair_memo = original.repair_memo,
            biz_type = original.biz_type };
        ApplyDefaultServices(card, care, reset);
        return care;
    }

    // “不限次数”与“限板”是不同概念，只有打蜡季卡限板。
    public static bool RequiresEquipmentBinding(int? total, string name) =>
        total == null && ((name ?? "").Contains("打蜡", StringComparison.Ordinal)
            || (name ?? "").Contains("热蜡", StringComparison.Ordinal));

    public static Care CoveredServices(CareCardPreviewItem card, Care selected)
    {
        var covered = new Care();
        if (selected.is_cancel || selected.summer != null || selected.warranty || selected.entertain) return covered;
        string mode = ServiceMode(card);
        if (mode == "machine") { covered.free_wax = selected.free_wax; return covered; }
        int allowed = mode == "dual" ? 2 : 1;
        if (mode != "wax" && selected.need_edge == 1) { covered.need_edge = 1; allowed--; }
        if (allowed > 0 && selected.need_wax == 1) covered.need_wax = covered.need_unwax = 1;
        return covered;
    }

    public static bool HasCoveredService(CareCardPreviewItem card, Care selected)
    {
        if (selected.is_cancel || selected.summer != null || selected.warranty || selected.entertain) return false;
        var covered = CoveredServices(card, selected);
        // 机打蜡季卡升级项目沿用已有季卡加价规则，仍核销本次季卡权益。
        return covered.need_edge == 1 || covered.need_wax == 1 || covered.free_wax == 1
            || ServiceMode(card) == "machine" && (selected.need_edge == 1 || selected.need_wax == 1);
    }

    public static int RestoredPunches(int used, int restore)
    {
        if (restore < 0 || used < restore) throw new InvalidOperationException("卡次数与核销记录不一致，请联系管理员核对");
        return used - restore;
    }
}

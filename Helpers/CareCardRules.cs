using System;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Helpers;

public static class CareCardRules
{
    public static void ApplyDefaultServices(CareCardPreviewItem card, Care care)
    {
        if (care.need_edge == 1 || care.need_wax == 1 || care.free_wax == 1) return;
        if (card.total == null && card.name.Contains("机打蜡", StringComparison.Ordinal)) care.free_wax = 1;
        else if (card.requiresBinding) { care.need_wax = 1; care.need_unwax = 1; }
        else if (card.projectCount == 2 || card.name.Contains("双项"))
        { care.need_edge = 1; care.need_wax = 1; care.need_unwax = 1; }
        // 单项卡不能猜本次要修刃还是热蜡，仍由店员选择。
    }

    public static Care PreviewCare(CareCardPreviewItem card, Care original)
    {
        var care = new Care { is_cancel = original.is_cancel, summer = original.summer,
            need_repair = original.need_repair, repair_charge = original.repair_charge,
            warranty = original.warranty, entertain = original.entertain,
            need_edge = original.need_edge, need_wax = original.need_wax, free_wax = original.free_wax,
            need_unwax = original.need_unwax };
        ApplyDefaultServices(card, care);
        return care;
    }

    // “不限次数”与“限板”是不同概念，只有打蜡季卡限板。
    public static bool RequiresEquipmentBinding(int? total, string name) =>
        total == null && ((name ?? "").Contains("打蜡", StringComparison.Ordinal)
            || (name ?? "").Contains("热蜡", StringComparison.Ordinal));

    public static bool Covers(CareCardPreviewItem card, Care care)
    {
        if (care.is_cancel || care.summer != null || care.need_repair == 1 || care.repair_charge != 0
            || care.warranty || care.entertain) return false;
        if (card.total == null && card.name.Contains("机打蜡", StringComparison.Ordinal))
            return care.free_wax == 1 && care.need_edge == 0 && care.need_wax == 0;
        if (card.requiresBinding)
            return care.need_wax == 1 && care.need_edge == 0;
        int projects = (care.need_edge == 1 ? 1 : 0) + (care.need_wax == 1 ? 1 : 0);
        int allowed = card.projectCount ?? (card.name.Contains("双项") ? 2 : 1);
        return projects > 0 && projects <= allowed && care.free_wax == 0;
    }

    public static int RestoredPunches(int used, int restore)
    {
        if (restore < 0 || used < restore) throw new InvalidOperationException("卡次数与核销记录不一致，请联系管理员核对");
        return used - restore;
    }
}

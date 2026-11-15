using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers;

// 机构自助设置会员卡价格时的底价校验（纯规则）。管理员设底价，机构只能在底价之内设置；没开放的格子不能设优惠。
public static class CoachFloorRules
{
    private const double Eps = 1e-9;

    /// <summary>这一格有没有开放：设了最低折扣率或最低一口价。</summary>
    public static bool IsOpen(CoachPriceFloor? floor) => floor != null && (floor.min_rate != null || floor.min_fixed != null);

    private static string RateText(double rate) => Math.Round(rate * 10, 2).ToString("0.##") + " 折";

    /// <summary>校验一格养护价格；通过返回 null，否则返回给机构看的原因（label 如「万龙服务中心 单项」）。mode = none 总是通过。</summary>
    public static string? CheckCare(CoachPriceFloor? floor, string? mode, double value, string label)
    {
        if (mode == null || mode == CoachCardRules.ModeNone) return null;
        if (!IsOpen(floor)) return label + "：管理员还没有开放这一项，请联系管理员";
        if (mode == CoachCardRules.ModeRate)
        {
            if (floor!.min_rate == null) return label + "：管理员这一项只开放了一口价，不能设折扣";
            if (value + Eps < floor.min_rate.Value) return label + "：折扣不能低于 " + RateText(floor.min_rate.Value);
        }
        else if (mode == CoachCardRules.ModeFixed)
        {
            if (floor!.min_fixed == null) return label + "：管理员这一项只开放了折扣，不能设一口价";
            if (value + Eps < floor.min_fixed.Value) return label + "：一口价不能低于 ¥" + Math.Round(floor.min_fixed.Value, 2).ToString("0.##");
        }
        return null;
    }

    /// <summary>租赁折扣率：null = 不优惠，总是通过。</summary>
    public static string? CheckRental(CoachPriceFloor? floor, double? rate)
    {
        if (rate == null) return null;
        if (floor == null || floor.min_rate == null) return "租赁：管理员还没有开放租赁折扣，请联系管理员";
        if (rate.Value + Eps < floor.min_rate.Value) return "租赁：折扣不能低于 " + RateText(floor.min_rate.Value);
        return null;
    }

    /// <summary>管理员设底价时的输入校验：最低折扣率 (0,1]，最低一口价 ≥ 0；两个都空 = 不开放。</summary>
    public static string? ValidateFloorInput(double? minRate, double? minFixed, string label)
    {
        if (minRate != null && !(minRate.Value > 0 && minRate.Value <= 1)) return label + "：最低折扣填 0.1～10 折（8 表示 8 折）";
        if (minFixed != null && !(minFixed.Value >= 0)) return label + "：最低一口价不能为负数";
        return null;
    }
}

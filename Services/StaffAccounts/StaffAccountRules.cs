using System;
using System.Linq;
using System.Text.RegularExpressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.StaffAccounts;

/// <summary>
/// 员工账号管理的纯规则。一个员工账号同一时间只关联一套手机号 + 微信（social_account_for_job 一行），
/// 工作手机 / 私人手机是那套手机的属性；系统管理员（职级 ≥ 300）可以管理所有账号（含超级管理员），只是不能给自己办离职、不能改自己的职级。
/// </summary>
public static class StaffAccountRules
{
    public const int AdminLevel = 300;
    public static readonly int[] TitleLevels = { 50, 100, 200, 300 };
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(30);
    // 自助登记的待开通记录一年内有效，过期不再出现在「待开通」里
    public static readonly TimeSpan SelfRegLifetime = TimeSpan.FromDays(365);

    public static bool IsAdmin(Staff? staff) => staff is { valid: 1 } && staff.title_level >= AdminLevel;

    public static bool IsTitleAllowed(int level) => TitleLevels.Contains(level);

    public static bool IsValidCell(string? cell) => cell != null && Regex.IsMatch(cell.Trim(), @"^1\d{10}$");

    public static bool IsValidGender(string? gender) => gender == "男" || gender == "女";

    // 雪季：6 月起算下一季，与 StaffController.CreateStaff 一致，如 2026-09-30 → 26-27雪季
    public static string SeasonMemo(DateTime date)
    {
        int start = date.Month >= 6 ? date.Year : date.Year - 1;
        return (start - 2000) + "-" + (start + 1 - 2000) + "雪季";
    }

    public static string NewToken() => Guid.NewGuid().ToString("N");

    public static string CodeStatus(StaffBindCode code, DateTime now) =>
        code.used_date != null ? "used" : code.cancel_date != null ? "cancelled" : code.expire_date > now ? "ok" : "expired";

    // 与 StaffController.GetStaffBySessionKey 的判定一致
    public static bool IsActiveLink(StaffSocialAccount link, DateTime now) =>
        link.valid == 1 && (link.end_date == null || link.end_date >= now);

    // 结束关联的时间：离职日期早于今天就记那天，否则记当前时间
    public static DateTime EndTime(DateTime? date, DateTime now) =>
        date.HasValue && date.Value.Date < now.Date ? date.Value.Date : now;

    // 结束一条关联：同时置 valid=0。登录时 SocialAccountForJob.GetStaff 按日期比较 end_date，只改 end_date 当天仍会被认作员工
    public static void EndLink(StaffSocialAccount link, DateTime endTime, DateTime now)
    {
        link.end_date = endTime;
        link.valid = 0;
        link.update_date = now;
    }
}

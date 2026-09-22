using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Fnb;

/// <summary>Resolve the employee afresh on every request, for either existing client session.</summary>
public sealed class FnbAccess(ApplicationDBContext db)
{
    public sealed record Actor(Staff Staff, string SourceClient, string AuditUserId);

    public static bool CanAccess(Staff? staff, int shopId, bool manager) =>
        staff is { valid: 1 } && staff.base_shop_id == shopId &&
        (!manager || staff.title_level >= 200);

    public async Task<Staff?> ResolveAsync(string? sessionKey) => (await ResolveActorAsync(sessionKey))?.Staff;

    public async Task<Actor?> ResolveActorAsync(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        var key = Util.UrlDecode(sessionKey).Trim();
        var session = await db.miniSession.AsNoTracking()
            .Where(s => s.session_key == key && s.valid == 1 && s.expire_date >= DateTime.Now)
            .Select(s => new { s.session_type, s.wechat_openid })
            .FirstOrDefaultAsync();
        if (session == null) return null;
        var staffController = new StaffController(db);
        Staff? staff = session.session_type switch
        {
            "wecom_userid" when !string.IsNullOrWhiteSpace(session.wechat_openid) =>
                await staffController.GetStaffBySocialNum(session.wechat_openid.Trim(), MemberSocialAccount.TYPE_WECOM),
            "wechat_mini_openid" => await staffController.GetStaffBySessionKey(key),
            _ => null
        };
        if (staff?.valid != 1) return null;
        return session.session_type == "wecom_userid"
            ? new Actor(staff, "wecom", session.wechat_openid!.Trim())
            : new Actor(staff, "mini", $"mini#{staff.id}:{staff.name}");
    }
}

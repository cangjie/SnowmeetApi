using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Controllers.CoachPlatform;

// 教练引流平台三个控制器的公共部分。code：0 成功 / 1 业务规则不允许 / 2 会话失效 / 3 无权限 / 5 这个微信还不是会员
public abstract class CoachApiBase(ApplicationDBContext db) : ControllerBase
{
    protected ApplicationDBContext Db => db;

    protected static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };

    protected async Task<MiniSession?> SessionAsync(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        string key = Util.UrlDecode(sessionKey).Trim();
        return await db.miniSession.AsNoTracking()
            .FirstOrDefaultAsync(s => s.session_key == key && s.session_type == "wechat_mini_openid" && s.valid == 1 && s.expire_date >= DateTime.Now);
    }

    /// <summary>会话对应的会员；没有会员返回 null（新用户还没授权手机号）。</summary>
    protected async Task<Member?> MemberOfAsync(MiniSession session)
    {
        if (session.member_id == null) return null;
        return await db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == session.member_id && m.valid == 1);
    }

    protected static async Task<ApiResult<object>> Run(Func<Task<object?>> action)
    {
        try { return Result(0, "", await action()); }
        catch (CoachException e) { return Result(e.Code, e.Message); }
    }
}

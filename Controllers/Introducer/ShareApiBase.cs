using System;
using System.Threading.Tasks;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Controllers.IntroducerShare;

// 介绍人分账两个控制器的公共部分。code：0 成功 / 1 业务规则不允许 / 2 会话失效 / 3 无权限
public abstract class ShareApiBase(ApplicationDBContext db) : CoachApiBase(db)
{
    protected async Task<ApiResult<object>> AsStaff(string? sessionKey, Func<Staff?, bool> allowed, string denyMessage,
        Func<Staff, Task<object?>> action)
    {
        if (await SessionAsync(sessionKey) == null) return Result(2, "登录已失效，请重新进入小程序");
        Staff? op = await new StaffController(Db).GetStaffBySessionKey(sessionKey!);
        if (!allowed(op)) return Result(3, denyMessage);
        try
        {
            return Result(0, "", await action(op!));
        }
        catch (ShareException e)
        {
            return Result(e.Code, e.Message);
        }
        catch (CoachException e)
        {
            return Result(e.Code, e.Message);
        }
    }
}

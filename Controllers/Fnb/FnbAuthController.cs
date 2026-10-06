using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

public sealed record FnbMe(int StaffId, string Name, int ShopId, string ShopName, bool IsManager,
    string ClientType, string? SessionKey = null);
public sealed record FnbWeComLoginInput(string Code);

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class FnbAuthController(ApplicationDBContext db, IConfiguration config,
    IFnbWeComLoginGateway? gateway = null) : ControllerBase
{
    private static ApiResult<FnbMe> Result(int code, string message, FnbMe? me = null) => new() { code = code, message = message, data = me };
    private async Task<FnbMe?> Identity(Staff staff, string client, string? key = null)
    {
        if (staff.valid != 1 || staff.base_shop_id is not > 0) return null;
        var shop = await db.shop.AsNoTracking().SingleOrDefaultAsync(x => x.id == staff.base_shop_id);
        return shop == null ? null : new FnbMe(staff.id, staff.name, shop.id, shop.name, staff.title_level >= 200, client, key);
    }
    [HttpGet]
    public async Task<ApiResult<FnbMe>> GetMe(string sessionKey)
    {
        try
        {
            var actor = await new FnbAccess(db).ResolveActorAsync(sessionKey);
            if (actor == null) return Result(2, "会话失效或员工已离职");
            var me = await Identity(actor.Staff, actor.SourceClient);
            return me == null ? Result(3, "请管理员为员工配置所属门店") : Result(0, "", me);
        }
        catch (Exception ex) when (FnbV4ControllerBase.HasSqlError(ex, 1205)) { return Result(4, "数据冲突，请重试"); }
    }
    [HttpPost]
    public async Task<ApiResult<FnbMe>> WeComLogin([FromBody] FnbWeComLoginInput input)
    {
        try
        {
            var code = FnbV4Rules.RequiredText(input.Code, 512, "授权 code");
            var userId = await (gateway ?? new FnbWeComLoginGateway(db, config)).ExchangeCodeAsync(code);
            if (string.IsNullOrWhiteSpace(userId)) return Result(1, "仅限企业成员使用");
            var staff = await new StaffController(db).GetStaffBySocialNum(userId, MemberSocialAccount.TYPE_WECOM);
            if (staff?.valid != 1) return Result(3, "仅限在职员工使用，请联系管理员开通");
            string key = Guid.NewGuid().ToString("N"); var me = await Identity(staff, "wecom", key);
            if (me == null) return Result(3, "请管理员为员工配置所属门店");
            db.miniSession.Add(new MiniSession { session_key = key, session_type = "wecom_userid", wechat_openid = userId,
                member_id = null, valid = 1, expire_date = DateTime.Now.AddDays(30), create_date = DateTime.Now });
            await db.SaveChangesAsync(); return Result(0, "", me);
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (Exception ex) when (FnbV4ControllerBase.HasSqlError(ex, 1205)) { return Result(4, "数据冲突，请重试"); }
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Controllers;

// 员工账号管理（小程序 pages/staffadmin）。code：0 成功 / 1 业务规则不允许 / 2 会话失效 / 3 无权限 / 5 这个微信还不是会员
// 管理接口只对系统管理员（职级 ≥ 300）开放；GetBindCode、ConfirmBind、SelfRegister 在被绑定的那部手机上调用，只要求有效的小程序会话
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class StaffAdminController(ApplicationDBContext db) : ControllerBase
{
    private readonly StaffAccountService _service = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };

    public sealed record StaffIdInput(int staff_id);
    public sealed record AccountIdInput(int account_id);
    public sealed record ChangePhoneInput(int staff_id, int account_id);
    public sealed record OffboardInput(int staff_id, DateTime? date);
    public sealed record ApproveInput(int staff_id, int title_level, int? base_shop_id);
    public sealed record CellInput(string? cell);
    public sealed record ConfirmBindInput(string? token, string? encData, string? iv);
    public sealed record SelfRegisterInput(string? name, string? gender, string? encData, string? iv);

    private async Task<MiniSession?> SessionAsync(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        string key = Util.UrlDecode(sessionKey).Trim();
        return await db.miniSession.AsNoTracking()
            .FirstOrDefaultAsync(s => s.session_key == key && s.session_type == "wechat_mini_openid" && s.valid == 1 && s.expire_date >= DateTime.Now);
    }

    private async Task<ApiResult<object>> AsAdmin(string? sessionKey, Func<Staff, Task<object?>> action)
    {
        if (await SessionAsync(sessionKey) == null) return Result(2, "登录已失效，请重新进入小程序");
        Staff? op = await new StaffController(db).GetStaffBySessionKey(sessionKey!);
        if (!StaffAccountRules.IsAdmin(op)) return Result(3, "只有系统管理员可以管理员工账号");
        try { return Result(0, "", await action(op!)); }
        catch (StaffAccountException e) { return Result(e.Code, e.Message); }
    }

    private async Task<ApiResult<object>> AsUser(string? sessionKey, Func<MiniSession, Task<object?>> action)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        try { return Result(0, "", await action(session)); }
        catch (StaffAccountException e) { return Result(e.Code, e.Message); }
    }

    // getPhoneNumber 的加密数据用这个会话的 session_key 解密
    private static string DecryptCell(MiniSession session, string? encData, string? iv)
    {
        try { return WechatPhoneHelper.Decrypt(encData ?? "", iv ?? "", session.session_key); }
        catch { throw new StaffAccountException("手机号授权失败，请重新授权"); }
    }

    [HttpGet]
    public Task<ApiResult<object>> ListStaff(string sessionKey) => AsAdmin(sessionKey, async _ => await _service.ListStaffAsync());

    [HttpGet]
    public Task<ApiResult<object>> GetStaff(string sessionKey, int id) => AsAdmin(sessionKey, async _ => await _service.GetStaffAsync(id));

    [HttpGet]
    public Task<ApiResult<object>> ListPhones(string sessionKey) => AsAdmin(sessionKey, async _ => await _service.ListPhonesAsync());

    [HttpGet]
    public Task<ApiResult<object>> ListShops(string sessionKey) => AsAdmin(sessionKey, async _ => await _service.ListShopsAsync());

    [HttpPost]
    public Task<ApiResult<object>> Onboard(string sessionKey, [FromBody] OnboardInput input) =>
        AsAdmin(sessionKey, async op => await _service.OnboardAsync(op, input));

    [HttpPost]
    public Task<ApiResult<object>> UpdateStaff(string sessionKey, [FromBody] UpdateStaffInput input) =>
        AsAdmin(sessionKey, async op => new { staff_id = await _service.UpdateStaffAsync(op, input) });

    [HttpPost]
    public Task<ApiResult<object>> ChangePhone(string sessionKey, [FromBody] ChangePhoneInput input) =>
        AsAdmin(sessionKey, async op => new { staff_id = await _service.ChangePhoneAsync(op, input.staff_id, input.account_id) });

    [HttpPost]
    public Task<ApiResult<object>> Rebind(string sessionKey, [FromBody] StaffIdInput input) =>
        AsAdmin(sessionKey, async op => await _service.RebindAsync(op, input.staff_id));

    [HttpPost]
    public Task<ApiResult<object>> Offboard(string sessionKey, [FromBody] OffboardInput input) =>
        AsAdmin(sessionKey, async op => new { staff_id = await _service.OffboardAsync(op, input.staff_id, input.date) });

    [HttpPost]
    public Task<ApiResult<object>> Approve(string sessionKey, [FromBody] ApproveInput input) =>
        AsAdmin(sessionKey, async op => new { staff_id = await _service.ApproveAsync(op, input.staff_id, input.title_level, input.base_shop_id) });

    [HttpPost]
    public Task<ApiResult<object>> Reject(string sessionKey, [FromBody] StaffIdInput input) =>
        AsAdmin(sessionKey, async op => new { staff_id = await _service.RejectAsync(op, input.staff_id) });

    [HttpPost]
    public Task<ApiResult<object>> ReclaimPhone(string sessionKey, [FromBody] AccountIdInput input) =>
        AsAdmin(sessionKey, async op => new { account_id = await _service.ReclaimPhoneAsync(op, input.account_id) });

    [HttpPost]
    public Task<ApiResult<object>> AddJobPhone(string sessionKey, [FromBody] CellInput input) =>
        AsAdmin(sessionKey, async op => await _service.AddJobPhoneAsync(op, input.cell));

    [HttpPost]
    public Task<ApiResult<object>> BindJobPhone(string sessionKey, [FromBody] AccountIdInput input) =>
        AsAdmin(sessionKey, async op => await _service.BindJobPhoneAsync(op, input.account_id));

    [HttpGet]
    public Task<ApiResult<object>> GetBindCode(string sessionKey, string token) =>
        AsUser(sessionKey, async _ => await _service.GetCodeAsync(token));

    [HttpPost]
    public Task<ApiResult<object>> ConfirmBind(string sessionKey, [FromBody] ConfirmBindInput input) =>
        AsUser(sessionKey, async session =>
        {
            string cell = DecryptCell(session, input.encData, input.iv);
            return new { staff_id = await _service.ConfirmBindAsync(input.token, session.wechat_openid, cell) };
        });

    [HttpPost]
    public Task<ApiResult<object>> SelfRegister(string sessionKey, [FromBody] SelfRegisterInput input) =>
        AsUser(sessionKey, async session =>
        {
            string cell = DecryptCell(session, input.encData, input.iv);
            return new { staff_id = await _service.SelfRegisterAsync(session.wechat_openid, cell, input.name, input.gender) };
        });
}

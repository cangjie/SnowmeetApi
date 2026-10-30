using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 会员管理、会员明细页面对所有店员开放（2026-10-30 用户要求）：职级 ≥ 100 的店员可用，万龙对账（50）仍不可用；
/// 会员合并仍只给系统管理员（≥ 300），储值账户查询两个页面仍是店长以上（≥ 200）。
/// </summary>
public class MemberAdminAccessTests
{
    // 会员管理 / 会员明细页面用到的全部接口（原先 200，现在 100）
    private static readonly string[] OpenToStaff =
    {
        "SearchMembersByStaff", "GetMemberDetailByStaff", "UpdateMemberProfile", "AddMemberTag", "RemoveMemberTag",
        "GetTagLibrary", "GetTagLibraryWithStats", "MergeTagPreset", "DeleteTagPreset", "AddTagPreset",
        "RegisterMemberByPhone", "ChargeMemberDeposit", "GetPunchCardPresets", "GrantPunchCard", "GetCouponTemplates", "GrantCoupon",
    };

    private static MemberAdminController Controller(CoachFixture f) => new(f.NewContext(), f.Config, new HttpContextAccessor());

    // 只关心权限闸：其余参数一律传默认值 / 空值，业务校验不通过没关系，只要不是「没有权限」
    private static async Task<ApiResult<object>> Call(MemberAdminController c, string method, string sessionKey)
    {
        MethodInfo m = typeof(MemberAdminController).GetMethod(method)!;
        object?[] args = m.GetParameters().Select(p =>
            p.Name == "sessionKey" ? sessionKey
            : p.HasDefaultValue ? p.DefaultValue
            : p.ParameterType == typeof(int) ? 0
            : p.ParameterType == typeof(string) ? ""
            : null).ToArray();
        var task = (Task)m.Invoke(c, args)!;
        await task;
        object? action = task.GetType().GetProperty("Result")!.GetValue(task);
        object? ok = action!.GetType().GetProperty("Result")!.GetValue(action);
        return (ApiResult<object>)((OkObjectResult)ok!).Value!;
    }

    private static async Task<CoachFixture> WithLevel50()
    {
        var f = await CoachFixture.Create();
        f.Db.staff.Add(new Staff { id = 11, name = "万龙对账", title_level = 50, valid = 1 });
        f.Db.socialAccountForJob.Add(new SocialAccountForJob { id = 11, member_id = CoachFixture.StudentC, wechat_mini_openid = "mini-5" });
        f.Db.staffSocialAccount.Add(new StaffSocialAccount { staff_id = 11, social_account_id = 11, valid = 1, start_date = DateTime.Now.AddDays(-2) });
        await f.Db.SaveChangesAsync();
        return f;
    }

    [Fact]
    public async Task EveryClerk_CanUseTheMemberManagementEndpoints()
    {
        await using var f = await CoachFixture.Create();
        foreach (string method in OpenToStaff)
        {
            var r = await Call(Controller(f), method, "session-7");   // 职级 100 的店员
            Assert.True(!r.message.Contains("没有权限"), method + " 仍然拦住了店员：" + r.message);
        }
    }

    [Fact]
    public async Task Clerk_CanSearchAndOpenMemberDetail()
    {
        await using var f = await CoachFixture.Create();
        static ApiResult<object> Env(ActionResult<ApiResult<object>> a) => Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(a.Result).Value);
        Assert.Equal(0, Env(await Controller(f).SearchMembersByStaff("session-7")).code);
        Assert.Equal(0, Env(await Controller(f).GetMemberDetailByStaff(CoachFixture.StudentA, "session-7")).code);
    }

    [Fact]
    public async Task WanlongReconciliationLevel50_StillBlocked_AndInvalidSessionToo()
    {
        await using var f = await WithLevel50();
        foreach (string method in OpenToStaff)
        {
            var r = await Call(Controller(f), method, "session-5");   // 职级 50
            Assert.Equal("没有权限", r.message);
        }
        // 学员（不是员工）和无效会话
        Assert.Equal("没有权限", (await Call(Controller(f), "SearchMembersByStaff", "session-3")).message);
        Assert.Equal("没有权限", (await Call(Controller(f), "SearchMembersByStaff", "nope")).message);
    }

    [Fact]
    public async Task MergeStaysSystemAdminOnly_AndDepositAccountPagesStayManagerLevel()
    {
        await using var f = await CoachFixture.Create();
        Assert.Equal("没有权限", (await Call(Controller(f), "MergeMemberByStaff", "session-7")).message);
        Assert.Equal("没有权限", (await Call(Controller(f), "SearchDepositAccountsByStaff", "session-7")).message);
        Assert.Equal("没有权限", (await Call(Controller(f), "GetDepositAccountDetailByStaff", "session-7")).message);
        // 系统管理员（300）通得过闸（后面的业务校验另说）
        Assert.NotEqual("没有权限", (await Call(Controller(f), "MergeMemberByStaff", "session-1")).message);
        Assert.NotEqual("没有权限", (await Call(Controller(f), "SearchDepositAccountsByStaff", "session-1")).message);
    }
}

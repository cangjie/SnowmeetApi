using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>日养护工作列表接口：只有店员能看；日期不对 / 还没到的日期给出提示；不传日期就是今天。</summary>
public class CareWorkloadControllerTests
{
    private static ApiResult<CareWorkloadStat> Unwrap(ActionResult<ApiResult<CareWorkloadStat>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<ApiResult<CareWorkloadStat>>(ok.Value);
    }

    [Fact]
    public async Task OnlyStaffCanRead()
    {
        await using var f = await CoachFixture.Create();
        var controller = new CareWorkloadController(f.NewContext());
        var customer = Unwrap(await controller.GetDailyWorkloadByStaff(null, null, "session-3"));      // 学员：不是店员
        Assert.Equal((1, "没有权限"), (customer.code, customer.message));
        var nobody = Unwrap(await controller.GetDailyWorkloadByStaff(null, null, "no-such-session"));
        Assert.Equal(1, nobody.code);
        var clerk = Unwrap(await controller.GetDailyWorkloadByStaff(null, null, "session-7"));           // 普通店员（职级 100）
        Assert.Equal(0, clerk.code);
        Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), clerk.data!.date);
    }

    [Fact]
    public async Task RejectsBadOrFutureDates_AndAcceptsPastDatesAndShops()
    {
        await using var f = await CoachFixture.Create();
        var controller = new CareWorkloadController(f.NewContext());
        Assert.Equal("日期格式不对", Unwrap(await controller.GetDailyWorkloadByStaff("不是日期", null, "session-7")).message);
        Assert.Equal("不能查询还没到的日期",
            Unwrap(await controller.GetDailyWorkloadByStaff(DateTime.Now.AddDays(1).ToString("yyyy-MM-dd"), null, "session-7")).message);
        var past = Unwrap(await controller.GetDailyWorkloadByStaff("2026-11-10", "万龙服务中心", "session-7"));
        Assert.Equal(0, past.code);
        Assert.Equal(("2026-11-10", "万龙服务中心"), (past.data!.date, past.data.shop));
        Assert.Equal(new DateTime(2026, 11, 10, 22, 0, 0), past.data.asOf);
    }
}

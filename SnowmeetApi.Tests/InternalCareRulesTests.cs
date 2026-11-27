using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class InternalCareRulesTests
{
    private static CareTask Step(string name, string status, int valid = 1) =>
        new() { task_name = name, status = status, valid = valid };

    [Fact]
    public void CompleteOnlyWhenAllValidStepsAreComplete()
    {
        Assert.Equal("未开始", InternalCareRules.Status(new[] { Step("安全检查", "未开始"), Step("修刃", "未开始") }));
        Assert.Equal("进行中", InternalCareRules.Status(new[] { Step("安全检查", "已完成"), Step("修刃", "未开始") }));
        Assert.Equal("已完成", InternalCareRules.Status(new[] { Step("安全检查", "已完成"), Step("修刃", "已完成"), Step("维修", "未开始", 0) }));
        Assert.Equal("未开始", InternalCareRules.Status(Array.Empty<CareTask>()));
    }

    [Fact]
    public void TerminatedStepDoesNotSilentlyCompleteEquipment()
    {
        Assert.Equal("进行中", InternalCareRules.Status(new[] { Step("安全检查", "已完成"), Step("修刃", "强行中止") }));
    }

    [Fact]
    public void InternalCareCompletesWithoutPickupButCustomerCareStillNeedsPickup()
    {
        var steps = new List<CareTask> { Step("安全检查", "已完成"), Step("修刃", "已完成") };
        Assert.Equal("已完成", new Care { equipment = "单板", biz_type = InternalCareRules.OrderType, tasks = steps }.status);
        Assert.Equal("进行中", new Care { equipment = "单板", biz_type = "养护", tasks = steps }.status);
    }
}

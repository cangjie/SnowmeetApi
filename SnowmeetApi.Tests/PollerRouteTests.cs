using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Services.SkipassJobs;

namespace SnowmeetApi.Tests;

/// <summary>
/// 后台轮询任务请求的是本机自己的接口，地址写错了只会在日志里静静地返回 404，没人发现。
/// 2026-11-03：退款状态同步的默认地址写成了 /core/Tenpay/…，而 TenpayController 的路由是 api/[controller]/[action]，
/// 自上线起每 5 分钟都 404，任务从来没起过作用。这里把轮询的默认地址和控制器真实的路由对上。
/// </summary>
public class PollerRouteTests
{
    private static string RoutePath(Type controller, string controllerName, string action)
    {
        string template = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        return "/" + template.Replace("[controller]", controllerName).Replace("[action]", action);
    }

    [Fact]
    public void RefundStateSync_DefaultUrl_IsTheRealTenpayRoute()
    {
        var uri = new Uri(RefundStateSyncPoller.DefaultUrl);
        MethodInfo? action = typeof(TenpayController).GetMethod("SyncPendingRefunds");
        Assert.NotNull(action);
        Assert.Equal(RoutePath(typeof(TenpayController), "Tenpay", "SyncPendingRefunds"), uri.AbsolutePath);
        Assert.Contains(action!.GetParameters(), p => p.Name == "days");              // 地址里的 days=3 是接口真有的参数
        Assert.Contains("days=", uri.Query);
    }

    [Fact]
    public void SkipassPoller_DefaultBase_AndEveryCall_AreRealSkiPassActions()
    {
        var baseUri = new Uri(SkipassCancelPoller.DefaultBase);
        Assert.Equal(RoutePath(typeof(SkiPassController), "SkiPass", "").TrimEnd('/') + "/", baseUri.AbsolutePath);
        foreach (string action in new[] { "RefreshCancel", "RefreshAutoReserve", "RefreshIssuing", "RefreshUsedAll" })
        {
            Assert.NotNull(typeof(SkiPassController).GetMethod(action));
        }
    }
}

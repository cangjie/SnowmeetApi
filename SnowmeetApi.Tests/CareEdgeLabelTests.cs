using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 修刃角度与标签存根（用户 2026-11-25 要求）：
/// 修刃开始之前角度都可以改；改了角度就必须重新打印标签，不重新打印不能点「开始修刃」。
/// 状态记在 care.label_edge_degree（标签上印的角度）：NULL = 没有待重打的改动。
/// </summary>
public class CareEdgeLabelTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static CareController Ctl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private const string Clerk = "session-7";      // 店员（职级 100）
    private const string Customer = "session-3";   // 普通顾客，不是员工

    private static ApiResult<Care?> Result(ActionResult<ApiResult<Care?>> r) => (ApiResult<Care?>)((OkObjectResult)r.Result!).Value!;

    // ---------- 纯规则 ----------

    [Theory]
    [InlineData(null, null, false)]       // 没有待重打的改动
    [InlineData(null, "88", false)]
    [InlineData("89", "89", false)]       // 改回标签上的角度：不用重打
    [InlineData("89", "88", true)]
    [InlineData("89", null, false)]       // 角度为空标签印 89，和标签一致
    [InlineData("88", null, true)]        // 标签 88，现在空了（标签会印 89）：不一致
    [InlineData(" 89 ", "89", false)]
    public void IsPending(string? label, string? current, bool expected)
        => Assert.Equal(expected, CareEdgeLabelRules.IsPending(label, current));

    [Theory]
    [InlineData("88", "88")]
    [InlineData(" 88 ", "88")]
    [InlineData("88.5", "88.5")]
    [InlineData("89.0", "89")]
    [InlineData("90", "90")]
    [InlineData("0.5", "0.5")]
    [InlineData("5", "5")]
    public void ParseAngle_Valid(string input, string expected)
    {
        Assert.True(CareEdgeLabelRules.TryParseAngle(input, out var normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("91")]
    [InlineData("100")]
    [InlineData("-5")]
    [InlineData("88.55")]
    [InlineData("8 8")]
    [InlineData("88度")]
    public void ParseAngle_Invalid(string? input)
    {
        Assert.False(CareEdgeLabelRules.TryParseAngle(input, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ---------- 数据准备 ----------

    /// <summary>已生效的一件装备：安全检查已完成、修刃按给定状态、发板未开始。返回 (careId, 修刃任务 id)。</summary>
    private static async Task<(int careId, int edgeTaskId)> EdgeCare(CoachFixture f, string? edge = "89", string? label = null,
        string edgeStatus = "未开始", int needEdge = 1, bool cancelled = false, bool withEdgeTask = true)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = "崇礼旗舰店", member_id = CoachFixture.StudentA, valid = 1, staff_id = 9, code = "YH_E_" + Guid.NewGuid().ToString("N")[..6] };
        db.order.Add(order); await db.SaveChangesAsync();
        var care = new Care { order_id = order.id, equipment = "双板", brand = "品牌", scale = "170", need_edge = needEdge, edge_degree = edge,
            label_edge_degree = label, need_wax = 1, need_unwax = 1, biz_type = "雪季养护", valid = 1, is_cancel = cancelled, task_flow_code = "WF_TEST" };
        db.care.Add(care); await db.SaveChangesAsync();
        db.careTask.Add(new CareTask { care_id = care.id, task_name = "安全检查", sort = 10, status = "已完成", valid = 1 });
        int edgeId = 0;
        if (withEdgeTask)
        {
            var t = new CareTask { care_id = care.id, task_name = "修刃", sort = 20, status = edgeStatus, valid = 1 };
            db.careTask.Add(t); await db.SaveChangesAsync(); edgeId = t.id;
        }
        db.careTask.Add(new CareTask { care_id = care.id, task_name = "热蜡", sort = 40, status = "未开始", valid = 1 });
        db.careTask.Add(new CareTask { care_id = care.id, task_name = "发板", sort = 60, status = "未开始", valid = 1 });
        await db.SaveChangesAsync();
        return (care.id, edgeId);
    }

    private static Task<Care> Reload(CoachFixture f, int careId) => f.NewContext().care.AsNoTracking().SingleAsync(c => c.id == careId);
    private static Task<CareTask> Task_(CoachFixture f, int id) => f.NewContext().careTask.AsNoTracking().SingleAsync(t => t.id == id);

    // ---------- 改角度 ----------

    [Fact]
    public async Task ChangeAngle_RecordsThePrintedAngle_AndMarksLabelPending()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        var r = Result(await Ctl(f).SetEdgeDegree(careId, "88", Clerk));
        Assert.Equal(0, r.code);
        var care = await Reload(f, careId);
        Assert.Equal("88", care.edge_degree);
        Assert.Equal("89", care.label_edge_degree);        // 标签上印的还是 89
        Assert.True(care.edgeLabelPending);
        Assert.True(r.data!.edgeLabelPending);
    }

    [Fact]
    public async Task ChangeAngle_WhenAngleWasEmpty_LabelHadDefault89()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, edge: null);
        Assert.Equal(0, Result(await Ctl(f).SetEdgeDegree(careId, "88", Clerk)).code);
        var care = await Reload(f, careId);
        Assert.Equal(("88", "89"), (care.edge_degree, care.label_edge_degree));
    }

    [Fact]
    public async Task ChangeAngleSeveralTimes_KeepsTheOriginalPrintedAngle()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        await Ctl(f).SetEdgeDegree(careId, "87", Clerk);
        var care = await Reload(f, careId);
        Assert.Equal(("87", "89"), (care.edge_degree, care.label_edge_degree));
        Assert.True(care.edgeLabelPending);
    }

    [Fact]
    public async Task ChangeBackToThePrintedAngle_NoLongerPending()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        await Ctl(f).SetEdgeDegree(careId, "89", Clerk);
        var care = await Reload(f, careId);
        Assert.Equal("89", care.edge_degree);
        Assert.False(care.edgeLabelPending);
    }

    [Fact]
    public async Task SameAngle_ChangesNothing_AndWritesNoLog()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        var r = Result(await Ctl(f).SetEdgeDegree(careId, "89.0", Clerk));
        Assert.Equal(0, r.code);
        var care = await Reload(f, careId);
        Assert.Equal(("89", (string?)null), (care.edge_degree, care.label_edge_degree));
        Assert.Empty(await f.NewContext().coreDataModLog.Where(l => l.table_name == "care" && l.field_name == "edge_degree").ToListAsync());
    }

    [Fact]
    public async Task ChangeAngle_WritesAuditLog()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        var log = Assert.Single(await f.NewContext().coreDataModLog.Where(l => l.table_name == "care" && l.field_name == "edge_degree").ToListAsync());
        Assert.Equal(careId, log.key_value);
        Assert.Equal(("89", "88"), (log.prev_value, log.current_value));
        Assert.Equal(10, log.staff_id);
    }

    [Theory]
    [InlineData("已开始")]
    [InlineData("已完成")]
    [InlineData("强行中止")]
    public async Task ChangeAngle_RejectedOnceEdgeTaskHasStarted(string status)
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89", edgeStatus: status);
        var r = Result(await Ctl(f).SetEdgeDegree(careId, "88", Clerk));
        Assert.Equal(1, r.code);
        Assert.Contains("修刃已经开始", r.message);
        var care = await Reload(f, careId);
        Assert.Equal(("89", (string?)null), (care.edge_degree, care.label_edge_degree));
    }

    [Fact]
    public async Task ChangeAngle_RejectedWithoutEdgeTask_OrWhenCancelled()
    {
        await using var f = await CoachFixture.Create();
        var (noEdge, _) = await EdgeCare(f, "89", needEdge: 0, withEdgeTask: false);
        Assert.Equal(1, Result(await Ctl(f).SetEdgeDegree(noEdge, "88", Clerk)).code);
        var (cancelled, _) = await EdgeCare(f, "89", cancelled: true);
        Assert.Equal(1, Result(await Ctl(f).SetEdgeDegree(cancelled, "88", Clerk)).code);
        Assert.Equal("89", (await Reload(f, cancelled)).edge_degree);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("95")]
    public async Task ChangeAngle_InvalidInput_Rejected(string degree)
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        Assert.Equal(1, Result(await Ctl(f).SetEdgeDegree(careId, degree, Clerk)).code);
        Assert.Equal("89", (await Reload(f, careId)).edge_degree);
    }

    [Fact]
    public async Task ChangeAngle_RequiresStaff()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        var r = Result(await Ctl(f).SetEdgeDegree(careId, "88", Customer));
        Assert.Equal(1, r.code);
        Assert.Equal("没有权限", r.message);
        Assert.Equal("89", (await Reload(f, careId)).edge_degree);
    }

    // ---------- 重新打印标签 ----------

    [Fact]
    public async Task MarkPrinted_ClearsPending()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        var r = Result(await Ctl(f).MarkEdgeLabelPrinted(careId, Clerk, degree: "88"));
        Assert.Equal(0, r.code);
        var care = await Reload(f, careId);
        Assert.Null(care.label_edge_degree);
        Assert.False(care.edgeLabelPending);
        Assert.Equal("88", care.edge_degree);
        var logs = await f.NewContext().coreDataModLog.Where(l => l.table_name == "care" && l.field_name == "label_edge_degree").ToListAsync();
        Assert.Single(logs);
    }

    [Fact]
    public async Task MarkPrinted_WithoutDegree_AlsoClears()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        Assert.Equal(0, Result(await Ctl(f).MarkEdgeLabelPrinted(careId, Clerk)).code);
        Assert.False((await Reload(f, careId)).edgeLabelPending);
    }

    [Fact]
    public async Task MarkPrinted_WithStaleAngle_Rejected_AndStaysPending()
    {
        // 页面打印的是旧角度 88，打印期间角度又被改成 87：这张标签不算数
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "87", Clerk);
        var r = Result(await Ctl(f).MarkEdgeLabelPrinted(careId, Clerk, degree: "88"));
        Assert.Equal(1, r.code);
        Assert.True((await Reload(f, careId)).edgeLabelPending);
    }

    [Fact]
    public async Task MarkPrinted_WhenNothingPending_IsAHarmlessNoOp()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        Assert.Equal(0, Result(await Ctl(f).MarkEdgeLabelPrinted(careId, Clerk, degree: "89")).code);
        Assert.Empty(await f.NewContext().coreDataModLog.Where(l => l.field_name == "label_edge_degree").ToListAsync());
    }

    [Fact]
    public async Task MarkPrinted_RequiresStaff()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        Assert.Equal(1, Result(await Ctl(f).MarkEdgeLabelPrinted(careId, Customer)).code);
        Assert.True((await Reload(f, careId)).edgeLabelPending);
    }

    // ---------- 开始修刃的拦截 ----------

    [Fact]
    public async Task StartEdge_Blocked_WhileLabelNotReprinted()
    {
        await using var f = await CoachFixture.Create();
        var (careId, edgeTask) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        var r = Result(await Ctl(f).SetTaskStatus(edgeTask, "已开始", "养护详情页", Clerk));
        Assert.Equal(1, r.code);
        Assert.Contains("重新打印标签", r.message);
        var task = await Task_(f, edgeTask);
        Assert.Equal("未开始", task.status);
        Assert.Null(task.start_time);
    }

    [Fact]
    public async Task StartEdge_Allowed_AfterLabelReprinted()
    {
        await using var f = await CoachFixture.Create();
        var (careId, edgeTask) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        await Ctl(f).MarkEdgeLabelPrinted(careId, Clerk, degree: "88");
        var r = Result(await Ctl(f).SetTaskStatus(edgeTask, "已开始", "养护详情页", Clerk));
        Assert.Equal(0, r.code);
        var task = await Task_(f, edgeTask);
        Assert.Equal("已开始", task.status);
        Assert.NotNull(task.start_time);
    }

    [Fact]
    public async Task StartEdge_Allowed_WhenAngleNeverChanged()
    {
        // 存量数据 label_edge_degree 全是 NULL：照常开始，不受新规则影响
        await using var f = await CoachFixture.Create();
        var (_, edgeTask) = await EdgeCare(f, "89");
        Assert.Equal(0, Result(await Ctl(f).SetTaskStatus(edgeTask, "已开始", "养护详情页", Clerk)).code);
        Assert.Equal("已开始", (await Task_(f, edgeTask)).status);
    }

    [Fact]
    public async Task StartEdge_Allowed_WhenAngleChangedBackToThePrintedOne()
    {
        await using var f = await CoachFixture.Create();
        var (careId, edgeTask) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        await Ctl(f).SetEdgeDegree(careId, "89", Clerk);
        Assert.Equal(0, Result(await Ctl(f).SetTaskStatus(edgeTask, "已开始", "养护详情页", Clerk)).code);
    }

    [Fact]
    public async Task OtherSteps_CanStartWhileLabelPending_OnlyEdgeIsBlocked()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        var wax = await f.NewContext().careTask.AsNoTracking().SingleAsync(t => t.care_id == careId && t.task_name == "热蜡");
        Assert.Equal(0, Result(await Ctl(f).SetTaskStatus(wax.id, "已开始", "养护详情页", Clerk)).code);
    }

    [Fact]
    public async Task OnlyStartingIsBlocked_NotTheLaterTransitions()
    {
        // 已经开始的修刃（比如旧版小程序在改角度规则之前开始的）不能因为待重打而结束不了
        await using var f = await CoachFixture.Create();
        var (careId, edgeTask) = await EdgeCare(f, "89", label: "88", edgeStatus: "已开始");
        Assert.Equal(0, Result(await Ctl(f).SetTaskStatus(edgeTask, "已完成", "养护详情页", Clerk)).code);
    }

    // ---------- 整份回传不能把「待重打」冲掉 ----------

    [Fact]
    public async Task WholeRowUpdate_DoesNotOverwriteLabelEdgeDegree()
    {
        await using var f = await CoachFixture.Create();
        var (careId, _) = await EdgeCare(f, "89");
        await Ctl(f).SetEdgeDegree(careId, "88", Clerk);
        // 页面上较早读到的装备（没有待重打标志）整份回传保存装备信息
        var stale = await Reload(f, careId);
        stale.label_edge_degree = null;
        stale.memo = "备注改了";
        await Ctl(f).UpdateCare(stale, null, 10, "养护订单详情页修改装备信息");
        var care = await Reload(f, careId);
        Assert.Equal("备注改了", care.memo);
        Assert.Equal("89", care.label_edge_degree);
        Assert.True(care.edgeLabelPending);
    }
}

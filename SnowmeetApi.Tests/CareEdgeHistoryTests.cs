using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 修刃角度「修改历史」（用户 2026-11-25 要求）：养护订单详情页能看到这件装备的角度改过几次、
/// 谁在什么时候从几度改成几度，以及重新打印过几次标签存根。
/// 记录来自审计日志 core_data_mod_log：改角度写 care.edge_degree，重新打印写 care.label_edge_degree（清空）。
/// </summary>
public class CareEdgeHistoryTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static CareController Ctl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private const string Clerk = "session-7";      // 店员（职级 100，staff 10「店员」）
    private const string Customer = "session-3";   // 普通顾客，不是员工

    private static async Task<int> NewCare(CoachFixture f, string? edge = "89", bool withEdgeTask = true)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = "崇礼旗舰店", member_id = CoachFixture.StudentA, valid = 1, staff_id = 9, code = "YH_H_" + Guid.NewGuid().ToString("N")[..6] };
        db.order.Add(order); await db.SaveChangesAsync();
        var care = new Care { order_id = order.id, equipment = "双板", brand = "品牌", scale = "170", need_edge = 1, edge_degree = edge,
            need_wax = 1, need_unwax = 1, biz_type = "雪季养护", valid = 1, task_flow_code = "WF_TEST" };
        db.care.Add(care); await db.SaveChangesAsync();
        if (withEdgeTask) db.careTask.Add(new CareTask { care_id = care.id, task_name = "修刃", sort = 20, status = "未开始", valid = 1 });
        await db.SaveChangesAsync();
        return care.id;
    }

    private static async Task<ApiResult<List<CareEdgeHistoryItem>?>> History(CoachFixture f, int careId, string session = Clerk)
    {
        var r = await Ctl(f).GetEdgeHistory(careId, session);
        return (ApiResult<List<CareEdgeHistoryItem>?>)((OkObjectResult)r.Result!).Value!;
    }

    private static CoreDataModLog Log(int careId, string field, string? prev, string? curr, int? staffId, DateTime at,
        string table = "care", string scene = "测试")
        => new() { table_name = table, field_name = field, key_value = careId, scene = scene, staff_id = staffId,
            prev_value = prev, current_value = curr, trace_id = at.Ticks, create_date = at };

    private static async Task Seed(CoachFixture f, params CoreDataModLog[] logs)
    {
        var db = f.NewContext();
        db.coreDataModLog.AddRange(logs);
        await db.SaveChangesAsync();
    }

    // ---------- 纯规则（Build） ----------

    [Fact]
    public void Build_NoLogs_IsEmpty()
        => Assert.Empty(CareEdgeHistoryRules.Build("89", new List<CoreDataModLog>()));

    [Fact]
    public void Build_ShowsNewestFirst_WithWhoAndFromTo()
    {
        var t0 = new DateTime(2026, 11, 25, 10, 0, 0);
        var logs = new List<CoreDataModLog>
        {
            Log(1, "edge_degree", "89", "88", 10, t0),
            Log(1, "label_edge_degree", "89", null, 9, t0.AddMinutes(2)),
            Log(1, "edge_degree", "88", "87", 9, t0.AddMinutes(5)),
        };
        logs[0].staff = new Staff { id = 10, name = "店员" };
        logs[1].staff = new Staff { id = 9, name = "管理员" };
        logs[2].staff = new Staff { id = 9, name = "管理员" };
        var items = CareEdgeHistoryRules.Build("87", logs);
        Assert.Equal(3, items.Count);
        Assert.Equal(("angle", "88", "87", "管理员"), (items[0].kind, items[0].from, items[0].to, items[0].staffName));
        Assert.Equal(("print", "89", "88", "管理员"), (items[1].kind, items[1].from, items[1].to, items[1].staffName));   // 打印时角度是 88，换下的旧标签是 89
        Assert.Equal(("angle", "89", "88", "店员"), (items[2].kind, items[2].from, items[2].to, items[2].staffName));
        Assert.Equal(new[] { t0.AddMinutes(5), t0.AddMinutes(2), t0 }, items.Select(i => i.time).ToArray());
    }

    [Fact]
    public void Build_PrintRow_CarriesTheAngleThatWasPrinted()
    {
        var t0 = new DateTime(2026, 11, 25, 10, 0, 0);
        var logs = new List<CoreDataModLog>
        {
            Log(1, "edge_degree", "89", "88", 10, t0),
            Log(1, "edge_degree", "88", "87", 10, t0.AddMinutes(1)),
            Log(1, "label_edge_degree", "89", "null", 10, t0.AddMinutes(2)),   // 服务器写的清空是字符串 "null"
            Log(1, "edge_degree", "87", "86", 10, t0.AddMinutes(3)),
        };
        var items = CareEdgeHistoryRules.Build("86", logs);
        var print = Assert.Single(items, i => i.kind == "print");
        Assert.Equal("87", print.to);        // 打印那一刻的角度是 87，不是后来的 86，也不是最早的 89
        Assert.Equal("89", print.from);      // 换下来的旧标签上是 89
    }

    [Fact]
    public void Build_ThePrintedAngleBeforeAnyChange_UsesTheInitialAngle()
    {
        // 没有任何改角度日志、只有一条重打日志（不应出现，但别出错）：打的就是当前角度
        var logs = new List<CoreDataModLog> { Log(1, "label_edge_degree", "89", null, 10, new DateTime(2026, 11, 25, 10, 0, 0)) };
        var print = Assert.Single(CareEdgeHistoryRules.Build("88", logs));
        Assert.Equal(("print", "88"), (print.kind, print.to));
    }

    [Theory]
    [InlineData(null, "88", "89", "88")]        // 之前没填角度，标签印 89
    [InlineData("null", "88", "89", "88")]      // CreateCoreDataModLog 把空写成字符串 "null"
    [InlineData("", "88", "89", "88")]
    [InlineData("88", null, "88", "89")]        // 改成空 = 回到默认 89
    [InlineData("88", "null", "88", "89")]
    public void Build_EmptyAngleCountsAs89(string? prev, string? curr, string from, string to)
    {
        var items = CareEdgeHistoryRules.Build(to, new List<CoreDataModLog> { Log(1, "edge_degree", prev, curr, 10, DateTime.Now) });
        var item = Assert.Single(items);
        Assert.Equal((from, to), (item.from, item.to));
    }

    [Theory]
    [InlineData(null, "89")]
    [InlineData("null", "89")]
    [InlineData("89", "89.0")]      // 写法不同、其实没变
    public void Build_SkipsChangesThatChangeNothing(string? prev, string? curr)
        => Assert.Empty(CareEdgeHistoryRules.Build("89", new List<CoreDataModLog> { Log(1, "edge_degree", prev, curr, 10, DateTime.Now) }));

    [Fact]
    public void Build_CapitalCareTable_FromWholeRowSave_AlsoCounts()
    {
        // UpdateCare 整行保存时写的日志 table_name 是类名 Care，空是真 null
        var items = CareEdgeHistoryRules.Build("88", new List<CoreDataModLog> { Log(1, "edge_degree", "89", "88", 10, DateTime.Now, table: "Care") });
        Assert.Equal(("angle", "89", "88"), (Assert.Single(items).kind, items[0].from, items[0].to));
    }

    [Fact]
    public void Build_IgnoresOtherTablesAndOtherFields()
    {
        var logs = new List<CoreDataModLog>
        {
            Log(1, "edge_degree", "89", "88", 10, DateTime.Now, table: "care_task"),
            Log(1, "status", "未开始", "已开始", 10, DateTime.Now),
            Log(1, "label_edge_degree", null, "89", 10, DateTime.Now),   // 第一次改角度时记下标签角度：不算一次打印
        };
        Assert.Empty(CareEdgeHistoryRules.Build("89", logs));
    }

    [Fact]
    public void Build_NoStaff_IsMarkedUnrecorded_AndStaffWithoutNameShowsId()
    {
        var logs = new List<CoreDataModLog>
        {
            Log(1, "edge_degree", "89", "88", null, new DateTime(2026, 11, 25, 10, 0, 0)),
            Log(1, "edge_degree", "88", "87", 77, new DateTime(2026, 11, 25, 10, 1, 0)),
        };
        var items = CareEdgeHistoryRules.Build("87", logs);
        Assert.Equal("员工#77", items[0].staffName);
        Assert.Equal("未记录", items[1].staffName);
        Assert.Null(items[1].staffId);
    }

    [Fact]
    public void Build_SameTimestamp_KeepsLogIdOrder()
    {
        var at = new DateTime(2026, 11, 25, 10, 0, 0);
        var a = Log(1, "edge_degree", "89", "88", 10, at); a.id = 1;
        var b = Log(1, "edge_degree", "88", "87", 10, at); b.id = 2;
        var items = CareEdgeHistoryRules.Build("87", new List<CoreDataModLog> { b, a });
        Assert.Equal(new[] { "87", "88" }, items.Select(i => i.to).ToArray());     // 新的在前：id 大的在前
    }

    // ---------- 接口 ----------

    [Fact]
    public async Task Endpoint_NonStaff_IsRefused()
    {
        await using var f = await CoachFixture.Create();
        var careId = await NewCare(f);
        var r = await History(f, careId, Customer);
        Assert.Equal(1, r.code);
        Assert.Equal("没有权限", r.message);
        Assert.Null(r.data);
    }

    [Fact]
    public async Task Endpoint_UnknownCare_ReportsNotFound()
    {
        await using var f = await CoachFixture.Create();
        var r = await History(f, 999999);
        Assert.Equal(1, r.code);
        Assert.Equal("未找到养护装备", r.message);
    }

    [Fact]
    public async Task Endpoint_NeverChanged_IsEmptyList()
    {
        await using var f = await CoachFixture.Create();
        var careId = await NewCare(f);
        var r = await History(f, careId);
        Assert.Equal(0, r.code);
        Assert.Empty(r.data!);
    }

    [Fact]
    public async Task Endpoint_ShowsRealChangesAndReprints_FromTheRealActions()
    {
        await using var f = await CoachFixture.Create();
        var careId = await NewCare(f, "89");
        Assert.Equal(0, ((ApiResult<Care?>)((OkObjectResult)(await Ctl(f).SetEdgeDegree(careId, "88", Clerk)).Result!).Value!).code);
        Assert.Equal(0, ((ApiResult<Care?>)((OkObjectResult)(await Ctl(f).SetEdgeDegree(careId, "87", Clerk)).Result!).Value!).code);
        Assert.Equal(0, ((ApiResult<Care?>)((OkObjectResult)(await Ctl(f).MarkEdgeLabelPrinted(careId, Clerk, degree: "87")).Result!).Value!).code);

        var r = await History(f, careId);
        Assert.Equal(0, r.code);
        var items = r.data!;
        Assert.Equal(new[] { "print", "angle", "angle" }, items.Select(i => i.kind).ToArray());     // 新的在前
        Assert.All(items, i => { Assert.Equal(10, i.staffId); Assert.Equal("店员", i.staffName); });
        Assert.Equal(("87", "89"), (items[0].to, items[0].from));                // 打印的是 87，换下的旧标签是 89
        Assert.Equal(("88", "87"), (items[1].from, items[1].to));
        Assert.Equal(("89", "88"), (items[2].from, items[2].to));
    }

    [Fact]
    public async Task Endpoint_OnlyThisCaresLogs()
    {
        await using var f = await CoachFixture.Create();
        var a = await NewCare(f, "89");
        var b = await NewCare(f, "89");
        await Ctl(f).SetEdgeDegree(a, "88", Clerk);
        await Ctl(f).SetEdgeDegree(b, "85", Clerk);
        var ra = (await History(f, a)).data!;
        var rb = (await History(f, b)).data!;
        Assert.Equal("88", Assert.Single(ra).to);
        Assert.Equal("85", Assert.Single(rb).to);
    }

    [Fact]
    public async Task Endpoint_WholeRowSave_ThatChangesTheAngle_AlsoShows_AndNoFakeReprintRow()
    {
        await using var f = await CoachFixture.Create();
        var careId = await NewCare(f, "89");
        // 旧版页面整行回传：UpdateCare 写的日志 table_name 是 Care
        var ctl = Ctl(f);
        var care = await f.NewContext().care.AsNoTracking().SingleAsync(c => c.id == careId);
        care.edge_degree = "86";
        await ctl.UpdateCare(care, null, 10, "整行保存");
        var items = (await History(f, careId)).data!;
        var item = Assert.Single(items);
        Assert.Equal(("angle", "89", "86", "店员"), (item.kind, item.from, item.to, item.staffName));
    }
}

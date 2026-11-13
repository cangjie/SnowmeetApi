using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

/// <summary>
/// 日养护工作列表的取数（CareWorkloadService，SQLite 内存库）：
/// 哪些装备参与统计、门店筛选与相加、22:00 截止、完成人姓名从员工表带出来。
/// 统计公式本身在 CareWorkloadRulesTests。
/// </summary>
public class CareWorkloadServiceTests
{
    private static readonly DateTime Day = new(2026, 11, 10);
    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);
    private static readonly DateTime NightNow = At(10, 23, 30);   // 当天 22:00 之后查：截止在 22:00

    private static int nextOrder = 7000;

    /// <summary>加一块板（一单一件）：生效时生成安全检查 + 发板；finished 不为空表示已发板。</summary>
    private static Care AddBoard(CoachFixture f, string shop, DateTime entered, DateTime? finished = null, int isTest = 0,
        int orderValid = 1, string orderType = "养护", string? flow = "F", int careValid = 1, bool cancel = false,
        params CareTask[] more)
    {
        var order = new Order { id = ++nextOrder, type = orderType, shop = shop, valid = orderValid, is_test = isTest,
            biz_date = entered, create_date = entered, pay_option = "普通" };
        var care = new Care { equipment = "双板", valid = careValid, biz_type = "", create_date = entered, task_flow_code = flow, is_cancel = cancel };
        care.tasks.Add(new CareTask { task_name = "安全检查", create_date = entered });
        care.tasks.AddRange(more);
        care.tasks.Add(finished == null
            ? new CareTask { task_name = "发板", create_date = entered }
            : new CareTask { task_name = "发板", create_date = entered, status = "已完成", start_time = finished, end_time = finished, staff_id = 9 });
        order.cares.Add(care);
        f.Db.order.Add(order);
        return care;
    }

    private static CareTask Done(string name, DateTime created, DateTime end, int staff) => new()
    {
        task_name = name, create_date = created, status = "已完成", start_time = end.AddMinutes(-5), end_time = end, staff_id = staff
    };

    [Fact]
    public async Task OnlyEffectiveValidCareOrdersOfTheShopCount_FinishedBeforeTheDayAndTestOrdersAreLeftOut()
    {
        await using var f = await CoachFixture.Create();
        AddBoard(f, "万龙服务中心", At(9, 10));                                     // 昨天进来还压着
        AddBoard(f, "万龙服务中心", At(9, 10), At(10, 11));                         // 昨天进来今天完成
        AddBoard(f, "万龙服务中心", At(10, 9));                                     // 今天新增
        AddBoard(f, "万龙服务中心", At(8, 10), At(9, 12));                          // 昨天就发板了
        AddBoard(f, "万龙服务中心", At(10, 9), isTest: 1);                          // 测试单
        AddBoard(f, "万龙服务中心", At(10, 9), orderValid: 0);                      // 作废订单
        AddBoard(f, "万龙服务中心", At(10, 9), orderType: "租赁");                  // 不是养护订单
        AddBoard(f, "万龙服务中心", At(10, 9), flow: null);                         // 还没生效
        AddBoard(f, "万龙服务中心", At(10, 9), careValid: 0);                       // 装备被删了
        AddBoard(f, "万龙服务中心", At(10, 23));                                    // 22:00 之后才进来
        AddBoard(f, "崇礼旗舰店", At(10, 10));                                      // 别的门店
        await f.Db.SaveChangesAsync();

        var service = new CareWorkloadService(f.NewContext());
        var one = await service.ComputeAsync(Day, NightNow, "万龙服务中心");
        Assert.Equal((2, 1, 1, 2), (one.initialPending, one.added, one.completed, one.pending));   // 昨天进来的两块（一块今天发板）+ 今天新增一块
        Assert.Equal(one.pending, one.initialPending + one.added - one.completed);
        Assert.Equal("万龙服务中心", one.shop);
        Assert.Equal("live", one.source);

        var all = await service.ComputeAsync(Day, NightNow);
        Assert.Equal((2, 2, 1, 3), (all.initialPending, all.added, all.completed, all.pending));   // 各店相加，崇礼多 1 块新增
        Assert.Null(all.shop);

        var byShop = await service.ComputeByShopAsync(Day, NightNow);
        Assert.Equal(new[] { "万龙服务中心", "崇礼旗舰店" }, byShop.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(1, byShop["崇礼旗舰店"].added);
    }

    [Fact]
    public async Task BoardsOrderedBeforeApril30AndOffSeasonCareAreLeftOut()
    {
        await using var f = await CoachFixture.Create();
        void Other(DateTime orderDate, string? bizType)
        {
            var care = AddBoard(f, "万龙服务中心", At(9, 10));
            care.biz_type = bizType;
            f.Db.order.Local.Single(o => o.cares.Contains(care)).biz_date = orderDate;
        }
        AddBoard(f, "万龙服务中心", At(9, 10));                                  // 这个雪季的，压着：算
        Other(new DateTime(2025, 5, 3), null);                                  // 去年批量导入的：不算
        Other(new DateTime(2026, 4, 29), "");                                   // 4 月 29 日：不算
        Other(new DateTime(2026, 4, 30), "");                                   // 4 月 30 日：算
        Other(new DateTime(2026, 3, 8), "非雪季养护");                           // 春天收的非雪季养护：不算
        Other(new DateTime(2026, 11, 8), "非雪季养护");                          // 这个雪季开的非雪季养护：也不算
        await f.Db.SaveChangesAsync();
        var s = await new CareWorkloadService(f.NewContext()).ComputeAsync(Day, NightNow, "万龙服务中心");
        Assert.Equal((2, 0, 0, 2), (s.initialPending, s.added, s.completed, s.pending));
    }

    [Fact]
    public async Task OnlyHandOutLeftCountsAsFinished_ReadFromTheDatabase()
    {
        await using var f = await CoachFixture.Create();
        Care Workshop(DateTime entered, params CareTask[] services)
        {
            var care = AddBoard(f, "万龙服务中心", entered, null, 0, 1, "养护", "F", 1, false, services);
            var safety = care.tasks.First(t => t.task_name == "安全检查");
            safety.status = "已完成"; safety.start_time = entered; safety.end_time = entered.AddMinutes(1); safety.staff_id = 9;
            return care;
        }
        Workshop(At(9, 10), Done("修刃", At(9, 10), At(10, 11), 9), Done("热蜡", At(9, 10), At(10, 14), 10));   // 今天做完，只差发板：完成
        Workshop(At(9, 10), Done("修刃", At(9, 10), At(10, 11), 9),
            new CareTask { task_name = "热蜡", create_date = At(9, 10) });                                       // 热蜡没做：压着
        Workshop(At(9, 10), Done("修刃", At(9, 10), At(9, 15), 9));                                              // 昨天就做完了：今天不算
        Workshop(At(10, 9), Done("修刃", At(10, 9), At(10, 10), 9));                                             // 今天收今天做完
        await f.Db.SaveChangesAsync();
        var service = new CareWorkloadService(f.NewContext());
        var s = await service.ComputeAsync(Day, NightNow, "万龙服务中心");
        Assert.Equal((2, 1, 2, 1), (s.initialPending, s.added, s.completed, s.pending));
        Assert.Equal(3, s.process[CareWorkloadRules.Edge]);                   // 今天完成的修刃 3 条（昨天那条不算）
        var noon = await service.ComputeAsync(Day, At(10, 12), "万龙服务中心");
        Assert.Equal((2, 1, 1, 2), (noon.initialPending, noon.added, noon.completed, noon.pending));   // 12:00 时第一块的热蜡还没做完
    }

    [Fact]
    public async Task EmptyDayOrUnknownShop_GivesAllZeros()
    {
        await using var f = await CoachFixture.Create();
        AddBoard(f, "万龙服务中心", At(10, 9));
        await f.Db.SaveChangesAsync();
        var service = new CareWorkloadService(f.NewContext());
        var none = await service.ComputeAsync(Day, NightNow, "南山");
        Assert.Equal((0, 0, 0, 0, 0, 0), (none.initialPending, none.added, none.completed, none.pending, none.idlePending, none.worked));
        Assert.Equal("南山", none.shop);
        Assert.Empty(none.staff);
        Assert.All(CareWorkloadRules.Processes, p => Assert.Equal(0, none.process[p]));
    }

    [Fact]
    public async Task CutoffAtTwentyTwo_TodayBeforeCutoffShowsNowAndAfterwardsFreezes()
    {
        await using var f = await CoachFixture.Create();
        AddBoard(f, "万龙服务中心", At(9, 10), At(10, 22, 30));          // 22:30 才发板
        AddBoard(f, "万龙服务中心", At(10, 9), At(10, 15));              // 15:00 发板
        AddBoard(f, "万龙服务中心", At(10, 16));                         // 16:00 才进来
        await f.Db.SaveChangesAsync();
        var service = new CareWorkloadService(f.NewContext());

        var noon = await service.ComputeAsync(Day, At(10, 12));
        Assert.Equal((1, 1, 0, 2), (noon.initialPending, noon.added, noon.completed, noon.pending));
        var evening = await service.ComputeAsync(Day, At(10, 18));
        Assert.Equal((1, 2, 1, 2), (evening.initialPending, evening.added, evening.completed, evening.pending));
        var night = await service.ComputeAsync(Day, At(10, 22, 40));      // 22:00 之后：冻结在 22:00，22:30 的发板不算
        Assert.Equal((1, 2, 1, 2), (night.initialPending, night.added, night.completed, night.pending));
        var nextDay = await service.ComputeAsync(Day, At(11, 8));          // 第二天早上看昨天：同样 22:00 截止
        Assert.Equal((night.initialPending, night.added, night.completed, night.pending), (nextDay.initialPending, nextDay.added, nextDay.completed, nextDay.pending));
        Assert.Equal(At(10, 22), nextDay.asOf);
    }

    [Fact]
    public async Task StaffNamesComeFromTheStaffTable_AndProcessesAreSplitByWhoFinished()
    {
        await using var f = await CoachFixture.Create();   // 员工 9 = 管理员，10 = 店员
        AddBoard(f, "万龙服务中心", At(9, 9), null, 0, 1, "养护", "F", 1, false,
            Done("修刃", At(9, 9), At(10, 10), 9), Done("热蜡", At(9, 9), At(10, 11), 10));
        AddBoard(f, "万龙服务中心", At(10, 9), null, 0, 1, "养护", "F", 1, false,
            Done("修刃", At(10, 9), At(10, 12), 10), Done("刮蜡", At(10, 9), At(10, 13), 10));
        await f.Db.SaveChangesAsync();
        var s = await new CareWorkloadService(f.NewContext()).ComputeAsync(Day, NightNow, "万龙服务中心");
        var byId = s.staff.ToDictionary(p => p.staffId);
        Assert.Equal("管理员", byId[9].name);
        Assert.Equal("店员", byId[10].name);
        Assert.Equal(1, byId[9].process[CareWorkloadRules.Edge]);
        Assert.Equal(1, byId[10].process[CareWorkloadRules.Edge]);
        Assert.Equal(1, byId[10].process[CareWorkloadRules.Wax]);
        Assert.Equal(1, byId[10].process[CareWorkloadRules.Scrape]);
        Assert.Equal(2, s.process[CareWorkloadRules.Edge]);
        Assert.Equal(2, s.worked);   // 两块板今天都有操作
    }

    [Fact]
    public async Task InvalidTasksAndPatchBottomMemoAreReadFromTheDatabase()
    {
        await using var f = await CoachFixture.Create();
        var care = AddBoard(f, "万龙服务中心", At(10, 9), null, 0, 1, "养护", "F", 1, false,
            Done("维修", At(10, 9), At(10, 11), 9),
            new CareTask { task_name = "修刃", create_date = At(10, 9), status = "已完成", end_time = At(10, 12), staff_id = 9, valid = 0 });
        care.repair_memo = "补板底";
        care.need_repair = 1;
        await f.Db.SaveChangesAsync();
        var s = await new CareWorkloadService(f.NewContext()).ComputeAsync(Day, NightNow, "万龙服务中心");
        Assert.Equal(1, s.process[CareWorkloadRules.PatchBottom]);
        Assert.Equal(0, s.process[CareWorkloadRules.Repair]);
        Assert.Equal(0, s.process[CareWorkloadRules.Edge]);   // 无效任务不计
    }

    [Fact]
    public async Task CancelledBoardIsCountedAsCompletedAndMarked()
    {
        await using var f = await CoachFixture.Create();
        AddBoard(f, "万龙服务中心", At(9, 9), At(10, 10), cancel: true);
        await f.Db.SaveChangesAsync();
        var s = await new CareWorkloadService(f.NewContext()).ComputeAsync(Day, NightNow, "万龙服务中心");
        Assert.Equal((1, 0, 1, 0, 1), (s.initialPending, s.added, s.completed, s.pending, s.completedCancelled));
    }
}

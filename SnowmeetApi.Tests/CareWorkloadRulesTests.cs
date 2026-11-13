using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 日养护工作列表的统计规则（CareWorkloadRules，纯函数）：
///   ① 初始待完成 + ② 新增 − ③ 完成 = ④ 待完成；过去的日子按当天 22:00 截止；取消算完成并标出；
///   无操作的待完成、今日养护雪板数；工序数量按「已完成」的任务条数、按完成人拆分；补板底与维修互斥。
/// </summary>
public class CareWorkloadRulesTests
{
    // 统计日：2026-11-10（周二）
    private static readonly DateTime Day = new(2026, 11, 10);
    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);
    private static readonly DateTime Asof = CareWorkloadRules.AsOf(Day, At(10, 23, 30));   // 当天 22:00

    private static int nextTask = 1;

    private static CareTask Task(string name, DateTime created, string status = "未开始", DateTime? start = null,
        DateTime? end = null, int? staff = null, string? staffName = null, int valid = 1) => new()
    {
        id = nextTask++, task_name = name, create_date = created, status = status, start_time = start, end_time = end,
        staff_id = staff, staff = staff == null ? null : new Staff { id = staff.Value, name = staffName ?? ("员工" + staff) },
        valid = valid
    };

    private static Care Care(int id, string? repairMemo = null, bool cancel = false, params CareTask[] tasks) => new()
    {
        id = id, equipment = "双板", valid = 1, task_flow_code = "F" + id, repair_memo = repairMemo, is_cancel = cancel,
        tasks = tasks.ToList()
    };

    /// <summary>一块板：生效时间 entered 生成安全检查 + 发板；finished 不为空表示发板已完成。</summary>
    private static Care Board(int id, DateTime entered, DateTime? finished = null, bool cancel = false, params CareTask[] more)
    {
        var tasks = new List<CareTask> { Task("安全检查", entered) };
        tasks.AddRange(more);
        tasks.Add(finished == null ? Task("发板", entered) : Task("发板", entered, "已完成", finished, finished, 1));
        return Care(id, null, cancel, tasks.ToArray());
    }

    // ───────────── 截止时刻 ─────────────

    [Fact]
    public void AsOf_IsTheEarlierOfNowAndTwentyTwoOClock()
    {
        Assert.Equal(At(10, 9), CareWorkloadRules.AsOf(Day, At(10, 9)));               // 今天 22:00 前：当前时刻
        Assert.Equal(At(10, 22), CareWorkloadRules.AsOf(Day, At(10, 22)));
        Assert.Equal(At(10, 22), CareWorkloadRules.AsOf(Day, At(10, 22, 30)));         // 今天 22:00 后：冻结在 22:00
        Assert.Equal(At(10, 22), CareWorkloadRules.AsOf(Day, At(11, 3)));              // 过去的日子：当天 22:00
        Assert.Equal(At(10, 22), CareWorkloadRules.AsOf(Day.AddHours(5), At(12, 8)));  // 传进来带时分的日期也取日期部分
        Assert.Equal(At(10, 0), CareWorkloadRules.AsOf(Day, At(9, 23)));               // 还没到的日子：窗口为空
    }

    // ───────────── 进出统计 ─────────────

    [Fact]
    public void Flow_InitialPlusAddedMinusCompletedEqualsPending()
    {
        var cares = new[]
        {
            Board(1, At(9, 10)),                           // 昨天进来，一直没完成：初始待完成、待完成
            Board(2, At(9, 11), At(10, 11)),               // 昨天进来，今天完成：初始待完成、完成
            Board(3, At(10, 9), At(10, 15)),               // 今天进来今天完成：新增、完成
            Board(4, At(10, 9, 30)),                       // 今天进来没完成：新增、待完成
            Board(5, At(8, 9), At(9, 12)),                 // 昨天就完成了：和今天无关
            Board(6, At(10, 23)),                          // 22:00 之后才进来：不算
        };
        var s = CareWorkloadRules.Compute(cares, Day, Asof);
        Assert.Equal((2, 2, 2, 2), (s.initialPending, s.added, s.completed, s.pending));
        Assert.Equal(s.pending, s.initialPending + s.added - s.completed);
        Assert.Equal(0, s.completedCancelled);
        Assert.Equal("2026-11-10", s.date);
    }

    [Fact]
    public void Flow_CompletionAfterCutoffDoesNotCount_ItStaysPending()
    {
        var late = Board(1, At(9, 10), At(10, 22, 30));    // 22:30 才完成
        var s = CareWorkloadRules.Compute(new[] { late }, Day, Asof);
        Assert.Equal((1, 0, 0, 1), (s.initialPending, s.added, s.completed, s.pending));
        // 同一块板，截止时刻放到 23:00 就算完成了
        var after = CareWorkloadRules.Compute(new[] { late }, Day, At(10, 23));
        Assert.Equal((1, 0, 1, 0), (after.initialPending, after.added, after.completed, after.pending));
    }

    [Fact]
    public void Flow_TodayBeforeCutoffUsesNow()
    {
        var cares = new[] { Board(1, At(10, 9), At(10, 15)), Board(2, At(10, 16)) };
        var noon = CareWorkloadRules.Compute(cares, Day, CareWorkloadRules.AsOf(Day, At(10, 12)));
        Assert.Equal((0, 1, 0, 1), (noon.initialPending, noon.added, noon.completed, noon.pending));   // 12:00 时 1 号板还没完成、2 号板还没进来
        var evening = CareWorkloadRules.Compute(cares, Day, CareWorkloadRules.AsOf(Day, At(10, 18)));
        Assert.Equal((0, 2, 1, 1), (evening.initialPending, evening.added, evening.completed, evening.pending));
    }

    [Fact]
    public void Flow_CancelledBoardsCountAsCompletedAndAreMarked()
    {
        var cares = new[]
        {
            Board(1, At(9, 10), At(10, 10), cancel: true),    // 昨天的板今天取消
            Board(2, At(10, 9), At(10, 11)),                  // 正常完成
        };
        var s = CareWorkloadRules.Compute(cares, Day, Asof);
        Assert.Equal((1, 1, 2, 0), (s.initialPending, s.added, s.completed, s.pending));
        Assert.Equal(1, s.completedCancelled);
    }

    [Fact]
    public void Flow_ForcedReturnAndTerminatedFinishTasksCountAsFinished()
    {
        var forced = Care(1, null, false, Task("安全检查", At(9, 9)), Task("强行索回", At(9, 9), "已完成", At(10, 9), At(10, 9), 1));
        var terminated = Care(2, null, false, Task("安全检查", At(9, 9)), Task("发板", At(9, 9), "强行中止", null, At(10, 10), null));
        var open = Care(3, null, false, Task("安全检查", At(9, 9)), Task("发板", At(9, 9), "已开始", At(10, 10), null, 1));
        var s = CareWorkloadRules.Compute(new[] { forced, terminated, open }, Day, Asof);
        Assert.Equal((3, 0, 2, 1), (s.initialPending, s.added, s.completed, s.pending));
    }

    [Fact]
    public void Flow_IgnoresInvalidTasksAndBoardsWithoutTasks()
    {
        var noTasks = Care(1);                                                        // 订单还没生效
        var onlyInvalid = Care(2, null, false, Task("安全检查", At(10, 9), valid: 0));
        // 无效的发板任务不算完成；无效任务也不决定进入时刻
        var invalidFinish = Care(3, null, false, Task("安全检查", At(10, 9)), Task("发板", At(10, 9), "已完成", At(10, 10), At(10, 10), 1, valid: 0));
        var s = CareWorkloadRules.Compute(new[] { noTasks, onlyInvalid, invalidFinish }, Day, Asof);
        Assert.Equal((0, 1, 0, 1), (s.initialPending, s.added, s.completed, s.pending));
    }

    [Fact]
    public void Flow_FinishedBeforeEnteredIsClampedSoTheIdentityHolds()
    {
        // 脏数据：发板完成时间早于任务创建时间
        var odd = Care(1, null, false, Task("安全检查", At(10, 12)), Task("发板", At(10, 12), "已完成", At(10, 8), At(10, 8), 1));
        var s = CareWorkloadRules.Compute(new[] { odd }, Day, Asof);
        Assert.Equal(s.pending, s.initialPending + s.added - s.completed);
        Assert.Equal((0, 1, 1, 0), (s.initialPending, s.added, s.completed, s.pending));
    }

    // ───────────── 有操作 / 无操作 ─────────────

    [Fact]
    public void Operations_WorkedCountsAnyStartOrEndInTheWindow_IdleIsPendingWithoutOperation()
    {
        var cares = new[]
        {
            Board(1, At(9, 10)),                                                             // 压着，今天没人动：无操作
            Board(2, At(9, 10), null, false, Task("修刃", At(9, 10), "已开始", At(10, 13))),    // 压着，今天开始了修刃：有操作、未完成
            Board(3, At(9, 10), null, false, Task("修刃", At(9, 10), "已完成", At(9, 15), At(9, 16), 1)),  // 昨天的操作，今天没动：无操作
            Board(4, At(9, 10), At(10, 16)),                                                 // 今天发板完成：有操作、已完成
            Board(5, At(8, 10), At(9, 16)),                                                  // 昨天就完成的：不算
            Board(6, At(10, 23), null, false, Task("修刃", At(10, 23), "已完成", At(10, 23, 5), At(10, 23, 20), 1)),   // 22:00 后的操作不算
        };
        var s = CareWorkloadRules.Compute(cares, Day, Asof);
        Assert.Equal(3, s.pending);          // 1、2、3 号还压着
        Assert.Equal(2, s.idlePending);      // 1、3 号今天没人动
        Assert.Equal(2, s.worked);           // 2、4 号今天有操作
        // 恒等式：待完成 = 无操作的 + 今天有操作但没完成的（只有 2 号）
        Assert.Equal(s.pending, s.idlePending + 1);
    }

    [Fact]
    public void Operations_SafetyCheckAtIntakeCountsAsAnOperation()
    {
        var fresh = Board(1, At(10, 9), null, false);
        fresh.tasks[0].status = "已完成";
        fresh.tasks[0].start_time = At(10, 9, 1);
        fresh.tasks[0].end_time = At(10, 9, 2);
        fresh.tasks[0].staff_id = 1;
        var s = CareWorkloadRules.Compute(new[] { fresh }, Day, Asof);
        Assert.Equal((1, 1, 0), (s.worked, s.added, s.idlePending));
    }

    // ───────────── 工序数量 ─────────────

    [Fact]
    public void Processes_CountCompletedTasksInTheWindowSplitByWhoFinished()
    {
        var done = (string name, int staff, int hour) => Task(name, At(9, 9), "已完成", At(10, hour), At(10, hour, 30), staff);
        var cares = new[]
        {
            Board(1, At(9, 9), null, false, done("修刃", 1, 9), done("热蜡", 1, 10), done("刮蜡", 2, 11)),
            Board(2, At(9, 9), null, false, done("修刃", 1, 12), done("机打蜡", 3, 13)),
            Board(3, At(9, 9), null, false, done("修刃", 2, 14), done("养板打蜡1", 2, 15), done("养板刮蜡1", 2, 16), done("养板打蜡2", 2, 17)),
            // 不计的：没完成、昨天完成、22:00 后完成、无效任务、不计工作量的任务名
            Board(4, At(9, 9), null, false,
                Task("修刃", At(9, 9), "已开始", At(10, 9), null, 1),
                Task("刮蜡", At(9, 9), "已完成", At(9, 9), At(9, 10), 1),
                Task("热蜡", At(9, 9), "已完成", At(10, 22, 10), At(10, 22, 40), 1),
                Task("修刃", At(9, 9), "已完成", At(10, 9), At(10, 10), 1, valid: 0),
                done("寄存或快递", 1, 11)),
        };
        // 1、2、3 号的「安全检查」「发板」没有 staff 完成，不影响；发板任务不计工作量
        var s = CareWorkloadRules.Compute(cares, Day, Asof);
        Assert.Equal(3, s.process[CareWorkloadRules.Edge]);
        Assert.Equal(4, s.process[CareWorkloadRules.Wax]);       // 热蜡 + 机打蜡 + 养板打蜡 ×2
        Assert.Equal(2, s.process[CareWorkloadRules.Scrape]);    // 刮蜡 + 养板刮蜡
        Assert.Equal(0, s.process[CareWorkloadRules.PatchBottom]);
        Assert.Equal(0, s.process[CareWorkloadRules.Repair]);

        var byId = s.staff.ToDictionary(p => p.staffId);
        Assert.Equal(2, byId[1].process[CareWorkloadRules.Edge]);
        Assert.Equal(1, byId[2].process[CareWorkloadRules.Edge]);
        Assert.Equal(1, byId[1].process[CareWorkloadRules.Wax]);
        Assert.Equal(1, byId[3].process[CareWorkloadRules.Wax]);
        Assert.Equal(2, byId[2].process[CareWorkloadRules.Wax]);
        Assert.Equal("员工1", byId[1].name);
        Assert.Equal(new[] { 2, 1, 3 }, s.staff.Select(p => p.staffId).ToArray());   // 干得多的排前面：2 号 5 条、1 号 3 条、3 号 1 条
    }

    [Fact]
    public void Processes_PatchBottomAndRepairAreMutuallyExclusive_JudgedByTheCareRepairMemo()
    {
        Care Repair(int id, string? memo, string taskMemo = "扫码完成") => Care(id, memo, false,
            Task("安全检查", At(9, 9)),
            new CareTask { id = nextTask++, task_name = "维修", create_date = At(9, 9), status = "已完成", start_time = At(10, 9), end_time = At(10, 10),
                staff_id = 1, staff = new Staff { id = 1, name = "甲" }, valid = 1, memo = taskMemo },   // 任务自己的 memo 会被场景字符串覆盖，不能拿来判断
            Task("发板", At(9, 9)));
        var cares = new[]
        {
            Repair(1, "补板底"),
            Repair(2, "修底刃,补板底"),     // 两样都有：只算补板底，不重复
            Repair(3, "贴板面"),
            Repair(4, null),
            Repair(5, "雪耙", "补板底"),    // 任务 memo 里有「补板底」三个字也不算
        };
        var s = CareWorkloadRules.Compute(cares, Day, Asof);
        Assert.Equal(2, s.process[CareWorkloadRules.PatchBottom]);
        Assert.Equal(3, s.process[CareWorkloadRules.Repair]);
        Assert.Equal(5, s.process[CareWorkloadRules.PatchBottom] + s.process[CareWorkloadRules.Repair]);   // 加起来就是全部维修
    }

    [Fact]
    public void Processes_MissingFinisherIsGroupedUnderStaffZero_AndNamesAreFilled()
    {
        var noStaff = Task("修刃", At(9, 9), "已完成", At(10, 9), At(10, 10), null);
        var s = CareWorkloadRules.Compute(new[] { Board(1, At(9, 9), null, false, noStaff) }, Day, Asof);
        var who = Assert.Single(s.staff);
        Assert.Equal((0, 1), (who.staffId, who.process[CareWorkloadRules.Edge]));
    }

    [Fact]
    public void Classify_ByTaskName()
    {
        var care = new Care { id = 1, equipment = "双板" };
        string? Kind(string name) => CareWorkloadRules.Classify(new CareTask { task_name = name }, care);
        Assert.Equal(CareWorkloadRules.Edge, Kind(" 修刃 "));
        Assert.Equal(CareWorkloadRules.Wax, Kind("热蜡"));
        Assert.Equal(CareWorkloadRules.Wax, Kind("机打蜡"));
        Assert.Equal(CareWorkloadRules.Wax, Kind("打蜡"));
        Assert.Equal(CareWorkloadRules.Wax, Kind("养板打蜡3"));
        Assert.Equal(CareWorkloadRules.Scrape, Kind("刮蜡"));
        Assert.Equal(CareWorkloadRules.Scrape, Kind("养板刮蜡2"));
        Assert.Equal(CareWorkloadRules.Repair, Kind("维修"));
        Assert.Null(Kind("安全检查"));
        Assert.Null(Kind("发板"));
        Assert.Null(Kind("寄存或快递"));
    }

    // ───────────── 门店相加 ─────────────

    [Fact]
    public void Merge_SumsShopsAndCombinesPeople_SameAsComputingEverythingAtOnce()
    {
        Care Shop(int id, DateTime entered, DateTime? finished, int staff, string name) =>
            Board(id, entered, finished, false, Task(name, entered, "已完成", At(10, 11), At(10, 12), staff));
        var shopA = new[] { Shop(1, At(9, 9), At(10, 13), 1, "修刃"), Shop(2, At(10, 9), null, 2, "热蜡") };
        var shopB = new[] { Shop(3, At(9, 9), null, 1, "修刃"), Shop(4, At(10, 10), At(10, 14), 3, "刮蜡") };
        var merged = CareWorkloadRules.Merge(new[]
        {
            CareWorkloadRules.Compute(shopA, Day, Asof), CareWorkloadRules.Compute(shopB, Day, Asof)
        }, Day, Asof);
        var all = CareWorkloadRules.Compute(shopA.Concat(shopB), Day, Asof);
        Assert.Equal(all.initialPending, merged.initialPending);
        Assert.Equal(all.added, merged.added);
        Assert.Equal(all.completed, merged.completed);
        Assert.Equal(all.pending, merged.pending);
        Assert.Equal(all.idlePending, merged.idlePending);
        Assert.Equal(all.worked, merged.worked);
        Assert.Equal(all.process, merged.process);
        Assert.Equal(all.staff.Select(p => (p.staffId, p.name, p.process[CareWorkloadRules.Edge])),
            merged.staff.Select(p => (p.staffId, p.name, p.process[CareWorkloadRules.Edge])));
        Assert.Equal(2, merged.process[CareWorkloadRules.Edge]);   // 1 号员工在两家店各修了一块，合并成一个人
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

/// <summary>
/// 日养护工作列表的每日快照：每晚 22:00 存当天的、不重复存、过去的日子读快照而不受后来改动影响、今天永远现算、
/// 表读不出来退回现算、定时任务只存当天不往回补、建表脚本和 EF 模型一致。
/// </summary>
public class CareWorkloadSnapshotTests
{
    private static readonly DateTime Day = new(2026, 11, 10);
    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);

    private static int nextOrder = 9000;

    private static void AddBoard(CoachFixture f, string shop, DateTime entered, DateTime? finished = null, params CareTask[] more)
    {
        var order = new Order { id = ++nextOrder, type = "养护", shop = shop, valid = 1, is_test = 0, biz_date = entered, create_date = entered, pay_option = "普通" };
        var care = new Care { equipment = "双板", valid = 1, biz_type = "", create_date = entered, task_flow_code = "F" };
        care.tasks.Add(new CareTask { task_name = "安全检查", create_date = entered });
        care.tasks.AddRange(more);
        care.tasks.Add(finished == null
            ? new CareTask { task_name = "发板", create_date = entered }
            : new CareTask { task_name = "发板", create_date = entered, status = "已完成", start_time = finished, end_time = finished, staff_id = 9 });
        order.cares.Add(care);
        f.Db.order.Add(order);
    }

    private static CareTask Done(string name, DateTime created, DateTime end, int staff) => new()
    {
        task_name = name, create_date = created, status = "已完成", start_time = end.AddMinutes(-5), end_time = end, staff_id = staff
    };

    /// <summary>11-10 两家店各有数据：万龙 昨天进来 1 块压着 + 今天进来 1 块今天发板（员工 9 修刃）；崇礼 今天进来 1 块没完成。</summary>
    private static async Task Seed(CoachFixture f)
    {
        AddBoard(f, "万龙服务中心", At(9, 10));
        AddBoard(f, "万龙服务中心", At(10, 9), At(10, 15), Done("修刃", At(10, 9), At(10, 11), 9));
        AddBoard(f, "崇礼旗舰店", At(10, 10), null, Done("修刃", At(10, 10), At(10, 12), 10));
        await f.Db.SaveChangesAsync();
    }

    // ───────────── 存、读 ─────────────

    [Fact]
    public async Task Save_StoresOneRowPerShopAfterTwentyTwo_OnlyOncePerDay_AndNotBeforeTheDayIsFinal()
    {
        await using var f = await CoachFixture.Create();
        await Seed(f);
        var service = new CareWorkloadService(f.NewContext());
        Assert.Equal(0, await service.SaveSnapshotAsync(Day, At(10, 21)));                 // 22:00 之前：数据还没最终，不存
        Assert.Empty(await f.NewContext().careDailyWorkload.ToListAsync());

        Assert.Equal(2, await service.SaveSnapshotAsync(Day, At(10, 22, 15)));
        Assert.Equal(0, await service.SaveSnapshotAsync(Day, At(10, 23)));                 // 已经存过：不重复
        var rows = await f.NewContext().careDailyWorkload.ToListAsync();
        Assert.Equal(new[] { "万龙服务中心", "崇礼旗舰店" }, rows.Select(r => r.shop).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var wl = rows.Single(r => r.shop == "万龙服务中心");
        Assert.Equal((Day, At(10, 22), "auto"), (wl.stat_date, wl.as_of, wl.source));
        Assert.Equal((1, 1, 1, 1), (wl.initial_pending, wl.added, wl.completed, wl.pending));
        Assert.Equal(1, wl.edge_count);
        Assert.Contains("\"staffId\":9", wl.staff_json);
    }

    [Fact]
    public async Task Read_PastDaysUseTheSnapshot_SoLaterChangesDoNotRewriteHistory()
    {
        await using var f = await CoachFixture.Create();
        await Seed(f);
        await new CareWorkloadService(f.NewContext()).SaveSnapshotAsync(Day, At(10, 22, 15));
        // 存完快照以后：一单被作废、又冒出一块旧的新增
        await f.NewContext().order.Where(o => o.shop == "崇礼旗舰店").ExecuteUpdateAsync(s => s.SetProperty(o => o.valid, 0));
        AddBoard(f, "万龙服务中心", At(10, 8));
        await f.Db.SaveChangesAsync();

        var service = new CareWorkloadService(f.NewContext());
        var snap = await service.GetAsync(Day, At(12, 9), "万龙服务中心");
        Assert.Equal("snapshot", snap.source);
        Assert.Equal((1, 1, 1, 1), (snap.initialPending, snap.added, snap.completed, snap.pending));   // 还是存下来时的样子
        var live = await service.ComputeAsync(Day, At(12, 9), "万龙服务中心");
        Assert.Equal("live", live.source);
        Assert.Equal(2, live.added);                                                                    // 现算会多出后来加的那块

        var all = await service.GetAsync(Day, At(12, 9));                                               // 全部门店 = 各店行相加
        Assert.Equal("snapshot", all.source);
        Assert.Equal((1, 2, 1, 2), (all.initialPending, all.added, all.completed, all.pending));
        Assert.Null(all.shop);
        Assert.Equal(2, all.process[CareWorkloadRules.Edge]);                                           // 员工 9 和 10 各修 1 块
        Assert.Equal(new[] { 9, 10 }, all.staff.Select(p => p.staffId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Read_TodayIsAlwaysLive_AndAShopWithoutARowOnASnapshotDayIsZero()
    {
        await using var f = await CoachFixture.Create();
        await Seed(f);
        await new CareWorkloadService(f.NewContext()).SaveSnapshotAsync(Day, At(10, 22, 15));
        var service = new CareWorkloadService(f.NewContext());
        Assert.Equal("live", (await service.GetAsync(Day, At(10, 18), "万龙服务中心")).source);       // 当天 22:00 前：不读快照
        var tonight = await service.GetAsync(Day, At(10, 23), "万龙服务中心");                          // 当天 22:00 之后快照已经存了，但今天仍然现算
        Assert.Equal("live", tonight.source);
        var stored = await service.GetSnapshotAsync(Day, "万龙服务中心");
        Assert.Equal((stored!.initialPending, stored.added, stored.completed, stored.pending),        // 冻结在 22:00，和存下的快照数字一致
            (tonight.initialPending, tonight.added, tonight.completed, tonight.pending));
        var south = await service.GetAsync(Day, At(11, 8), "南山");                                     // 那天存过，但南山没有养护装备
        Assert.Equal(("snapshot", 0, 0), (south.source, south.added, south.pending));
        Assert.Equal("南山", south.shop);
        // 从来没存过的日子（快照还没生成）：现算
        var other = await service.GetAsync(Day.AddDays(-3), At(11, 8), "万龙服务中心");
        Assert.Equal("live", other.source);
    }

    [Fact]
    public async Task Read_FallsBackToLiveWhenTheSnapshotTableIsMissing()
    {
        await using var f = await CoachFixture.Create();
        await Seed(f);
        await f.NewContext().Database.ExecuteSqlRawAsync("DROP TABLE care_daily_workload");
        var stat = await new CareWorkloadService(f.NewContext()).GetAsync(Day, At(11, 8), "万龙服务中心");
        Assert.Equal("live", stat.source);
        Assert.Equal(2, stat.initialPending + stat.added);
    }

    [Fact]
    public async Task Save_AdayWithNoCareBoardsStoresNothing()
    {
        await using var f = await CoachFixture.Create();
        Assert.Equal(0, await new CareWorkloadService(f.NewContext()).SaveSnapshotAsync(Day, At(10, 22, 15)));
        Assert.Empty(await f.NewContext().careDailyWorkload.ToListAsync());
    }

    // ───────────── 定时任务：每晚 22:00 存当天的 ─────────────

    [Fact]
    public async Task Ensure_StoresOnlyTodayAtTwentyTwo_NotBeforeNotTwiceAndNeverBackfills()
    {
        await using var f = await CoachFixture.Create();
        AddBoard(f, "万龙服务中心", At(7, 9), At(8, 12));     // 前几天的数据：不往回补
        AddBoard(f, "万龙服务中心", At(9, 9));
        AddBoard(f, "万龙服务中心", At(10, 9));
        await f.Db.SaveChangesAsync();
        var done = new HashSet<DateTime>();
        var service = new CareWorkloadService(f.NewContext());

        Assert.Equal(0, await service.EnsureSnapshotsAsync(At(10, 21, 59), done));      // 22:00 之前：不存
        Assert.Empty(done);
        Assert.Equal(1, await service.EnsureSnapshotsAsync(At(10, 22), done));          // 22:00 整：存今天（一家店一行）
        Assert.Contains(Day, done);
        Assert.Equal(0, await service.EnsureSnapshotsAsync(At(10, 22, 1), done));       // 同一进程里再跑：不碰库
        Assert.Equal(0, await new CareWorkloadService(f.NewContext()).EnsureSnapshotsAsync(At(10, 23), new HashSet<DateTime>()));   // 换个进程（重启）：库里已有就不重复存
        var rows = await f.NewContext().careDailyWorkload.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal((Day, "auto", At(10, 22)), (row.stat_date, row.source, row.as_of));   // 只有 11-10 这一天，11-07 ~ 11-09 没有补

        // 服务 22:00 以后才启动：当天的快照也会补存（只补当天）
        Assert.Equal(1, await new CareWorkloadService(f.NewContext()).EnsureSnapshotsAsync(At(11, 23, 5), new HashSet<DateTime>()));
        Assert.Equal(new[] { 10, 11 }, (await f.NewContext().careDailyWorkload.ToListAsync()).Select(r => r.stat_date.Day).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Job_RunOnceUsesItsOwnScope_AndRemembersWhatIsDone()
    {
        await using var f = await CoachFixture.Create();
        await Seed(f);
        var job = new CareWorkloadSnapshotJob(new ScopeFactory(f));
        Assert.Equal(0, await job.RunOnceAsync(At(10, 21, 30)));
        Assert.Equal(2, await job.RunOnceAsync(At(10, 22, 0)));      // 两家店各一行，只有当天
        Assert.Equal(0, await job.RunOnceAsync(At(10, 22, 1)));
        Assert.Equal(2, (await f.NewContext().careDailyWorkload.ToListAsync()).Count);
    }

    private sealed class ScopeFactory : IServiceScopeFactory
    {
        private readonly CoachFixture _f;
        public ScopeFactory(CoachFixture f) { _f = f; }
        public IServiceScope CreateScope() => new Scope(_f.NewContext());
        private sealed class Scope : IServiceScope, IServiceProvider
        {
            private readonly ApplicationDBContext _db;
            public Scope(ApplicationDBContext db) { _db = db; }
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(ApplicationDBContext) ? _db : null;
            public void Dispose() => _db.Dispose();
        }
    }

    // ───────────── 建表脚本与模型一致 ─────────────

    [Fact]
    public void SchemaScript_MatchesTheEntityColumnsAndKeepsTheUniqueIndex()
    {
        string? script = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); script == null && dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "snowmeet_ai_doc", "sql", "2026-11-13_care_daily_workload.sql");
            if (File.Exists(candidate)) script = candidate;
        }
        Assert.True(script != null, "需要相邻 snowmeet_ai_doc 仓库里的 sql/2026-11-13_care_daily_workload.sql");
        string sql = File.ReadAllText(script!);

        var options = new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new ApplicationDBContext(options);
        var entity = db.Model.FindEntityType(typeof(CareDailyWorkload))!;
        Assert.Equal("care_daily_workload", entity.GetTableName());
        var modelColumns = entity.GetProperties().Select(p => p.GetColumnName()).OrderBy(c => c, StringComparer.Ordinal).ToList();
        string body = sql.Substring(sql.IndexOf("CREATE TABLE dbo.care_daily_workload", StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf(");", StringComparison.Ordinal));
        var scriptColumns = System.Text.RegularExpressions.Regex.Matches(body, @"^\s{12}(?<col>[a-z_]+)\s+(?<type>\w+)", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups["col"].Value).OrderBy(c => c, StringComparer.Ordinal).ToList();
        Assert.Equal(modelColumns, scriptColumns);
        // 可空性一致：只有 staff_json 可空
        Assert.Equal(new[] { "staff_json" }, entity.GetProperties().Where(p => p.IsNullable).Select(p => p.GetColumnName()).ToArray());
        Assert.Contains("staff_json nvarchar(max) NULL", sql);
        // 唯一索引：(stat_date, shop)，脚本可重复执行
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "stat_date", "shop" }, index.Properties.Select(p => p.GetColumnName()).ToArray());
        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX ux_care_daily_workload_date_shop ON dbo.care_daily_workload (stat_date, shop)", sql);
        Assert.Contains("IF OBJECT_ID(N'dbo.care_daily_workload', N'U') IS NULL", sql);
        Assert.Contains("DROP TABLE dbo.care_daily_workload", File.ReadAllText(script!.Replace(".sql", "_rollback.sql")));
    }
}

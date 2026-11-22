using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Tests;

// 租赁订单重开（用户 2026-11-22 要求）：已关闭的订单可以重开，沿用老接口 Order/SetOrderCloseStatus；
// 每次关闭、重开的时间和操作的员工在订单页显示（Order/GetOrderCloseHistory，页面里可折叠）。
public class ReopenRentOrderTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
    private static readonly IHttpContextAccessor Http = new HttpContextAccessor();
    private static readonly DateTime ClosedAt = new DateTime(2026, 11, 20, 12, 37, 36);

    // ---------------- 重开 ----------------

    [Fact]
    public async Task 店员重开已关闭的订单_订单变回未关闭_记下操作员工_关闭时间保留()
    {
        await using var f = await Fixture.Create(closed: 1);
        var before = DateTime.Now.AddSeconds(-2);

        var result = await f.Reopen("staff-session");

        Assert.Equal(0, result.code);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(0, order.closed);
        Assert.Equal(ClosedAt, order.close_date);    // 上一次的关闭时间保留
        Assert.Equal(77, order.staff_id);            // 开单员工不能被覆盖
        Assert.InRange(order.update_date!.Value, before, DateTime.Now.AddSeconds(2));
        var reopen = (await f.Logs()).Single(l => l.current_value == "0");
        Assert.Equal("订单重开", reopen.scene);
        Assert.Equal(28, reopen.staff_id);
        Assert.Equal("1", reopen.prev_value);
        Assert.Equal(1, reopen.is_manual);
        Assert.NotEqual("了结关闭", (await f.LoadOrder()).rentProperties!.rentStatus);
    }

    [Fact]
    public async Task 夜间自动关单没有日志_第一次重开时先补记那次关闭()
    {
        await using var f = await Fixture.Create(closed: 1);

        Assert.Equal(0, (await f.Reopen("staff-session")).code);

        var logs = await f.Logs();
        Assert.Equal(2, logs.Count);
        var close = logs.Single(l => l.current_value == "1");
        Assert.Null(close.staff_id);                 // 系统自动，没有员工
        Assert.Equal(ClosedAt, close.create_date);   // 时间是订单当时的关闭时间
        Assert.Equal("订单关闭", close.scene);
        Assert.Equal("0", close.prev_value);
    }

    [Fact]
    public async Task 已经有关闭日志_重开不再补记()
    {
        await using var f = await Fixture.Create(closed: 1, closeLogBy: 28);

        Assert.Equal(0, (await f.Reopen("staff-session")).code);

        var logs = await f.Logs();
        Assert.Equal(2, logs.Count);
        Assert.Equal(28, logs.Single(l => l.current_value == "1").staff_id);   // 还是原来那条手动关单的日志
    }

    [Theory]
    [InlineData("no-such-session")]
    [InlineData("")]
    [InlineData("customer-session")]
    public async Task 没有会话_或不是员工_不能重开(string sessionKey)
    {
        await using var f = await Fixture.Create(closed: 1);
        var result = await f.Reopen(sessionKey);
        Assert.Equal(1, result.code);
        Assert.Equal("没有权限", result.message);
        Assert.Equal(1, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
        Assert.Empty(await f.Logs());
    }

    [Fact]
    public async Task 职级低于100的员工_不能重开()
    {
        await using var f = await Fixture.Create(closed: 1, staffLevel: 50);
        var result = await f.Reopen("staff-session");
        Assert.Equal(1, result.code);
        Assert.Equal(1, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
    }

    [Fact]
    public async Task 订单没有关闭_不用重开_不记日志()
    {
        await using var f = await Fixture.Create(closed: 0, closeDate: null);
        var result = await f.Reopen("staff-session");
        Assert.Equal(1, result.code);
        Assert.Equal("订单没有关闭，不用重开", result.message);
        Assert.Empty(await f.Logs());
    }

    [Fact]
    public async Task 未付款作废的订单没有关闭时间_不能重开()
    {
        await using var f = await Fixture.Create(closed: 1, voided: true);
        var result = await f.Reopen("staff-session");
        Assert.Equal(1, result.code);
        Assert.Equal("这是未付款作废的订单，不能重开", result.message);
        Assert.Equal(1, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
        Assert.Empty(await f.Logs());
    }

    [Fact]
    public async Task 订单不存在_提示没有找到()
    {
        await using var f = await Fixture.Create(closed: 1);
        var result = await f.Reopen("staff-session", orderId: 999);
        Assert.Equal(1, result.code);
        Assert.Equal("没有找到订单", result.message);
    }

    [Fact]
    public async Task 连点两次_第二次提示没有关闭_只记一条重开日志()
    {
        await using var f = await Fixture.Create(closed: 1, closeLogBy: 28);
        Assert.Equal(0, (await f.Reopen("staff-session")).code);
        var second = await f.Reopen("staff-session");
        Assert.Equal(1, second.code);
        Assert.Equal("订单没有关闭，不用重开", second.message);
        Assert.Single((await f.Logs()).Where(l => l.current_value == "0"));
    }

    [Fact]
    public async Task 两个请求同时拿着已关闭的订单_只有一个生效_只记一条日志()
    {
        await using var f = await Fixture.Create(closed: 1, closeLogBy: 28);
        var helper = new OrderController(f.Db, Config, Http);
        var stale1 = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        var stale2 = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);

        Assert.NotNull(await helper.SetOrderClosedState(stale1, false, 28));
        Assert.Null(await helper.SetOrderClosedState(stale2, false, 28));   // 手里的订单还是旧的「已关闭」，库里已经重开了

        Assert.Single((await f.Logs()).Where(l => l.current_value == "0"));
    }

    // ---------------- 关闭接口的收口 ----------------

    [Fact]
    public async Task 租赁订单不能从老接口直接关闭_要走订单关闭按钮()
    {
        // 重开后订单带着上次的关闭时间，老接口不能让它绕过关闭条件
        await using var f = await Fixture.Create(closed: 1, closeLogBy: 28);
        Assert.Equal(0, (await f.Reopen("staff-session")).code);

        var result = await f.Reopen("staff-session", closed: true);

        Assert.Equal(1, result.code);
        Assert.Contains("订单关闭", result.message);
        Assert.Equal(0, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
    }

    [Fact]
    public async Task 其他类型的订单仍可以从老接口关闭()
    {
        await using var f = await Fixture.Create(closed: 0, closeDate: ClosedAt, type: "养护");
        var result = await f.Reopen("staff-session", closed: true);
        Assert.Equal(0, result.code);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(1, order.closed);
        Assert.NotEqual(ClosedAt, order.close_date);
        Assert.Equal(28, (await f.Logs()).Single(l => l.current_value == "1").staff_id);
    }

    // ---------------- 重开之后 ----------------

    [Fact]
    public async Task 重开后夜间关单任务不会自动再关它()
    {
        await using var f = await Fixture.Create(closed: 1);
        Assert.Equal(0, (await f.Reopen("staff-session")).code);

        await new RentController(f.Db, Config, Http).CloseOrder();

        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(0, order.closed);
        Assert.Equal(ClosedAt, order.close_date);
    }

    [Fact]
    public async Task 重开后条件满足_可以再用订单关闭按钮关一次_关闭时间更新()
    {
        await using var f = await Fixture.Create(closed: 1);
        Assert.Equal(0, (await f.Reopen("staff-session")).code);
        Assert.True((await f.LoadOrder()).canManualClose);

        var closed = await new RentController(f.Db, Config, Http).CloseOrderByStaff(1, "staff-session");

        var ok = Assert.IsType<OkObjectResult>(closed.Result);
        Assert.Equal(0, Assert.IsType<ApiResult<RentController.ManualCloseResult?>>(ok.Value).code);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(1, order.closed);
        Assert.True(order.close_date > ClosedAt);
    }

    [Fact]
    public async Task 万龙订单关闭_重开_再关闭_万龙应分记录不会重复生成()
    {
        await using var f = await Fixture.Create(closed: 0, closeDate: null, shop: "万龙体验中心");
        var rent = new RentController(f.Db, Config, Http);
        Assert.Equal(0, ((ApiResult<RentController.ManualCloseResult?>)((OkObjectResult)(await rent.CloseOrderByStaff(1, "staff-session")).Result!).Value!).code);
        Assert.Single(await f.Db.orderShare.AsNoTracking().Where(s => s.order_id == 1 && s.valid).ToListAsync());

        Assert.Equal(0, (await f.Reopen("staff-session")).code);
        Assert.Equal(0, ((ApiResult<RentController.ManualCloseResult?>)((OkObjectResult)(await rent.CloseOrderByStaff(1, "staff-session")).Result!).Value!).code);

        Assert.Single(await f.Db.orderShare.AsNoTracking().Where(s => s.order_id == 1 && s.valid).ToListAsync());
    }

    // ---------------- 关闭 / 重开记录 ----------------

    [Fact]
    public async Task 记录按时间列出每次关闭和重开_带员工姓名()
    {
        await using var f = await Fixture.Create(closed: 0, closeDate: null);
        var rent = new RentController(f.Db, Config, Http);
        await rent.CloseOrderByStaff(1, "staff-session");           // 张三 关闭
        Assert.Equal(0, (await f.Reopen("staff2-session")).code);   // 李四 重开
        await rent.CloseOrderByStaff(1, "staff2-session");          // 李四 又关闭
        Assert.Equal(0, (await f.Reopen("staff-session")).code);    // 张三 又重开

        var history = await f.History("staff-session");

        Assert.Equal(0, history.code);
        Assert.Equal(new[] { "关闭", "重开", "关闭", "重开" }, history.data!.Select(e => e.action).ToArray());
        Assert.Equal(new[] { "张三", "李四", "李四", "张三" }, history.data.Select(e => e.staffName).ToArray());
        Assert.All(history.data, e => Assert.False(e.auto));
        Assert.Equal(history.data.OrderBy(e => e.time).Select(e => e.time), history.data.Select(e => e.time));
    }

    [Fact]
    public async Task 夜间自动关单后重开_记录里第一行是系统自动关闭()
    {
        await using var f = await Fixture.Create(closed: 1);
        Assert.Equal(0, (await f.Reopen("staff-session")).code);

        var history = await f.History("staff-session");

        Assert.Equal(2, history.data!.Count);
        Assert.Equal("关闭", history.data[0].action);
        Assert.Equal("系统自动", history.data[0].staffName);
        Assert.True(history.data[0].auto);
        Assert.Equal(ClosedAt, history.data[0].time);
        Assert.Equal("重开", history.data[1].action);
        Assert.Equal("张三", history.data[1].staffName);
    }

    [Fact]
    public async Task 夜间自动关单从没重开过_没有日志也显示一行系统自动关闭()
    {
        await using var f = await Fixture.Create(closed: 1);
        var history = await f.History("staff-session");
        var only = Assert.Single(history.data!);
        Assert.Equal("关闭", only.action);
        Assert.True(only.auto);
        Assert.Equal(ClosedAt, only.time);
    }

    [Fact]
    public async Task 没关闭过也没有日志的订单_记录为空()
    {
        await using var f = await Fixture.Create(closed: 0, closeDate: null);
        Assert.Empty((await f.History("staff-session")).data!);
    }

    [Theory]
    [InlineData("no-such-session")]
    [InlineData("customer-session")]
    public async Task 记录接口_不是员工不能看(string sessionKey)
    {
        await using var f = await Fixture.Create(closed: 1);
        var history = await f.History(sessionKey);
        Assert.Equal(1, history.code);
        Assert.Equal("没有权限", history.message);
        Assert.Null(history.data);
    }

    [Fact]
    public async Task 记录接口_订单不存在提示没有找到()
    {
        await using var f = await Fixture.Create(closed: 1);
        var history = await f.History("staff-session", orderId: 999);
        Assert.Equal(1, history.code);
        Assert.Equal("没有找到订单", history.message);
    }

    [Fact]
    public async Task 记录只看这张订单的订单关闭日志_别的表别的字段别的订单不混进来()
    {
        await using var f = await Fixture.Create(closed: 0, closeDate: null);
        f.Db.coreDataModLog.Add(new CoreDataModLog { table_name = "Order", field_name = "hide", key_value = 1, scene = "x",
            prev_value = "0", current_value = "1", create_date = ClosedAt });
        f.Db.coreDataModLog.Add(new CoreDataModLog { table_name = "Order", field_name = "closed", key_value = 2, scene = "订单关闭",
            prev_value = "0", current_value = "1", staff_id = 28, create_date = ClosedAt });
        f.Db.coreDataModLog.Add(new CoreDataModLog { table_name = "rental", field_name = "closed", key_value = 1, scene = "x",
            prev_value = "0", current_value = "1", create_date = ClosedAt });
        await f.Db.SaveChangesAsync();

        Assert.Empty((await f.History("staff-session")).data!);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ApplicationDBContext Db { get; }

        private Fixture(SqliteConnection connection, ApplicationDBContext db) { this.connection = connection; Db = db; }

        public Task<Order> LoadOrder() => new OrderController(Db, Config, Http).GetOrder(1);

        public Task<List<CoreDataModLog>> Logs() => Db.coreDataModLog.AsNoTracking()
            .Where(l => l.table_name == "Order" && l.field_name == "closed" && l.key_value == 1)
            .OrderBy(l => l.id).ToListAsync();

        public async Task<ApiResult<Order?>> Reopen(string sessionKey, int orderId = 1, bool closed = false)
        {
            var r = await new OrderController(Db, Config, Http).SetOrderCloseStatus(orderId, closed, sessionKey);
            var ok = Assert.IsType<OkObjectResult>(r.Result);
            return Assert.IsType<ApiResult<Order?>>(ok.Value);
        }

        public async Task<ApiResult<List<OrderCloseEvent>?>> History(string sessionKey, int orderId = 1)
        {
            var r = await new OrderController(Db, Config, Http).GetOrderCloseHistory(orderId, sessionKey);
            var ok = Assert.IsType<OkObjectResult>(r.Result);
            return Assert.IsType<ApiResult<List<OrderCloseEvent>?>>(ok.Value);
        }

        // 一张租赁单：押金 500 退 430、租金 70（可关单）；closed = 1 时关闭时间是 ClosedAt（夜间自动关单：没有日志）。
        // closeLogBy 给了员工 id 就补一条那位员工手动关单的日志。staff-session 是张三（28，职级 staffLevel），staff2-session 是李四（29，职级 200）
        // voided = 未付款作废的订单：closed = 1，没有关闭时间
        public static async Task<Fixture> Create(int closed, DateTime? closeDate = null, string type = "租赁",
            string shop = "测试店", int staffLevel = 300, int? closeLogBy = null, bool voided = false)
        {
            DateTime? closeAt = voided ? null : closeDate ?? (closed == 1 ? ClosedAt : (DateTime?)null);
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
            await db.Database.EnsureCreatedAsync();
            db.member.Add(new Member { id = 10, real_name = "张三会员", gender = "男" });
            db.member.Add(new Member { id = 11, real_name = "顾客", gender = "男" });
            db.member.Add(new Member { id = 12, real_name = "李四会员", gender = "男" });
            db.Set<Staff>().Add(new Staff { id = 28, name = "张三", title_level = staffLevel, valid = 1 });
            db.Set<Staff>().Add(new Staff { id = 29, name = "李四", title_level = 200, valid = 1 });
            db.Set<SocialAccountForJob>().Add(new SocialAccountForJob { id = 1, member_id = 10, wechat_mini_openid = "staff-openid" });
            db.Set<SocialAccountForJob>().Add(new SocialAccountForJob { id = 2, member_id = 12, wechat_mini_openid = "staff2-openid" });
            db.Set<StaffSocialAccount>().Add(new StaffSocialAccount { staff_id = 28, social_account_id = 1, valid = 1 });
            db.Set<StaffSocialAccount>().Add(new StaffSocialAccount { staff_id = 29, social_account_id = 2, valid = 1 });
            db.miniSession.Add(new MiniSession { session_key = "staff-session", session_type = "wechat_mini_openid", member_id = 10,
                wechat_openid = "staff-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.miniSession.Add(new MiniSession { session_key = "staff2-session", session_type = "wechat_mini_openid", member_id = 12,
                wechat_openid = "staff2-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.miniSession.Add(new MiniSession { session_key = "customer-session", session_type = "wechat_mini_openid", member_id = 11,
                wechat_openid = "customer-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.order.Add(new Order { id = 1, code = "WT_ZL_261120_00004", type = type, shop = shop, valid = 1, closed = closed,
                close_date = closeAt, staff_id = 77, create_date = new DateTime(2026, 11, 20) });
            db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = 1, guaranty = 500 });
            db.rentalDetail.Add(new RentalDetail { id = 3, rental_id = 2, charge_type = "租金", amount = 70, valid = 1 });
            db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = 500, pay_method = "微信支付",
                status = "支付成功", valid = 1, out_trade_no = "WT_ZL_261120_00004_ZF_01" });
            db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { id = 5, order_id = 1, payment_id = 4,
                amount = 430, refund_id = "refunded" });
            db.guaranty.Add(new Guaranty { id = 6, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = 500 });
            db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 6, payment_id = 4 });
            if (closeLogBy != null)
            {
                db.coreDataModLog.Add(new CoreDataModLog { table_name = "Order", field_name = "closed", key_value = 1,
                    prev_value = "0", current_value = "1", staff_id = closeLogBy, is_manual = 1, scene = "订单关闭",
                    create_date = ClosedAt });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

// 关闭 / 重开记录的整理规则本身（不经过接口）
public class OrderCloseHistoryRulesTests
{
    private static CoreDataModLog Log(int id, string current, int? staffId, DateTime at, string? staffName = null,
        string table = "Order", string field = "closed")
        => new CoreDataModLog { id = id, table_name = table, field_name = field, key_value = 1, scene = "x",
            current_value = current, staff_id = staffId, create_date = at,
            staff = staffName == null ? null : new Staff { id = staffId ?? 0, name = staffName } };

    [Fact]
    public void 日志乱序也按时间排_同一时刻按日志编号()
    {
        var t = new DateTime(2026, 11, 20, 10, 0, 0);
        var events = OrderCloseHistoryRules.Build(0, t, new[]
        {
            Log(3, "0", 28, t.AddHours(2), "张三"),
            Log(2, "0", 29, t.AddHours(1), "李四"),
            Log(1, "1", 28, t.AddHours(1), "张三"),
        });
        Assert.Equal(new[] { "关闭", "重开", "重开" }, events.Select(e => e.action).ToArray());
        Assert.Equal(new[] { "张三", "李四", "张三" }, events.Select(e => e.staffName).ToArray());
    }

    [Fact]
    public void 员工找不到姓名_显示员工编号()
    {
        var events = OrderCloseHistoryRules.Build(1, DateTime.Now, new[] { Log(1, "1", 55, DateTime.Now) });
        Assert.Equal("员工#55", Assert.Single(events).staffName);
    }

    [Fact]
    public void 日志里的值不是0或1_跳过()
    {
        var events = OrderCloseHistoryRules.Build(0, null, new[] { Log(1, "2", 28, DateTime.Now), Log(2, null!, 28, DateTime.Now) });
        Assert.Empty(events);
    }

    [Fact]
    public void 订单已关闭但没有关闭时间_没有日志也不补行()
    {
        // 未付款作废的订单：closed = 1、没有 close_date
        Assert.Empty(OrderCloseHistoryRules.Build(1, null, new List<CoreDataModLog>()));
    }

    [Fact]
    public void 有日志时不再按订单关闭时间补一行()
    {
        var t = new DateTime(2026, 11, 20, 10, 0, 0);
        var events = OrderCloseHistoryRules.Build(1, t.AddDays(1), new[] { Log(1, "1", 28, t, "张三") });
        Assert.Single(events);
        Assert.Equal(t, events[0].time);
    }
}

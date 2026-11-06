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

// 租赁订单详情页「订单关闭」按钮（用户 2026-11-06 要求）：
//   订单全部退完押金，或者无需退押金且全部租赁物都已归还，才能手动关单；
//   点击后订单变成「了结关闭」（closed = 1），记下关闭时间（close_date）和操作的员工（审计日志 staff_id）。
// 显示条件和接口校验共用 RentOrderCloseRules.CanManualClose，页面读 order.canManualClose。
public class ManualCloseRentOrderTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
    private static readonly IHttpContextAccessor Http = new HttpContextAccessor();

    // ---------------- 显示条件（canManualClose） ----------------

    [Fact]
    public async Task 全额退押金_可以关单()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70);
        Assert.True((await f.LoadOrder()).canManualClose);
    }

    [Fact]
    public async Task 无需退押金且全部归还_可以关单()
    {
        // 押金 180 刚好抵租金 180，没有退款，也不需要退
        await using var f = await Fixture.Create(paid: 180, refunded: 0, rent: 180);
        var order = await f.LoadOrder();
        Assert.Equal("全部归还", order.rentProperties!.rentStatus);
        Assert.True(order.canManualClose);
    }

    [Fact]
    public async Task 全部归还但押金还没退_不能关单()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 0, rent: 70);
        Assert.False((await f.LoadOrder()).canManualClose);
    }

    [Fact]
    public async Task 只退了一部分押金_不能关单()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 100, rent: 70);
        var order = await f.LoadOrder();
        Assert.Equal("部分退押金", order.rentProperties!.rentStatus);
        Assert.False(order.canManualClose);
    }

    [Fact]
    public async Task 租赁物没有全部归还_即使无需退押金也不能关单()
    {
        await using var f = await Fixture.Create(paid: 180, refunded: 0, rent: 180, settled: 0);
        Assert.False((await f.LoadOrder()).canManualClose);
    }

    [Fact]
    public async Task 押金不够抵租金_客人还欠钱_不能关单()
    {
        await using var f = await Fixture.Create(paid: 100, refunded: 0, rent: 180);
        Assert.False((await f.LoadOrder()).canManualClose);
    }

    [Fact]
    public async Task 还有应收没收齐_不能关单()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70, stillOwed: 10);
        Assert.False((await f.LoadOrder()).canManualClose);
    }

    [Fact]
    public async Task 已经关闭的订单_不显示()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70, closed: 1);
        Assert.False((await f.LoadOrder()).canManualClose);
    }

    [Fact]
    public async Task 不是租赁订单_不显示()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70, type: "养护");
        Assert.False((await f.LoadOrder()).canManualClose);
    }

    // ---------------- 关单接口 ----------------

    [Fact]
    public async Task 店员关单_订单变成了结关闭_记下关闭时间和员工()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70);
        var before = DateTime.Now.AddSeconds(-2);

        var result = await f.Close("staff-session");

        Assert.Equal(0, result.code);
        Assert.Equal(1, result.data!.orderId);
        Assert.Equal(28, result.data.staffId);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(1, order.closed);
        Assert.NotNull(order.close_date);
        Assert.InRange(order.close_date!.Value, before, DateTime.Now.AddSeconds(2));
        Assert.Equal(order.close_date, result.data.closeDate);
        Assert.Equal(77, order.staff_id);   // 开单员工不能被覆盖
        var log = Assert.Single(await f.Db.coreDataModLog.AsNoTracking()
            .Where(l => l.table_name == "Order" && l.key_value == 1).ToListAsync());
        Assert.Equal("closed", log.field_name);
        Assert.Equal("订单关闭", log.scene);
        Assert.Equal(28, log.staff_id);
        Assert.Equal("0", log.prev_value);
        Assert.Equal("1", log.current_value);
        Assert.Equal(1, log.is_manual);
        Assert.Equal("了结关闭", (await f.LoadOrder()).rentProperties!.rentStatus);
    }

    [Theory]
    [InlineData("no-such-session")]
    [InlineData("")]
    [InlineData("customer-session")]
    public async Task 没有会话_或不是员工_不能关单(string sessionKey)
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70);
        var result = await f.Close(sessionKey);
        Assert.Equal(1, result.code);
        Assert.Equal("没有权限", result.message);
        Assert.Equal(0, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
        Assert.Empty(await f.Db.coreDataModLog.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task 职级低于100的员工_不能关单()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70, staffLevel: 50);
        var result = await f.Close("staff-session");
        Assert.Equal(1, result.code);
        Assert.Equal(0, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
    }

    [Fact]
    public async Task 不满足条件_接口也拒绝_不改订单不记日志()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 0, rent: 70);   // 押金还没退
        var result = await f.Close("staff-session");
        Assert.Equal(1, result.code);
        Assert.Contains("不满足关闭条件", result.message);
        var order = await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(0, order.closed);
        Assert.Null(order.close_date);
        Assert.Empty(await f.Db.coreDataModLog.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task 订单不存在_提示没有找到()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70);
        var result = await f.Close("staff-session", orderId: 999);
        Assert.Equal(1, result.code);
        Assert.Equal("没有找到订单", result.message);
    }

    [Fact]
    public async Task 连点两次_第二次提示已经关闭_只记一条日志()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70);
        Assert.Equal(0, (await f.Close("staff-session")).code);
        var second = await f.Close("staff-session");
        Assert.Equal(1, second.code);
        Assert.Equal("订单已经关闭", second.message);
        Assert.Single(await f.Db.coreDataModLog.AsNoTracking().Where(l => l.key_value == 1).ToListAsync());
    }

    [Fact]
    public async Task 万龙体验中心的订单手动关单_和夜间关单一样生成应分记录()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70, shop: "万龙体验中心");
        Assert.Equal(0, (await f.Close("staff-session")).code);
        var orderShare = await f.Db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        Assert.Equal(35, orderShare.amount, 2);
        Assert.Single(await f.Db.paymentShare.AsNoTracking().Where(s => s.share_id == orderShare.id).ToListAsync());
    }

    [Fact]
    public async Task 其他门店的订单手动关单_不生成应分记录()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70, shop: "测试店");
        Assert.Equal(0, (await f.Close("staff-session")).code);
        Assert.Empty(await f.Db.orderShare.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task 手动关闭后_夜间关单任务不再处理它()
    {
        await using var f = await Fixture.Create(paid: 500, refunded: 430, rent: 70);
        Assert.Equal(0, (await f.Close("staff-session")).code);
        var closeDate = (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).close_date;
        await new RentController(f.Db, Config, Http).CloseOrder();
        Assert.Equal(closeDate, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == 1)).close_date);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ApplicationDBContext Db { get; }

        private Fixture(SqliteConnection connection, ApplicationDBContext db) { this.connection = connection; Db = db; }

        public Task<Order> LoadOrder() => new OrderController(Db, Config, Http).GetOrder(1);

        public async Task<ApiResult<RentController.ManualCloseResult?>> Close(string sessionKey, int orderId = 1)
        {
            var r = await new RentController(Db, Config, Http).CloseOrderByStaff(orderId, sessionKey);
            var ok = Assert.IsType<OkObjectResult>(r.Result);
            return Assert.IsType<ApiResult<RentController.ManualCloseResult?>>(ok.Value);
        }

        // 一张租赁单：一次付款 paid，退 refunded，租金明细 rent；staffLevel 是 staff-session 那位员工的职级
        public static async Task<Fixture> Create(double paid, double refunded, double rent, int settled = 1, int closed = 0,
            string type = "租赁", string shop = "测试店", double? stillOwed = null, int staffLevel = 300)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
            await db.Database.EnsureCreatedAsync();
            db.member.Add(new Member { id = 10, real_name = "店员", gender = "男" });
            db.member.Add(new Member { id = 11, real_name = "顾客", gender = "男" });
            db.Set<Staff>().Add(new Staff { id = 28, title_level = staffLevel, valid = 1 });
            db.Set<SocialAccountForJob>().Add(new SocialAccountForJob { id = 1, member_id = 10, wechat_mini_openid = "staff-openid" });
            db.Set<StaffSocialAccount>().Add(new StaffSocialAccount { staff_id = 28, social_account_id = 1, valid = 1 });
            db.miniSession.Add(new MiniSession { session_key = "staff-session", session_type = "wechat_mini_openid", member_id = 10,
                wechat_openid = "staff-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.miniSession.Add(new MiniSession { session_key = "customer-session", session_type = "wechat_mini_openid", member_id = 11,
                wechat_openid = "customer-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.order.Add(new Order { id = 1, code = "WT_ZL_261028_00001", type = type, shop = shop, valid = 1, closed = closed,
                staff_id = 77, paying_amount = stillOwed, create_date = new DateTime(2026, 10, 28) });
            db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = settled, guaranty = paid });
            db.rentalDetail.Add(new RentalDetail { id = 3, rental_id = 2, charge_type = "租金", amount = rent, valid = 1 });
            db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = paid, pay_method = "微信支付",
                status = "支付成功", valid = 1, out_trade_no = "WT_ZL_261028_00001_ZF_01" });
            if (refunded > 0)
            {
                db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { id = 5, order_id = 1, payment_id = 4,
                    amount = refunded, refund_id = "refunded" });
            }
            db.guaranty.Add(new Guaranty { id = 6, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = paid });
            db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 6, payment_id = 4 });
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

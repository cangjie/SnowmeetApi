using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

public class SkipassBillTests
{
    static readonly DateTime Day = new(2026, 11, 13);

    [Theory]
    [InlineData("大好河山")]
    [InlineData("万龙自我游")]
    public async Task Source_and_full_end_day_are_kept_for_every_page_and_duplicate_orders_are_not_counted_twice(string source)
    {
        await using var f = await Fixture.Create();
        var pages = new List<int>();
        var service = new SkipassBillService(f.Db, (account, start, end, page) =>
        {
            Assert.Equal(source, account); Assert.Equal(Day, start);
            Assert.Equal(Day.AddDays(1).AddTicks(-1), end); pages.Add(page);
            return Task.FromResult(new SupplierBillPage(2, page == 0
                ? new[] { Row("A"), Row("B", Day.AddHours(23).AddMinutes(59)) }
                : new[] { Row("B"), Row("C"), Row("OUT", Day.AddDays(1)) }));
        });
        var rows = await service.GetAsync(source, Day.AddHours(2), Day.AddHours(3));
        Assert.Equal(new[] { 0, 1 }, pages); Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.False(row.isSystemOrder));
        Assert.Empty(await f.Db.ziwoyouOrder.ToListAsync()); // 不触发旧缓存写入或同步业务。
    }

    [Fact]
    public async Task Cart_receipts_refunds_and_shares_are_attached_to_each_ticket_without_repeating_the_order_total()
    {
        await using var f = await Fixture.Create();
        f.Db.staff.Add(new Staff { id = 7, name = "测试店员", valid = 1 });
        f.Db.order.Add(new Order { id = 10, staff_id = 7, type = "雪票", valid = 1 });
        f.Db.skiPassProduct.Add(new SkiPassProduct { product_id = 20, source = "大好河山" });
        f.Db.skiPass.AddRange(Pass(1, "A", 100), Pass(2, "B", 200));
        f.Db.orderPayment.AddRange(new OrderPayment { id = 30, order_id = 10, pay_method = "微信支付", amount = 300, status = "支付成功" },
            new OrderPayment { id = 31, order_id = 10, amount = 999, status = "待支付" });
        f.Db.Set<OrderPaymentRefund>().AddRange(
            new OrderPaymentRefund { id = 40, order_id = 10, payment_id = 30, skipass_id = 1, amount = 100, state = 1 },
            new OrderPaymentRefund { id = 41, order_id = 10, payment_id = 30, skipass_id = 2, amount = 50, refund_id = "accepted" },
            new OrderPaymentRefund { id = 42, order_id = 10, payment_id = 30, skipass_id = 2, amount = 1000, state = 0 });
        f.Db.orderShare.Add(new OrderShare { id = 50, order_id = 10, skipass_id = 2, amount = 1 });
        f.Db.paymentShare.AddRange(new PaymentShare { id = 51, share_id = 50, payment_id = 30, amount = 1, success = true },
            new PaymentShare { id = 52, share_id = 50, payment_id = 30, amount = 20, success = false });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var a = Row("A"); a.orderState = 3;
        var service = Service(f.Db, a, Row("B"), Row("EXTERNAL"));
        var rows = await service.GetAsync("大好河山", Day, Day);
        var first = rows.Single(r => r.orderId == "A"); var second = rows.Single(r => r.orderId == "B");
        Assert.Equal(100m, first.paidAmount); Assert.Equal(200m, second.paidAmount);
        Assert.Equal(100m, first.refundAmount); Assert.Equal(50m, second.refundAmount);
        Assert.Equal(0m, first.settlementAmount); Assert.Equal(20m, second.settlementAmount);
        Assert.Equal(0m, first.shareAmount); Assert.Equal(1m, second.shareAmount);
        Assert.Equal("测试店员", second.staffName);
        var external = rows.Single(r => r.orderId == "EXTERNAL");
        Assert.False(external.isSystemOrder); Assert.Equal(0m, external.paidAmount);
        // 去年的账号不能关联到今年商品的本地权益/收退款。
        Assert.All(await service.GetAsync("万龙自我游", Day, Day), r => Assert.False(r.isSystemOrder));
    }

    [Fact]
    public async Task Fractional_cent_allocations_and_old_untagged_refunds_preserve_exact_totals_even_for_partial_queries()
    {
        await using var f = await Fixture.Create();
        f.Db.order.Add(new Order { id = 10, type = "雪票", valid = 1 });
        f.Db.skiPassProduct.Add(new SkiPassProduct { product_id = 20, source = "大好河山" });
        f.Db.skiPass.AddRange(Pass(1, "A", 1), Pass(2, "B", 1), Pass(3, "C", 1));
        f.Db.orderPayment.Add(new OrderPayment { id = 30, order_id = 10, pay_method = "微信支付", amount = 1, status = "支付成功" });
        f.Db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { order_id = 10, payment_id = 30, amount = 0.5, state = 1 });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var rows = await Service(f.Db, Row("A"), Row("B"), Row("C")).GetAsync("大好河山", Day, Day);
        Assert.Equal(1m, rows.Sum(r => r.paidAmount)); Assert.Equal(0.5m, rows.Sum(r => r.refundAmount));
        var partial = await Service(f.Db, Row("B")).GetAsync("大好河山", Day, Day);
        Assert.Equal(rows.Single(r => r.orderId == "B").paidAmount, partial.Single().paidAmount);
    }

    [Fact]
    public async Task A_failed_or_incomplete_later_page_fails_the_report_instead_of_returning_partial_financial_totals()
    {
        await using var f = await Fixture.Create();
        var service = new SkipassBillService(f.Db, (_, _, _, page) => Task.FromResult(
            new SupplierBillPage(2, page == 0 ? new[] { Row("A") } : Array.Empty<ZiwoyouListOrder>())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("大好河山", Day, Day));
    }

    [Fact]
    public async Task New_supplier_report_rejects_missing_invalid_and_non_staff_sessions_before_contacting_supplier()
    {
        await using var f = await Fixture.Create();
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Settings:AppId"] = "local-test" }).Build();
        var controller = new WanlongZiwoyouHelper(f.Db, config);
        Assert.IsType<UnauthorizedResult>((await controller.GetSupplierOrderBills(Day, Day)).Result);
        Assert.IsType<UnauthorizedResult>((await controller.GetSupplierOrderBills(Day, Day, sessionKey: "invalid")).Result);
        f.Db.member.Add(new Member { id = 1 });
        f.Db.miniSession.Add(new MiniSession { session_key = "customer", session_type = "wechat_mini_openid",
            valid = 1, member_id = 1, expire_date = DateTime.Now.AddHours(1) });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        Assert.IsType<UnauthorizedResult>((await controller.GetSupplierOrderBills(Day, Day, sessionKey: "customer")).Result);
    }

    static SkipassBillService Service(ApplicationDBContext db, params ZiwoyouListOrder[] rows)
        => new(db, (_, _, _, _) => Task.FromResult(new SupplierBillPage(1, rows)));
    static ZiwoyouListOrder Row(string id, DateTime? date = null) => new()
        { orderId = id, orderDate = date ?? Day, productName = "测试票", num = 1, orderState = 2, settlementPrice = 20 };
    static SkiPass Pass(int id, string number, double price) => new()
        { id = id, product_id = 20, order_id = 10, reserve_no = number, deal_price = price,
            resort = "万龙", product_name = "测试票", valid = 1, count = 1 };

    sealed class Fixture : IAsyncDisposable
    {
        readonly SqliteConnection connection;
        public ApplicationDBContext Db { get; }
        Fixture(SqliteConnection connection, ApplicationDBContext db) { this.connection = connection; Db = db; }
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False"); await connection.OpenAsync();
            var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync(); return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}

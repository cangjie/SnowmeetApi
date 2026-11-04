using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RentalCloseReturnTimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseOrder_uses_latest_valid_return_across_rentals_even_when_refunded(bool refunded)
    {
        await using var db = await CreateOrderAsync(refunded);
        db.rental.Add(new Rental { id = 12, order_id = 1, valid = 1, settled = 1 });
        db.rentItem.AddRange(
            new RentItem { id = 20, rental_id = 2, valid = 1 },
            new RentItem { id = 21, rental_id = 12, valid = 1 },
            new RentItem { id = 22, rental_id = 12, valid = 0 });
        db.rentItemLog.AddRange(
            new RentItemLog { id = 30, rent_item_id = 20, status = "已归还", valid = 1, create_date = new DateTime(2026, 10, 28, 12, 0, 0) },
            new RentItemLog { id = 31, rent_item_id = 20, status = "已归还", valid = 1, create_date = new DateTime(2026, 10, 28, 16, 0, 0) },
            new RentItemLog { id = 32, rent_item_id = 21, status = "已归还", valid = 1, create_date = new DateTime(2026, 10, 29, 18, 30, 0) },
            new RentItemLog { id = 33, rent_item_id = 21, status = "已归还", valid = 0, create_date = new DateTime(2026, 11, 2) },
            new RentItemLog { id = 34, rent_item_id = 21, status = "已发放", valid = 1, create_date = new DateTime(2026, 11, 3) },
            new RentItemLog { id = 35, rent_item_id = 22, status = "已归还", valid = 1, create_date = new DateTime(2026, 11, 4) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new RentController(db, Config, new HttpContextAccessor()).CloseOrder();

        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(1, order.closed);
        Assert.Equal(new DateTime(2026, 10, 29, 18, 30, 0), order.close_date);
    }

    [Fact]
    public async Task CloseOrder_without_return_records_keeps_refund_time_fallback()
    {
        await using var db = await CreateOrderAsync(refunded: true);
        await new RentController(db, Config, new HttpContextAccessor()).CloseOrder();
        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        Assert.Equal(1, order.closed);
        Assert.Equal(new DateTime(2026, 10, 30, 10, 0, 0), order.close_date);
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();

    private static async Task<ApplicationDBContext> CreateOrderAsync(bool refunded)
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        await db.Database.EnsureCreatedAsync();
        var paid = refunded ? 500 : 70;
        db.order.Add(new Order { id = 1, code = "TEST", type = "租赁", shop = "测试店", valid = 1,
            create_date = new DateTime(2026, 10, 28) });
        db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = 1, guaranty = paid });
        db.rentalDetail.Add(new RentalDetail { id = 3, rental_id = 2, charge_type = "租金", amount = 70, valid = 1 });
        db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = paid, pay_method = "微信支付", status = "支付成功" });
        db.guaranty.Add(new Guaranty { id = 6, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = paid });
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 6, payment_id = 4 });
        if (refunded)
            db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { id = 5, order_id = 1, payment_id = 4,
                amount = 430, refund_id = "refunded", create_date = new DateTime(2026, 10, 30, 10, 0, 0) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}

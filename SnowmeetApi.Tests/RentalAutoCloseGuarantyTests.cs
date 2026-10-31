using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RentalAutoCloseGuarantyTests
{
    [Theory]
    [InlineData(430, 1, null, 0, 1)]
    [InlineData(400, 1, null, 30, 0)]
    [InlineData(430, 0, null, 0, 0)]
    [InlineData(430, 1, 10.0, 0, 0)]
    public async Task CloseOrder_uses_paid_deposit_and_preserves_other_guards(double refunded, int settled,
        double? stillOwed, double remaining, int expectedClosed)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        await using var db = new ApplicationDBContext(options);
        await db.Database.EnsureCreatedAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Settings:AppId"] = "local-test-app" }).Build();
        var http = new HttpContextAccessor();
        db.order.Add(new Order { id = 1, code = "WT_ZL_261028_00001", type = "租赁", shop = "测试店", valid = 1,
            paying_amount = stillOwed, create_date = new DateTime(2026, 10, 28) });
        db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = settled, guaranty = 500 });
        db.rentalDetail.Add(new RentalDetail { id = 3, rental_id = 2, charge_type = "租金", amount = 70, valid = 1 });
        db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = 500, pay_method = "微信支付", status = "支付成功" });
        db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { id = 5, order_id = 1, payment_id = 4,
            amount = refunded, refund_id = "refunded" });
        db.guaranty.Add(new Guaranty { id = 6, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = 500 });
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 6, payment_id = 4 });
        db.orderPayment.Add(new OrderPayment { id = 7, order_id = 1, amount = 200, pay_method = "微信支付", status = "待支付" });
        db.guaranty.Add(new Guaranty { id = 8, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = 200 });
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 8, payment_id = 7 });
        db.guaranty.Add(new Guaranty { id = 9, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 0, amount = 999 });
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 9, payment_id = 4 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var order = await new OrderController(db, config, http).GetOrder(1);
        Assert.Equal(500, order.rentProperties!.totalPaidGuarantyAmount);
        Assert.Equal(remaining, order.totalRentUnRefund);
        await new RentController(db, config, http).CloseOrder();
        Assert.Equal(expectedClosed, (await db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
    }
}

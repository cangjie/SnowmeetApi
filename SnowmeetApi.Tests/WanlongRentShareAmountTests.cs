using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 万龙应分 = 租金明细里「租金 − 减免」合计 ÷ 2，再扣优惠券（券后分，券只扣一次）；
// 超时费、赔偿金及挂在它们上面的减免、已免除那天的减免、招待/体验租赁都不进分账基数。
// 用了优惠券的租赁单照常生成分账记录，但标成不自动分账，夜间自动分账跳过，只能手动分。
public class WanlongRentShareAmountTests
{
    [Theory]
    [InlineData("截图单：两天租金，首日减免 40", 180)]
    [InlineData("优惠券只扣一次", 155)]
    [InlineData("超时费上的减免不扣租金", 180)]
    [InlineData("赔偿金不算租金", 180)]
    [InlineData("免除那天的减免不扣", 90)]
    [InlineData("体验租赁不进基数", 180)]
    [InlineData("招待租赁不进基数", 180)]
    public async Task CreateOrderShares_uses_rent_after_discounts_and_coupons(string scenario, double expectedShare)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        SeedTwoDayRental(db, secondDayValid: scenario != "免除那天的减免不扣");

        switch (scenario)
        {
            case "优惠券只扣一次":
                db.Set<Discount>().Add(new Discount { id = 21, amount = 50, order_id = 1, biz_type = "租赁", biz_id = 2,
                    ticket_code = "123456789", valid = 1 });
                break;
            case "超时费上的减免不扣租金":
                db.rentalDetail.Add(new RentalDetail { id = 12, rental_id = 2, charge_type = "超时费", amount = 100,
                    valid = 1, rental_date = new DateTime(2026, 10, 31) });
                db.Set<Discount>().Add(new Discount { id = 22, amount = 100, order_id = 1, biz_type = "租赁", biz_id = 2,
                    sub_biz_type = "日租金", sub_biz_id = 12, valid = 1 });
                break;
            case "赔偿金不算租金":
                db.rentalDetail.Add(new RentalDetail { id = 12, rental_id = 2, charge_type = "赔偿金", amount = 300,
                    valid = 1, rental_date = new DateTime(2026, 10, 31) });
                break;
            case "免除那天的减免不扣":
                db.Set<Discount>().Add(new Discount { id = 23, amount = 30, order_id = 1, biz_type = "租赁", biz_id = 2,
                    sub_biz_type = "日租金", sub_biz_id = 11, valid = 1 });
                break;
            case "体验租赁不进基数":
                db.rental.Add(new Rental { id = 3, order_id = 1, valid = 1, settled = 1, experience = true });
                db.rentalDetail.Add(new RentalDetail { id = 13, rental_id = 3, charge_type = "租金", amount = 100,
                    valid = 1, rental_date = new DateTime(2026, 10, 30) });
                db.Set<Discount>().Add(new Discount { id = 24, amount = 20, order_id = 1, biz_type = "租赁", biz_id = 3,
                    sub_biz_type = "日租金", sub_biz_id = 13, valid = 1 });
                break;
            case "招待租赁不进基数":
                db.rental.Add(new Rental { id = 3, order_id = 1, valid = 1, settled = 1, entertain = true });
                db.rentalDetail.Add(new RentalDetail { id = 13, rental_id = 3, charge_type = "租金", amount = 100,
                    valid = 1, rental_date = new DateTime(2026, 10, 30) });
                break;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var order = await new OrderController(db, Config, Http).GetOrder(1);
        await new OrderShareController(db, Config, Http).CreateOrderShares(order);

        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        Assert.Equal(4, share.relation_id);
        Assert.Equal(expectedShare, share.amount, 2);
    }

    [Theory]
    [InlineData("123456789", 175, true)]
    [InlineData("", 175, false)]      // 历史迁移数据里券号是空串，不算用了优惠券
    [InlineData(null, 200, false)]
    public async Task CreatePaymentShare_marks_coupon_rentals_for_manual_share_only(string? ticketCode,
        double expectedShare, bool expectedManualOnly)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        SeedTwoDayRental(db, secondDayValid: true, withDayDiscount: false);
        if (ticketCode != null)
        {
            db.Set<Discount>().Add(new Discount { id = 21, amount = 50, order_id = 1, biz_type = "租赁", biz_id = 2,
                ticket_code = ticketCode, valid = 1 });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var shareHelper = new OrderShareController(db, Config, Http);
        await shareHelper.CreateOrderShares(await new OrderController(db, Config, Http).GetOrder(1));
        db.ChangeTracker.Clear();   // 生产里应分记录在退款时生成、分账记录在夜间关单时生成，不是同一个上下文
        var orderShare = await db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        await shareHelper.CreatePaymentShare(orderShare);

        var paymentShare = await db.paymentShare.AsNoTracking().SingleAsync(s => s.share_id == orderShare.id);
        Assert.Equal(expectedShare, paymentShare.amount, 2);
        Assert.Equal(expectedManualOnly, paymentShare.can_not_share);
        if (expectedManualOnly)
        {
            Assert.Equal("租赁用了优惠券，不自动分账", paymentShare.memo);
        }
    }

    [Fact]
    public async Task CreatePaymentShare_ignores_coupons_used_on_other_orders()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        SeedTwoDayRental(db, secondDayValid: true, withDayDiscount: false);
        db.order.Add(new Order { id = 5, code = "WT_ZL_261031_00099", type = "租赁", shop = "万龙体验中心", valid = 1,
            create_date = new DateTime(2026, 10, 31) });
        db.rental.Add(new Rental { id = 6, order_id = 5, valid = 1 });
        db.Set<Discount>().Add(new Discount { id = 30, amount = 50, order_id = 5, biz_type = "租赁", biz_id = 6,
            ticket_code = "987654321", valid = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var shareHelper = new OrderShareController(db, Config, Http);
        await shareHelper.CreateOrderShares(await new OrderController(db, Config, Http).GetOrder(1));
        db.ChangeTracker.Clear();
        var orderShare = await db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        await shareHelper.CreatePaymentShare(orderShare);

        var paymentShare = await db.paymentShare.AsNoTracking().SingleAsync(s => s.share_id == orderShare.id);
        Assert.False(paymentShare.can_not_share);
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
    private static readonly IHttpContextAccessor Http = new HttpContextAccessor();

    private static async Task<ApplicationDBContext> CreateDbAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        var db = new ApplicationDBContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    // 万龙体验中心租赁单：微信付 5000；一个租赁，10-30 租金 220（可带减免 40）、10-31 租金 180。
    private static void SeedTwoDayRental(ApplicationDBContext db, bool secondDayValid, bool withDayDiscount = true)
    {
        db.order.Add(new Order { id = 1, code = "WT_ZL_261030_00001", type = "租赁", shop = "万龙体验中心", valid = 1,
            create_date = new DateTime(2026, 10, 30) });
        db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = 5000, pay_method = "微信支付",
            status = "支付成功", valid = 1, out_trade_no = "WT_ZL_261030_00001_ZF_01" });
        db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = 1, guaranty = 5000 });
        db.rentalDetail.Add(new RentalDetail { id = 10, rental_id = 2, charge_type = "租金", amount = 220, valid = 1,
            rental_date = new DateTime(2026, 10, 30) });
        db.rentalDetail.Add(new RentalDetail { id = 11, rental_id = 2, charge_type = "租金", amount = 180,
            valid = secondDayValid ? 1 : 0, rental_date = new DateTime(2026, 10, 31) });
        if (withDayDiscount)
        {
            db.Set<Discount>().Add(new Discount { id = 20, amount = 40, order_id = 1, biz_type = "租赁", biz_id = 2,
                sub_biz_type = "日租金", sub_biz_id = 10, valid = 1 });
        }
    }
}

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
        var options = new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        await using var db = new ApplicationDBContext(options);
        await db.Database.EnsureCreatedAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Settings:AppId"] = "local-test-app" }).Build();
        var http = new HttpContextAccessor();

        db.order.Add(new Order { id = 1, code = "WT_ZL_261030_00001", type = "租赁", shop = "万龙体验中心", valid = 1,
            create_date = new DateTime(2026, 10, 30) });
        db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = 5000, pay_method = "微信支付",
            status = "支付成功", valid = 1 });
        db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = 1, guaranty = 5000 });
        db.rentalDetail.Add(new RentalDetail { id = 10, rental_id = 2, charge_type = "租金", amount = 220, valid = 1,
            rental_date = new DateTime(2026, 10, 30) });
        db.rentalDetail.Add(new RentalDetail { id = 11, rental_id = 2, charge_type = "租金", amount = 180,
            valid = scenario == "免除那天的减免不扣" ? 0 : 1, rental_date = new DateTime(2026, 10, 31) });
        db.Set<Discount>().Add(new Discount { id = 20, amount = 40, order_id = 1, biz_type = "租赁", biz_id = 2,
            sub_biz_type = "日租金", sub_biz_id = 10, valid = 1 });

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

        var order = await new OrderController(db, config, http).GetOrder(1);
        await new OrderShareController(db, config, http).CreateOrderShares(order);

        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        Assert.Equal(4, share.relation_id);
        Assert.Equal(expectedShare, share.amount, 2);
    }
}

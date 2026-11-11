using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-09 用户定：连租（系统生成下一日租金）时，每天的租金超过「预约价」都按预约价算——
// 下一日大于预约价就记预约价，上一日大于预约价的也改成预约价。
// 预约价 = 价目表里同门店、同品类（或套餐）、同场次、同日类型、scene = 预约 的那档价。
public class RentalReservationPriceCapTests
{
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static readonly DateTime Today = DateTime.Now.Date;
    private static readonly DateTime Tomorrow = Today.AddDays(1);
    private static string DayType(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "平日";

    private sealed record Seeded(int RentalId, int ShopDayPriceId, int ReserveDayPriceId, int ShopMultiPriceId, int ReserveMultiPriceId);

    // 今天已起租（首日租金 150，来自今天的价格预设，日场/平日/门市）；价目表里同品类有门市价和预约价。
    private static async Task<Seeded> SeedStartedRental(CoachFixture f, double? reserveDay, double? reserveMulti,
        double shopMulti = 130, bool addManualPresetTomorrow = false)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1, staff_id = 9 };
        db.order.Add(order);
        await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "单板", valid = 0, category_id = 31, staff_id = 9, start_date = Today };
        db.rental.Add(rental);
        await db.SaveChangesAsync();
        var first = new RentItem { rental_id = rental.id, name = "雪鞋", class_name = "雪鞋", noCode = true, atOnce = true, valid = 0 };
        var second = new RentItem { rental_id = rental.id, name = "雪板", class_name = "雪板", noCode = true, atOnce = false, valid = 0 };
        db.rentItem.AddRange(first, second);
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = Today, rent_type = "日场", day_type = "平日", scene = "门市", price = 150 });
        if (addManualPresetTomorrow)
        {
            db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = Tomorrow, rent_type = "多日", day_type = DayType(Tomorrow), scene = "门市", price = 200, manual = true });
        }
        RentPrice P(string scene, string rentType, string dayType, double price) => new()
        {
            type = "分类", category_id = 31, shop_id = 1, scene = scene, rent_type = rentType, day_type = dayType, price = price, valid = 1
        };
        var shopDay = P("门市", "日场", "平日", 150);
        var reserveDayRow = P("预约", "日场", "平日", reserveDay ?? 0);
        var shopMultiRow = P("门市", "多日", DayType(Tomorrow), shopMulti);
        var reserveMultiRow = P("预约", "多日", DayType(Tomorrow), reserveMulti ?? 0);
        db.rentPrice.Add(shopDay);
        db.rentPrice.Add(shopMultiRow);
        if (reserveDay != null) db.rentPrice.Add(reserveDayRow);
        if (reserveMulti != null) db.rentPrice.Add(reserveMultiRow);
        await db.SaveChangesAsync();

        await Rent(f).EffectRental(rental.id, 9);
        await Rent(f).SetRentItemStatus(second.id, "已发放", "session-1");
        return new Seeded(rental.id, shopDay.id, reserveDayRow.id, shopMultiRow.id, reserveMultiRow.id);
    }

    private static async Task<List<RentalDetail>> Rents(CoachFixture f, int rentalId) =>
        await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1 && d.charge_type == "租金")
            .OrderBy(d => d.rental_date).ToListAsync();

    [Fact]
    public async Task 下一日和上一日的租金都超过预约价_都按预约价算()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: 120, reserveMulti: 100);
        var before = await Rents(f, s.RentalId);
        Assert.Equal(150, Assert.Single(before).amount);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, s.RentalId);
        Assert.Equal(new[] { 120d, 100d }, rows.Select(d => d.amount).ToArray());
        // 记录指向实际采用的预约价，方便以后核对
        Assert.Equal(s.ReserveDayPriceId, rows[0].rent_price_id);
        Assert.Equal(s.ReserveMultiPriceId, rows[1].rent_price_id);
    }

    [Fact]
    public async Task 改了上一日的租金要留审计日志()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: 120, reserveMulti: 100);
        var prevId = (await Rents(f, s.RentalId)).Single().id;

        await Rent(f).ContinueRentOrder(Tomorrow);

        var log = await f.Db.coreDataModLog.AsNoTracking().SingleAsync(l => l.table_name == "RentDetail" && l.key_value == prevId);
        Assert.Equal("amount", log.field_name);
        Assert.Equal("150", log.prev_value);
        Assert.Equal("120", log.current_value);
        Assert.Equal("系统续租", log.scene);
    }

    [Fact]
    public async Task 租金没超过预约价_保持原价不动也不写日志()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: 200, reserveMulti: 150);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, s.RentalId);
        Assert.Equal(new[] { 150d, 130d }, rows.Select(d => d.amount).ToArray());
        Assert.Equal(s.ShopDayPriceId, rows[0].rent_price_id);
        Assert.Equal(s.ShopMultiPriceId, rows[1].rent_price_id);
        Assert.False(await f.Db.coreDataModLog.AnyAsync(l => l.table_name == "RentDetail" && l.key_value == rows[0].id));
    }

    [Fact]
    public async Task 只有下一日超过预约价_只改下一日()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: 150, reserveMulti: 100);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 150d, 100d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task 价目表里没有预约价_不封顶()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: null, reserveMulti: null);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 150d, 130d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task 店员手改过价的租赁不封顶()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: 120, reserveMulti: 100, addManualPresetTomorrow: true);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 150d, 200d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task 夜间任务重跑不会重复生成也不会再改价()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveDay: 120, reserveMulti: 100);

        await Rent(f).ContinueRentOrder(Tomorrow);
        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 120d, 100d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
        Assert.Equal(1, await f.Db.coreDataModLog.CountAsync(l => l.table_name == "RentDetail" && l.scene == "系统续租"));
    }
}

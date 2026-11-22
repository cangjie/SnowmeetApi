using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-22 用户定：崇礼旗舰店的租赁订单发生续租时，每天的价格都改为多日价（第一天也改）；
// 只对今天及以后跑续租时命中的订单生效：第一次续租那一刻把第一天改成多日价，已经续租过的订单、历史行不改。
// 实现：RentController.ContinueRentalCore → RepriceFirstDayAtMultiDayPriceAsync（规则说明见 Helpers/ContinueRentRules）。
// 和 2026-11-09 的「连租按预约价封顶」（RentalReservationPriceCapTests）叠加：先改成多日价，再按预约价封顶。
public class ChongliContinueReservePriceTests
{
    private const string Chongli = "崇礼旗舰店";   // CoachFixture 里崇礼旗舰店的 shop.id = 2
    private const string Wanlong = "万龙服务中心";  // shop.id = 1

    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static readonly DateTime Today = DateTime.Now.Date;
    private static readonly DateTime Tomorrow = Today.AddDays(1);
    private static readonly DateTime DayAfter = Today.AddDays(2);

    private static string DayType(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "平日";

    private sealed record Seeded(int RentalId, int OrderId);

    // 今天已起租（首日租金 firstPrice，日场/今天的日类型，firstScene）；价目表里同品类：门市 日场 150、门市 多日 shopMulti，
    // 预约价按参数配（日场 / 多日各一档，null = 不配）。
    private static async Task<Seeded> SeedStartedRental(CoachFixture f, string shop = Chongli, double firstPrice = 150,
        string firstScene = "门市", double? reserveDay = null, double? reserveMulti = null, double shopMulti = 130,
        bool manualPresetTomorrow = false)
    {
        int shopId = shop == Chongli ? 2 : 1;
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = shop, valid = 1, staff_id = 9 };
        db.order.Add(order);
        await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "单板", valid = 0, category_id = 31, staff_id = 9, start_date = Today };
        db.rental.Add(rental);
        await db.SaveChangesAsync();
        var first = new RentItem { rental_id = rental.id, name = "雪鞋", class_name = "雪鞋", noCode = true, atOnce = true, valid = 0 };
        var second = new RentItem { rental_id = rental.id, name = "雪板", class_name = "雪板", noCode = true, atOnce = false, valid = 0 };
        db.rentItem.AddRange(first, second);
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = Today, rent_type = "日场", day_type = DayType(Today), scene = firstScene, price = firstPrice });
        if (manualPresetTomorrow)
        {
            db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = Tomorrow, rent_type = "多日", day_type = DayType(Tomorrow), scene = "门市", price = 200, manual = true });
        }
        RentPrice P(string scene, string rentType, string dayType, double price) => new()
        {
            type = "分类", category_id = 31, shop_id = shopId, scene = scene, rent_type = rentType, day_type = dayType, price = price, valid = 1
        };
        db.rentPrice.Add(P("门市", "日场", DayType(Today), 150));
        foreach (var dayType in new[] { "平日", "周末" })
        {
            db.rentPrice.Add(P("门市", "多日", dayType, shopMulti));
            if (reserveMulti != null) db.rentPrice.Add(P("预约", "多日", dayType, reserveMulti.Value));
        }
        if (reserveDay != null) db.rentPrice.Add(P("预约", "日场", DayType(Today), reserveDay.Value));
        await db.SaveChangesAsync();

        await Rent(f).EffectRental(rental.id, 9);
        await Rent(f).SetRentItemStatus(second.id, "已发放", "session-1");
        return new Seeded(rental.id, order.id);
    }

    private static async Task<List<RentalDetail>> Rents(CoachFixture f, int rentalId) =>
        await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1 && d.charge_type == "租金")
            .OrderBy(d => d.rental_date).ToListAsync();

    private static async Task<List<CoreDataModLog>> PriceLogs(CoachFixture f) =>
        await f.Db.coreDataModLog.AsNoTracking().Where(l => l.table_name == "RentDetail").OrderBy(l => l.id).ToListAsync();

    [Fact]
    public async Task 崇礼旗舰店第一次续租_第一天改成多日价_之后每天也是多日价()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f);
        Assert.Equal(150, Assert.Single(await Rents(f, s.RentalId)).amount);

        await Rent(f).ContinueRentOrder(Tomorrow);
        await Rent(f).ContinueRentOrder(DayAfter);

        Assert.Equal(new[] { 130d, 130d, 130d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task 改第一天要留审计日志_只记一次_后面再续租不会重复改()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f);
        int firstId = (await Rents(f, s.RentalId)).Single().id;

        await Rent(f).ContinueRentOrder(Tomorrow);
        await Rent(f).ContinueRentOrder(DayAfter);

        var log = Assert.Single(await PriceLogs(f));
        Assert.Equal(firstId, log.key_value);
        Assert.Equal("amount", log.field_name);
        Assert.Equal("150", log.prev_value);
        Assert.Equal("130", log.current_value);
        Assert.Equal("系统续租", log.scene);
        Assert.Contains("崇礼旗舰店续租", log.manual_memo);
    }

    [Fact]
    public async Task 已经续租过的订单_第一天和历史行都不动()
    {
        // 今天之前就续租过（已经有一条「系统续租」行）：它不是「今天及以后第一次命中」，已记的行一律不改
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f);
        var db = f.NewContext();
        db.rentalDetail.Add(new RentalDetail { rental_id = s.RentalId, charge_type = "租金", rental_date = Tomorrow,
            amount = 130, scene = "门市", memo = "系统续租", valid = 1, create_date = DateTime.Now });
        await db.SaveChangesAsync();

        await Rent(f).ContinueRentOrder(DayAfter);

        Assert.Equal(new[] { 150d, 130d, 130d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
        Assert.Empty(await PriceLogs(f));
    }

    [Fact]
    public async Task 多日价比第一天还高_不抬高第一天()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, shopMulti: 200);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 150d, 200d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
        Assert.Empty(await PriceLogs(f));
    }

    [Fact]
    public async Task 其它门店不受影响_第一天还是原价()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, shop: Wanlong);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 150d, 130d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
        Assert.Empty(await PriceLogs(f));
    }

    [Fact]
    public async Task 店员手改过价的崇礼租赁_仍以店员为准()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, manualPresetTomorrow: true);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 150d, 200d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
        Assert.Empty(await PriceLogs(f));
    }

    [Fact]
    public async Task 多日价之后再按预约价封顶_叠加生效()
    {
        // 第一天 150 → 多日价 130 → 预约多日价 100 封顶；第二天也是 100
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, reserveMulti: 100);

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(new[] { 100d, 100d }, (await Rents(f, s.RentalId)).Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task 开单选的是预约价_第一天改成预约的多日价()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f, firstPrice: 120, firstScene: "预约", reserveDay: 120, reserveMulti: 100);
        var firstRow = Assert.Single(await Rents(f, s.RentalId));
        Assert.Equal(120, firstRow.amount);
        Assert.Equal("预约", firstRow.scene);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, s.RentalId);
        Assert.Equal(new[] { 100d, 100d }, rows.Select(d => d.amount).ToArray());
        Assert.All(rows, d => Assert.Equal("预约", d.scene));
    }

    [Fact]
    public async Task 查不到多日价_第一天保持原价()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await SeedStartedRental(f);
        await f.Db.rentPrice.Where(p => p.rent_type == "多日").ExecuteDeleteAsync();

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(150, (await Rents(f, s.RentalId)).First().amount);
        Assert.Empty(await PriceLogs(f));
    }
}

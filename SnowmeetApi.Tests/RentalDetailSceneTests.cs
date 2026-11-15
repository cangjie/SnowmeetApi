using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-15 用户要求：租赁开单可以选按门市价还是预约价；价格预设和以后生成的日租金记录都要记下是预约价还是门市价。
//   首日租金：scene 取价格预设的 scene（没有预设按门市）；
//   续租：没有当天预设时沿用上一天租金的场景——预约价的租赁每天都按预约价续；预约价目里查不到那天的价就退回门市价（别记成 0 元），那天记门市；
//   连租按预约价封顶（11-09 规则）：封顶的那天、被改价的上一天都记预约；
//   旧版小程序回传明细没有 scene：不能把库里的值冲成空。
public class RentalDetailSceneTests
{
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static readonly DateTime Today = DateTime.Now.Date;
    private static readonly DateTime Tomorrow = Today.AddDays(1);
    private static string DayType(DateTime d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "平日";

    // 今天起租的租赁（首日价格预设 scene = firstScene，价 150）；价目表里同品类（分类 31，万龙服务中心 shop_id 1）有：
    //   门市 日场/多日、预约 日场（reserveDay）、预约 多日（reserveMulti，null 表示没配）
    private static async Task<int> Seed(CoachFixture f, string firstScene, double? reserveDay = 120, double? reserveMulti = 100,
        bool manual = false)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1, staff_id = 9 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "单板", valid = 0, category_id = 31, staff_id = 9, start_date = Today };
        db.rental.Add(rental); await db.SaveChangesAsync();
        db.rentItem.AddRange(
            new RentItem { rental_id = rental.id, name = "雪鞋", class_name = "雪鞋", noCode = true, atOnce = true, valid = 0 },
            new RentItem { rental_id = rental.id, name = "雪板", class_name = "雪板", noCode = true, atOnce = false, valid = 0 });
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = Today, rent_type = "日场", day_type = "平日",
            scene = firstScene, price = 150, manual = manual });
        RentPrice P(string scene, string rentType, string dayType, double price) => new()
        {
            type = "分类", category_id = 31, shop_id = 1, scene = scene, rent_type = rentType, day_type = dayType, price = price, valid = 1
        };
        db.rentPrice.Add(P("门市", "日场", "平日", 150));
        db.rentPrice.Add(P("门市", "多日", DayType(Tomorrow), 130));
        if (reserveDay != null) db.rentPrice.Add(P("预约", "日场", "平日", reserveDay.Value));
        if (reserveMulti != null) db.rentPrice.Add(P("预约", "多日", DayType(Tomorrow), reserveMulti.Value));
        await db.SaveChangesAsync();

        await Rent(f).EffectRental(rental.id, 9);
        var second = await f.Db.rentItem.AsNoTracking().FirstAsync(i => i.rental_id == rental.id && i.name == "雪板");
        await Rent(f).SetRentItemStatus(second.id, "已发放", "session-1");
        return rental.id;
    }

    private static async Task<List<RentalDetail>> Rents(CoachFixture f, int rentalId) =>
        await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1 && d.charge_type == "租金")
            .OrderBy(d => d.rental_date).ToListAsync();

    [Fact]
    public void SceneRulesOnlyKnowShopReserveAndMemberEverythingElseIsShop()
    {
        Assert.Equal("预约", RentSceneRules.Normalize(" 预约 "));
        Assert.Equal("会员", RentSceneRules.Normalize("会员"));
        foreach (var dirty in new string?[] { null, "", "   ", "'", "门市", "乱写" })
            Assert.Equal("门市", RentSceneRules.Normalize(dirty));
        Assert.True(RentSceneRules.IsReserve("预约"));
        Assert.False(RentSceneRules.IsReserve("会员"));
        Assert.False(RentSceneRules.IsReserve(null));
    }

    [Theory]
    [InlineData("预约")]
    [InlineData("门市")]
    public async Task FirstDayRentRecordsTheScenOfTheSelectedPreset(string scene)
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, scene);

        var first = Assert.Single(await Rents(f, rentalId));
        Assert.Equal(scene, first.scene);
        Assert.Equal(150, first.amount);              // 首日金额以价格预设为准
    }

    [Fact]
    public async Task ContinuedDaysFollowTheReserveSceneAndUseTheReserveCatalogPrice()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, "预约", reserveDay: 150, reserveMulti: 100);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, rentalId);
        Assert.Equal(new[] { "预约", "预约" }, rows.Select(d => d.scene).ToArray());
        Assert.Equal(new[] { 150d, 100d }, rows.Select(d => d.amount).ToArray());   // 次日取的是预约价目的多日价，不是门市 130
    }

    [Fact]
    public async Task ContinuedDaysOfAShopSceneRentalStayShop()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, "门市", reserveDay: null, reserveMulti: null);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, rentalId);
        Assert.Equal(new[] { "门市", "门市" }, rows.Select(d => d.scene).ToArray());
        Assert.Equal(new[] { 150d, 130d }, rows.Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task AReserveRentalWithoutAReserveMultiDayPriceFallsBackToTheShopPriceInsteadOfZero()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, "预约", reserveDay: 150, reserveMulti: null);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, rentalId);
        Assert.Equal(2, rows.Count);
        Assert.Equal("预约", rows[0].scene);
        Assert.Equal("门市", rows[1].scene);   // 预约价目里没有多日价，退回门市价，这天记门市
        Assert.Equal(130, rows[1].amount);
    }

    [Fact]
    public async Task TheDayCappedAtTheReservePriceAndTheCappedPreviousDayAreRecordedAsReserve()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        // 门市价开单：首日 150（预设）、次日门市多日价 130；预约价 日场 120 / 多日 100 → 两天都被封顶到预约价
        int rentalId = await Seed(f, "门市", reserveDay: 120, reserveMulti: 100);

        await Rent(f).ContinueRentOrder(Tomorrow);

        var rows = await Rents(f, rentalId);
        Assert.Equal(new[] { 120d, 100d }, rows.Select(d => d.amount).ToArray());
        Assert.Equal(new[] { "预约", "预约" }, rows.Select(d => d.scene).ToArray());
    }

    [Fact]
    public async Task RerunningTheNightlyJobDoesNotChangeTheRecordedScenes()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, "预约", reserveDay: 150, reserveMulti: 100);
        await Rent(f).ContinueRentOrder(Tomorrow);
        var before = (await Rents(f, rentalId)).Select(d => (d.id, d.scene, d.amount)).ToList();

        await Rent(f).ContinueRentOrder(Tomorrow);

        Assert.Equal(before, (await Rents(f, rentalId)).Select(d => (d.id, d.scene, d.amount)).ToList());
    }

    [Fact]
    public async Task AnOldClientPostingDetailsWithoutSceneDoesNotWipeTheStoredScene()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, "预约");
        var stored = Assert.Single(await Rents(f, rentalId));
        Assert.Equal("预约", stored.scene);

        // 旧版小程序：把明细原样回传，但没有 scene 字段，只改了金额
        var posted = new RentalDetail { id = stored.id, rental_id = rentalId, charge_type = "租金", rental_date = stored.rental_date,
            rent_price_id = stored.rent_price_id, amount = 99, memo = stored.memo, staff_id = stored.staff_id, valid = 1,
            create_date = stored.create_date, scene = null };
        await Rent(f).UpdateRentalDetails(rentalId, new List<RentalDetail> { posted }, 9, "租赁订单详细页修改租金");

        var after = Assert.Single(await Rents(f, rentalId));
        Assert.Equal(99, after.amount);
        Assert.Equal("预约", after.scene);
    }

    // 订单详情页「改租金」走的是 HTTP 接口 UpdateRentalDetails（整行标 Modified），和上面的内部方法是两条路径
    [Fact]
    public async Task AnOldClientPostingThroughTheHttpEndpointDoesNotWipeTheStoredSceneEither()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        int rentalId = await Seed(f, "预约");
        var stored = Assert.Single(await Rents(f, rentalId));

        var posted = new RentalDetail { id = stored.id, rental_id = rentalId, charge_type = "租金", rental_date = stored.rental_date,
            rent_price_id = stored.rent_price_id, amount = 88, memo = stored.memo, staff_id = stored.staff_id, valid = 1,
            create_date = stored.create_date, scene = null, _filledDiscountAmount = 0 };
        await Rent(f).UpdateRentalDetails(new List<RentalDetail> { posted }, "租赁订单详细页修改租金", "session-1");

        var after = Assert.Single(await Rents(f, rentalId));
        Assert.Equal(88, after.amount);
        Assert.Equal("预约", after.scene);
        // 新版小程序带着 scene 回传：按回传的值保存
        posted.scene = "门市"; posted.amount = 77;
        await Rent(f).UpdateRentalDetails(new List<RentalDetail> { posted }, "租赁订单详细页修改租金", "session-1");
        var again = Assert.Single(await Rents(f, rentalId));
        Assert.Equal(77, again.amount);
        Assert.Equal("门市", again.scene);
    }
}

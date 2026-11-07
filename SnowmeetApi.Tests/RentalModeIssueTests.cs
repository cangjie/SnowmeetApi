using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 租赁模式和发放：
///   延时租赁（atOnce=true、start_date=明天）：装备当晚发放，明天才开始计费，今天不产生租金记录；
///   先租后取（atOnce=false）：装备稍后发放，全部领取后计租。
/// 2026-11-01 起小程序开单页按这个口径发 atOnce（tests/rent_mode_at_once.test.js 钉住前端那一半），和旧版一致。
/// </summary>
public class RentalModeIssueTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private const int CategoryId = 31;
    private const int PackageId = 41;

    /// <summary>一单一个租赁，一件装备；开单页只给「今天」建了价格预设（和小程序创建租赁时一样）。</summary>
    private static async Task<(int rentalId, int itemId)> NewRental(CoachFixture f, bool atOnce, DateTime start, string pickType = "延时租赁", bool package = false)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "租赁", shop = "万龙服务中心", member_id = CoachFixture.StudentA, valid = 1, staff_id = 9 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental
        {
            order_id = order.id, name = "雪板套餐", valid = 1, staff_id = 9, start_date = start,
            category_id = CategoryId, package_id = package ? PackageId : null, pick_type = pickType,
        };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, name = "雪板", class_name = "双板", noCode = true, atOnce = atOnce, valid = 0, category_id = CategoryId, pick_type = pickType };
        db.rentItem.Add(item);
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = DateTime.Now.Date, rent_type = "夜场", day_type = "平日", scene = "门市", price = 150 });
        await db.SaveChangesAsync();
        return (rental.id, item.id);
    }

    /// <summary>价目表：分类价 200、套餐价 260（平日/周末都有，测试不受今天星期几影响）。</summary>
    private static async Task SeedPrices(CoachFixture f)
    {
        var db = f.NewContext();
        foreach (string dayType in new[] { "平日", "周末" })
        {
            db.rentPrice.Add(new RentPrice { type = "分类", category_id = CategoryId, shop_id = 1, shop = "万龙服务中心", day_type = dayType, rent_type = "日场", scene = "门市", price = 200, valid = 1 });
            db.rentPrice.Add(new RentPrice { type = "套餐", package_id = PackageId, shop_id = 1, shop = "万龙服务中心", day_type = dayType, rent_type = "日场", scene = "门市", price = 260, valid = 1 });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DelayRental_GearIsIssuedTonight_AndNothingIsChargedForToday()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedPrices(f);
        DateTime tomorrow = DateTime.Now.Date.AddDays(1);
        var (rentalId, itemId) = await NewRental(f, atOnce: true, start: tomorrow);

        await Rent(f).EffectRental(rentalId, 9);

        var logs = await f.Db.rentItemLog.AsNoTracking().Where(l => l.rent_item_id == itemId).ToListAsync();
        Assert.Contains(logs, l => l.status == "已发放");                       // 装备当晚发出去
        var details = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync();
        Assert.DoesNotContain(details, d => d.rental_date.Date == DateTime.Now.Date);   // 今天不收租金
    }

    [Fact]
    public async Task DelayRental_WithoutAtOnce_LeavesGearUnissued()
    {
        // 这是改之前小程序对「延时租赁」发出来的样子：装备不发放。保留这条是为了说明发不发放完全由 atOnce 决定，
        // 所以开单页必须发 atOnce=true（tests/rent_delay_at_once.test.js 钉住前端）。
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedPrices(f);
        var (rentalId, itemId) = await NewRental(f, atOnce: false, start: DateTime.Now.Date.AddDays(1));

        await Rent(f).EffectRental(rentalId, 9);

        Assert.DoesNotContain(await f.Db.rentItemLog.AsNoTracking().Where(l => l.rent_item_id == itemId).ToListAsync(), l => l.status == "已发放");
    }

    [Fact]
    public async Task DelayRental_FirstDayChargeIsOnTheStartDate_ForASingleProduct()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedPrices(f);
        DateTime tomorrow = DateTime.Now.Date.AddDays(1);
        var (rentalId, _) = await NewRental(f, atOnce: true, start: tomorrow);

        await Rent(f).EffectRental(rentalId, 9);

        var detail = await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == rentalId && d.valid == 1);
        Assert.Equal(tomorrow, detail.rental_date.Date);
    }

    [Fact]
    public async Task LaterPickupRental_DoesNotChargeBeforeGearIsIssued()
    {
        // 2026-11-06 起所有商品均在全部租赁物领取后计租，先租后取不再提前生成租金。
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedPrices(f);
        var (rentalId, itemId) = await NewRental(f, atOnce: false, start: DateTime.Now, pickType: "先租后取");

        await Rent(f).EffectRental(rentalId, 9);

        Assert.DoesNotContain(await f.Db.rentItemLog.AsNoTracking().Where(l => l.rent_item_id == itemId).ToListAsync(), l => l.status == "已发放");
        Assert.Empty(await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task FirstDay_PackageRentalWithoutPresetForThatDate_UsesThePackagePrice()
    {
        // 套餐租赁（开单页的「普通单板套餐」）：起租日（明天）没有价格预设，第一天租金取套餐价 260，不是分类价 200。
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedPrices(f);
        DateTime tomorrow = DateTime.Now.Date.AddDays(1);
        var (rentalId, _) = await NewRental(f, atOnce: true, start: tomorrow, package: true);

        await Rent(f).EffectRental(rentalId, 9);

        var detail = await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == rentalId && d.valid == 1);
        Assert.Equal(tomorrow, detail.rental_date.Date);
        Assert.Equal(260, detail.amount);
    }

    [Fact]
    public async Task FirstDay_CatalogPriceIgnoresOtherShopsAndInvalidRows()
    {
        // 别的门店的价、已作废的价不能被当成这单的价；单品只按分类取价。
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        foreach (string dayType in new[] { "平日", "周末" })
        {
            db.rentPrice.Add(new RentPrice { type = "分类", category_id = CategoryId, shop_id = 2, shop = "崇礼旗舰店", day_type = dayType, rent_type = "日场", scene = "门市", price = 999, valid = 1 });
            db.rentPrice.Add(new RentPrice { type = "分类", category_id = CategoryId, shop_id = 1, shop = "万龙服务中心", day_type = dayType, rent_type = "日场", scene = "门市", price = 888, valid = 0 });
            db.rentPrice.Add(new RentPrice { type = "分类", category_id = CategoryId + 1, shop_id = 1, shop = "万龙服务中心", day_type = dayType, rent_type = "日场", scene = "门市", price = 777, valid = 1 });
        }
        await db.SaveChangesAsync();
        await SeedPrices(f);
        var (rentalId, _) = await NewRental(f, atOnce: true, start: DateTime.Now.Date.AddDays(1));

        await Rent(f).EffectRental(rentalId, 9);

        Assert.Equal(200, (await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == rentalId && d.valid == 1)).amount);
    }
}

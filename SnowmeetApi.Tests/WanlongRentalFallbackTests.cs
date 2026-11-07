using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class WanlongRentalFallbackTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    [Fact]
    public async Task ServiceCenterUsesExperiencePackagesOnlyWhenItHasNoOwnPackages()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        db.rentPackage.AddRange(
            new RentPackage { id = 1, name = "通用", description = "", valid = 1, shop = null },
            new RentPackage { id = 2, name = "体验", description = "", valid = 1, shop = "万龙体验中心" });
        await db.SaveChangesAsync();
        var controller = new RentController(f.NewContext(), f.Config, Http());
        async Task<List<RentPackage>> Query() => ((ApiResult<List<RentPackage>>)((OkObjectResult)(await controller.GetRentPackageList("万龙服务中心", true)).Result!).Value!).data;
        Assert.Equal(new[] { 1, 2 }, (await Query()).Select(p => p.id).Order().ToArray());
        db.rentPackage.Add(new RentPackage { id = 3, name = "服务", description = "", valid = 1, shop = "万龙服务中心" });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { 1, 3 }, (await Query()).Select(p => p.id).Order().ToArray());
    }

    [Fact]
    public async Task ServiceCenterUsesLocalPriceAndFillsMissingPriceFromExperienceCenter()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        db.shop.Add(new Shop { id = 4, name = "万龙体验中心", code = "wt" });
        db.rentPrice.AddRange(
            new RentPrice { type = "分类", category_id = 31, shop_id = 1, day_type = "平日", rent_type = "日场", scene = "门市", price = 100, valid = 1 },
            new RentPrice { type = "分类", category_id = 31, shop_id = 4, day_type = "平日", rent_type = "日场", scene = "门市", price = 200, valid = 1 },
            new RentPrice { type = "分类", category_id = 31, shop_id = 4, day_type = "周末", rent_type = "日场", scene = "门市", price = 300, valid = 1 });
        await db.SaveChangesAsync();
        var controller = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.GetRentPriceList(1, "分类", 31, "门市", fallbackExperience: true);
        var prices = ((ApiResult<List<RentPrice>>)((OkObjectResult)result.Result!).Value!).data;
        Assert.Equal(100, Assert.Single(prices, p => p.day_type == "平日").price);
        Assert.Equal(300, Assert.Single(prices, p => p.day_type == "周末").price);
        var plain = await controller.GetRentPriceList(1, "分类", 31, "门市");
        Assert.Single(((ApiResult<List<RentPrice>>)((OkObjectResult)plain.Result!).Value!).data);
    }

    [Fact]
    public async Task ServiceCenterFirstDayAndContinuationUseExperiencePricesWhenMissing()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        db.shop.Add(new Shop { id = 4, name = "万龙体验中心", code = "wt" });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, category_id = 31, valid = 0, start_date = DateTime.Now };
        db.rental.Add(rental); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = rental.id, valid = 0, noCode = true, atOnce = true, name = "雪鞋", class_name = "雪鞋" });
        foreach (var day in new[] { "平日", "周末" })
        {
            db.rentPrice.Add(new RentPrice { type = "分类", category_id = 31, shop_id = 4, day_type = day, rent_type = "日场", scene = "门市", price = 180, valid = 1 });
            db.rentPrice.Add(new RentPrice { type = "分类", category_id = 31, shop_id = 4, day_type = day, rent_type = "多日", scene = "门市", price = 120, valid = 1 });
        }
        await db.SaveChangesAsync();
        await new RentController(f.NewContext(), f.Config, Http()).EffectRental(rental.id, 9);
        await new RentController(f.NewContext(), f.Config, Http()).ContinueRentOrder(DateTime.Now.Date.AddDays(1));
        var rows = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rental.id && d.valid == 1).OrderBy(d => d.rental_date).ToListAsync();
        Assert.Equal(new[] { 180d, 120d }, rows.Select(d => d.amount).ToArray());
    }
}

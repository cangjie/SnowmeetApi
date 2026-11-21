using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RentAppendAssociatedItemsTests
{
    [Fact]
    public async Task SaveDraftAcceptsAssociatedCategoryWithIgnoredCycleAndLinksItemToRental()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentCategory.Add(new RentCategory { id = 27, name = "雪杖", code = "0201" });
        var order = new Order { type = "租赁", shop = "万龙体验中心", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var saved = new Rental { order_id = order.id, name = "FIS", valid = 1, appending = true };
        f.Db.rental.Add(saved); await f.Db.SaveChangesAsync();
        var input = new Rental { id = saved.id, order_id = order.id, name = "FIS", valid = 1, appending = true,
            rentItems = new List<RentItem> { new RentItem { id = 0, rental_id = 0, category_id = 27, is_associate = true,
                valid = 1, noCode = true, class_name = "雪杖", category = new RentCategory {
                    id = 27, name = "雪杖", code = "0201", associateCategories = new List<RentCategoryAssociate> { null! }
                } } } };
        var controller = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        await controller.SaveAppendingRental(input, commit: false);
        var item = await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.rental_id == saved.id);
        Assert.Equal(27, item.category_id);
        Assert.True(item.is_associate);
    }
    [Fact]
    public async Task RepeatedAndConcurrentDraftSavesReuseAssociatedItemAndPricePreset()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = new Order { type = "租赁", shop = "万龙体验中心", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var saved = new Rental { order_id = order.id, name = "FIS", valid = 1, appending = true };
        f.Db.rental.Add(saved); await f.Db.SaveChangesAsync();
        Rental Snapshot() => new() { id = saved.id, order_id = order.id, name = "FIS", valid = 1, appending = true,
            rentItems = new List<RentItem> { new() { id = 0, rental_id = 0, category_id = 27, is_associate = true,
                valid = 1, noCode = true, name = "雪杖", class_name = "雪杖", category = new RentCategory {
                    id = 27, name = "雪杖", code = "0201", associateCategories = new List<RentCategoryAssociate> { null! }
                } } },
            pricePresets = new List<RentalPricePreset> { new() { id = 0, rental_id = 0, rent_date = new DateTime(2026, 11, 21), price = 240, scene = "门市" } } };
        RentController Controller() => new(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        await Controller().SaveAppendingRental(Snapshot(), commit: false);
        await Task.WhenAll(Controller().SaveAppendingRental(Snapshot(), commit: false), Controller().SaveAppendingRental(Snapshot(), commit: false));
        Assert.Single(await f.Db.rentItem.AsNoTracking().Where(i => i.rental_id == saved.id).ToListAsync());
        Assert.Single(await f.Db.rentalPricePreset.AsNoTracking().Where(p => p.rental_id == saved.id).ToListAsync());
        Assert.False(await f.Db.rentItem.AsNoTracking().AnyAsync(i => i.rental_id == 0));
        Assert.False(await f.Db.rentalPricePreset.AsNoTracking().AnyAsync(p => p.rental_id == 0));
    }
    [Fact]
    public async Task SaveAppendingsPersistsFisDraftThenConfirmsWithoutDuplicateItems()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentCategory.AddRange(new RentCategory { id = 4, name = "FIS", code = "0102" },
            new RentCategory { id = 27, name = "雪杖", code = "0201" });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1, dealed = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var saved = new Rental { order_id = order.id, name = "", valid = 1, appending = true, guaranty = 0, guaranty_discount = 0 };
        f.Db.rental.Add(saved); await f.Db.SaveChangesAsync();
        var main = new RentItem { rental_id = saved.id, valid = 1, noCode = true };
        f.Db.rentItem.Add(main); await f.Db.SaveChangesAsync();
        Rental Snapshot() => new() { id = saved.id, order_id = order.id, category_id = 4, name = "FIS", valid = 1, appending = true,
            guaranty = 5000, guaranty_discount = 0,
            rentItems = new List<RentItem> {
                new() { id = main.id, rental_id = saved.id, valid = 1, category_id = 4, class_name = "FIS", noCode = true },
                new() { id = 0, rental_id = 0, valid = 1, category_id = 27, is_associate = true, noCode = true, class_name = "雪杖",
                    category = new RentCategory { id = 27, name = "雪杖", code = "0201", associateCategories = new List<RentCategoryAssociate> { null! } } }
            },
            pricePresets = new List<RentalPricePreset> { new() { rent_date = new DateTime(2026, 11, 21), price = 300, scene = "门市" } } };
        RentController Controller() => new(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var first = await Controller().SaveAppendings(order.id, new List<Rental> { Snapshot() }, "session-7", commit: false);
        Assert.Equal(0, ((ApiResult<Order>)((OkObjectResult)first.Result!).Value!).code);
        var again = await Controller().SaveAppendings(order.id, new List<Rental> { Snapshot() }, "session-7", commit: false);
        Assert.Equal(0, ((ApiResult<Order>)((OkObjectResult)again.Result!).Value!).code);
        var commit = await Controller().SaveAppendings(order.id, new List<Rental> { Snapshot() }, "session-7", commit: true);
        Assert.Equal(0, ((ApiResult<Order>)((OkObjectResult)commit.Result!).Value!).code);
        Assert.Equal(2, await f.Db.rentItem.AsNoTracking().CountAsync(i => i.rental_id == saved.id && i.valid == 1));
        Assert.Single(await f.Db.rentalPricePreset.AsNoTracking().Where(p => p.rental_id == saved.id).ToListAsync());
        Assert.Equal(5000, (await f.Db.guaranty.AsNoTracking().SingleAsync(g => g.biz_id == saved.id)).amount);
    }
}

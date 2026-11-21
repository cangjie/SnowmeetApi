using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Rent;

namespace SnowmeetApi.Tests;

public class RentProductAvailabilityTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    [Fact]
    public void OpenBoundsAndEndBoundaryAreHandled()
    {
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        Assert.True(RentProductAvailability.IsActive(null, null, now));
        Assert.True(RentProductAvailability.IsActive(null, now.AddMinutes(1), now));
        Assert.True(RentProductAvailability.IsActive(now, null, now));
        Assert.False(RentProductAvailability.IsActive(null, now, now));
        Assert.False(RentProductAvailability.IsActive(now.AddMinutes(1), null, now));
    }

    [Fact]
    public async Task EffectiveStatusRespectsUnderlyingStatusAndNeverPersistsProjection()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.AddRange(new RentProduct { id = 101, name = "空状态", valid = 1, category_id = 1 },
            new RentProduct { id = 102, name = "在租", valid = 1, category_id = 1, status = "租赁中" },
            new RentProduct { id = 103, name = "正常", valid = 1, category_id = 1, status = "正常" });
        f.Db.rentProductUnavailability.AddRange(new RentProductUnavailability { rent_product_id = 101 },
            new RentProductUnavailability { rent_product_id = 102 },
            new RentProductUnavailability { rent_product_id = 103, end_time = DateTime.Now.AddDays(-1) });
        await f.Db.SaveChangesAsync();
        var db = f.NewContext();
        var products = await db.rentProduct.AsNoTracking().OrderBy(p => p.id).ToListAsync();
        await new RentProductAvailability(db).ApplyAsync(products, DateTime.Now);
        Assert.Equal(new[] { "不可用", "租赁中", "正常" }, products.Select(p => p.status));
        Assert.Null((await f.Db.rentProduct.AsNoTracking().SingleAsync(p => p.id == 101)).status);
    }

    [Fact]
    public async Task ManagementValidatesAndCanUpdateAndDeletePeriods()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.Add(new RentProduct { id = 101, name = "雪板", valid = 1, category_id = 1 });
        await f.Db.SaveChangesAsync();
        var controller = new RentController(f.NewContext(), f.Config, Http());
        var now = DateTime.Now;
        var invalid = await controller.AddRentProductUnavailability(101, new() { start_time = now, end_time = now }, "session-1");
        Assert.Equal(1, ((ApiResult<RentProductUnavailability>)((OkObjectResult)invalid.Result!).Value!).code);
        var denied = await controller.AddRentProductUnavailability(101, new(), "session-7");
        Assert.Equal(1, ((ApiResult<RentProductUnavailability>)((OkObjectResult)denied.Result!).Value!).code);
        var added = await controller.AddRentProductUnavailability(101, new(), "session-1");
        var record = ((ApiResult<RentProductUnavailability>)((OkObjectResult)added.Result!).Value!).data!;
        Assert.True(await new RentProductAvailability(f.NewContext()).IsBlockedAsync(101, null, now));
        var updated = await new RentController(f.NewContext(), f.Config, Http()).AddRentProductUnavailability(101,
            new() { id = record.id, start_time = now.AddDays(1) }, "session-1");
        Assert.Equal(0, ((ApiResult<RentProductUnavailability>)((OkObjectResult)updated.Result!).Value!).code);
        Assert.False(await new RentProductAvailability(f.NewContext()).IsBlockedAsync(101, null, now));
        await new RentController(f.NewContext(), f.Config, Http()).DeleteRentProductUnavailability(record.id, "session-1");
        Assert.Equal(0, (await f.Db.rentProductUnavailability.AsNoTracking().SingleAsync(x => x.id == record.id)).valid);
    }

    [Fact]
    public async Task IssuingBlockedItemByBarcodeIsRejected()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.Add(new RentProduct { id = 101, name = "雪板", barcode = "TEST-101", valid = 1, category_id = 1 });
        f.Db.rentProductUnavailability.Add(new RentProductUnavailability { rent_product_id = 101 });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, valid = 1, class_name = "雪板", category_id = 1, code = "TEST-101" };
        f.Db.rentItem.Add(item); await f.Db.SaveChangesAsync();
        var result = await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(item.id, "已发放", "session-1");
        Assert.Equal(1, ((ApiResult<Rental?>)((OkObjectResult)result.Result!).Value!).code);
        Assert.Empty(await f.Db.rentItemLog.AsNoTracking().Where(x => x.rent_item_id == item.id).ToListAsync());
    }
    [Fact]
    public async Task CheckoutRejectsBlockedBarcodeBeforeChangingOrder()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.Add(new RentProduct { id = 101, name = "雪板", barcode = "TEST-101", valid = 1, category_id = 1 });
        f.Db.rentProductUnavailability.Add(new RentProductUnavailability { rent_product_id = 101 });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 0 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, class_name = "雪板", category_id = 1, code = "TEST-101" });
        await f.Db.SaveChangesAsync();
        var result = await new OrderController(f.NewContext(), f.Config, Http()).PlaceRentOrder(order.id, "session-1");
        var response = (ApiResult<Order>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(1, response.code);
        Assert.Equal("该租赁物暂不可用", response.message);
        Assert.Equal(0, (await f.Db.order.AsNoTracking().SingleAsync(x => x.id == order.id)).valid);
        Assert.Empty(await f.Db.guaranty.AsNoTracking().Where(x => x.order_id == order.id).ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckoutRejectsItemRentedAfterSearch(bool useProductId)
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.Add(new RentProduct { id = 101, name = "雪板", barcode = "TEST-101",
            valid = 1, category_id = 1, status = "租赁中" });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 0 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, class_name = "雪板", category_id = 1,
            rent_product_id = useProductId ? 101 : null, code = "TEST-101" });
        await f.Db.SaveChangesAsync();

        var result = await new OrderController(f.NewContext(), f.Config, Http()).PlaceRentOrder(order.id, "session-1");
        var response = (ApiResult<Order>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(1, response.code);
        Assert.Equal("该租赁物暂不可用", response.message);
        Assert.Equal(0, (await f.Db.order.AsNoTracking().SingleAsync(x => x.id == order.id)).valid);
        Assert.Empty(await f.Db.guaranty.AsNoTracking().Where(x => x.order_id == order.id).ToListAsync());
    }

    [Fact]
    public async Task NewRentalAvailabilityChecksStatusAndOnlyCurrentPeriods()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var now = new DateTime(2026, 11, 21, 12, 0, 0);
        f.Db.rentProduct.AddRange(
            new RentProduct { id = 101, name = "空状态", barcode = "A101", valid = 1, category_id = 1 },
            new RentProduct { id = 102, name = "正常", barcode = "A102", valid = 1, category_id = 1, status = "正常" },
            new RentProduct { id = 103, name = "维修", barcode = "A103", valid = 1, category_id = 1, status = "维修" },
            new RentProduct { id = 104, name = "已删除", barcode = "A104", valid = 0, category_id = 1 });
        f.Db.rentProductUnavailability.AddRange(
            new RentProductUnavailability { rent_product_id = 101, start_time = now, end_time = now.AddHours(1) },
            new RentProductUnavailability { rent_product_id = 102, start_time = now.AddHours(1) });
        await f.Db.SaveChangesAsync();
        var availability = new RentProductAvailability(f.NewContext());
        Assert.True(await availability.IsUnavailableForNewRentalAsync(101, "A101", now));
        Assert.False(await availability.IsUnavailableForNewRentalAsync(101, "A101", now.AddHours(1)));
        Assert.False(await availability.IsUnavailableForNewRentalAsync(102, "A102", now));
        Assert.True(await availability.IsUnavailableForNewRentalAsync(null, "A103", now));
        Assert.True(await availability.IsUnavailableForNewRentalAsync(999, "missing", now));
        Assert.True(await availability.IsUnavailableForNewRentalAsync(104, "A104", now));
        Assert.False(await availability.IsUnavailableForNewRentalAsync(null, "unregistered", now));
    }

    [Fact]
    public async Task BecomingUnavailableAfterCheckoutLeavesTheItemUnissued()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.Add(new RentProduct { id = 101, name = "雪板", barcode = "TEST-101", valid = 1, category_id = 1 });
        f.Db.rentProductUnavailability.Add(new RentProductUnavailability { rent_product_id = 101 });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, rent_product_id = 101, valid = 1, atOnce = true,
            class_name = "雪板", category_id = 1, code = "TEST-101" };
        f.Db.rentItem.Add(item); await f.Db.SaveChangesAsync();
        await new RentController(f.NewContext(), f.Config, Http()).EffectRental(rental.id, 9);
        var saved = await f.Db.rentItem.AsNoTracking().SingleAsync(x => x.id == item.id);
        Assert.False(saved.atOnce);
        Assert.Empty(await f.Db.rentItemLog.AsNoTracking().Where(x => x.rent_item_id == item.id).ToListAsync());
    }
    [Fact]
    public async Task ExistingCustomerCanResumeFromStorageDuringUnavailablePeriod()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.rentProduct.Add(new RentProduct { id = 101, name = "雪板", barcode = "TEST-101", valid = 1, category_id = 1, status = "租赁中" });
        f.Db.rentProductUnavailability.Add(new RentProductUnavailability { rent_product_id = 101 });
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, rent_product_id = 101, valid = 1, class_name = "雪板", category_id = 1, code = "TEST-101" };
        f.Db.rentItem.Add(item); await f.Db.SaveChangesAsync();
        f.Db.rentItemLog.AddRange(new RentItemLog { rent_item_id = item.id, status = "已发放", valid = 1 },
            new RentItemLog { rent_item_id = item.id, status = "已暂存", valid = 1 });
        await f.Db.SaveChangesAsync();
        var result = await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(item.id, "已发放", "session-1");
        Assert.Equal(0, ((ApiResult<Rental?>)((OkObjectResult)result.Result!).Value!).code);
    }
}

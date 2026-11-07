using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class CrossShopRentalTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    [Fact]
    public async Task InvalidCrossShopDestinationIsRejectedBeforeSaving()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = new Order { shop = "万龙服务中心", type = "租赁", rentals = new List<Rental> {
            new Rental { rentItems = new List<RentItem> { new RentItem { pick_type = "异店租赁", pickup_shop = "崇礼旗舰店" } } }
        } };
        var result = await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(order, "session-1");
        Assert.Equal(1, ((ApiResult<Order?>)((OkObjectResult)result.Result!).Value!).code);
        Assert.Empty(await f.Db.order.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RelatedShopListFindsBothOriginAndDestinationButNotUnrelatedShop()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = rental.id, valid = 1, class_name = "雪鞋", pickup_shop = "万龙体验中心" });
        await db.SaveChangesAsync();
        var draftOrder = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(draftOrder); await db.SaveChangesAsync();
        var draft = new Rental { order_id = draftOrder.id, valid = 1, appending = true };
        db.rental.Add(draft); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = draft.id, valid = 1, class_name = "草稿", pickup_shop = "万龙体验中心" });
        await db.SaveChangesAsync();
        var controller = new OrderController(f.NewContext(), f.Config, Http());
        async Task<List<Order>> Query(string shop) => await controller.GetCommonOrders(null, shop, null, null, "租赁", null, null, crossShopOnly: true);
        Assert.Contains(await Query("崇礼旗舰店"), o => o.id == order.id && o.hasCrossShopRent);
        Assert.Contains(await Query("万龙体验中心"), o => o.id == order.id);
        Assert.DoesNotContain(await Query("万龙体验中心"), o => o.id == draftOrder.id);
        Assert.DoesNotContain(await Query("万龙服务中心"), o => o.id == order.id);
    }

    [Fact]
    public async Task CrossShopItemCanBeAssignedOnlyBeforeIssuing()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, valid = 1, class_name = "雪鞋", category_id = 31, pickup_shop = "万龙体验中心" };
        db.rentItem.Add(item);
        db.rentProduct.Add(new RentProduct { id = 310, category_id = 31, name = "雪鞋 A", barcode = "A310", valid = 1, status = "正常" });
        await db.SaveChangesAsync();
        var rent = new RentController(f.NewContext(), f.Config, Http());
        var result = await rent.AssignCrossShopItem(item.id, new RentController.AssignCrossShopItemRequest { productId = 310 }, "session-1");
        Assert.Equal(0, ((ApiResult<Rental?>)((OkObjectResult)result.Result!).Value!).code);
        var saved = await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.id == item.id);
        Assert.Equal("A310", saved.code);
        Assert.Equal("万龙体验中心", saved.pickup_shop);
        await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(item.id, "已发放", "session-1");
        var issued = await f.Db.rentItemLog.AsNoTracking().SingleAsync(l => l.rent_item_id == item.id && l.status == "已发放");
        Assert.Equal(9, issued.staff_id);
        Assert.Equal("租赁中", (await f.Db.rentProduct.AsNoTracking().SingleAsync(p => p.id == 310)).status);
        await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(item.id, "已暂存", "session-1");
        var again = await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(item.id, "已发放", "session-1");
        Assert.Equal(0, ((ApiResult<Rental?>)((OkObjectResult)again.Result!).Value!).code);
    }

    [Fact]
    public async Task CrossShopAssignmentAcceptsTheSelectedCategorysChildProduct()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        db.rentCategory.AddRange(new RentCategory { id = 31, code = "03", name = "雪鞋" },
            new RentCategory { id = 32, code = "0301", name = "双板鞋" });
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, valid = 1, class_name = "雪鞋", category_id = 31, pickup_shop = "万龙体验中心" };
        db.rentItem.Add(item);
        db.rentProduct.Add(new RentProduct { id = 320, category_id = 32, name = "双板鞋 A", barcode = "A320", valid = 1, status = "正常" });
        await db.SaveChangesAsync();
        var result = await new RentController(f.NewContext(), f.Config, Http()).AssignCrossShopItem(item.id,
            new RentController.AssignCrossShopItemRequest { productId = 320 }, "session-1");
        Assert.Equal(0, ((ApiResult<Rental?>)((OkObjectResult)result.Result!).Value!).code);
        Assert.Equal(32, (await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.id == item.id)).category_id);
        var scanned = new RentItem { rental_id = rental.id, valid = 1, class_name = "雪鞋", category_id = 31,
            pickup_shop = "万龙体验中心", code = "A320" };
        db.rentItem.Add(scanned);
        await db.SaveChangesAsync();
        var issueResult = await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(scanned.id, "已发放", "session-1");
        Assert.Equal(0, ((ApiResult<Rental?>)((OkObjectResult)issueResult.Result!).Value!).code);
        var persisted = await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.id == scanned.id);
        Assert.Equal(32, persisted.category_id);
        Assert.Equal(320, persisted.rent_product_id);
    }
}

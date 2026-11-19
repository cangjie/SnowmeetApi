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

    // 2026-11-19 用户：异店租赁的租赁物开单只选分类就能下单结算，但不录入编码（带码）/名称（无码）不允许发放。
    private static async Task<(CoachFixture f, int itemId)> SeedItemAsync(bool noCode, string? pickupShop, string? pickType, string? code = null, string? name = null)
    {
        var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, valid = 1, class_name = "雪鞋", category_id = 31, noCode = noCode,
            pickup_shop = pickupShop, pick_type = pickType, code = code, name = name };
        db.rentItem.Add(item);
        db.rentProduct.Add(new RentProduct { id = 310, category_id = 31, name = "雪鞋 A", barcode = "A310", valid = 1, status = "正常" });
        await db.SaveChangesAsync();
        return (f, item.id);
    }

    private static int CodeOf(ActionResult<ApiResult<Rental?>> result) => ((ApiResult<Rental?>)((OkObjectResult)result.Result!).Value!).code;

    [Theory]
    [InlineData(false, "万龙体验中心", "异店租赁")]   // 带码的异店租赁物，没录入编码
    [InlineData(true, "万龙体验中心", "异店租赁")]    // 无码的异店租赁物，没录入名称
    [InlineData(false, null, "异店租赁")]            // 租赁模式是异店租赁但领取门店空（也按异店处理）
    [InlineData(true, "万龙服务中心", null)]          // 只有领取门店、模式字段空
    public async Task A_cross_shop_item_without_a_code_or_name_cannot_be_issued(bool noCode, string? pickupShop, string? pickType)
    {
        var (f, itemId) = await SeedItemAsync(noCode, pickupShop, pickType);
        await using var _ = f;

        var result = await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(itemId, "已发放", "session-1");

        Assert.Equal(1, CodeOf(result));
        Assert.Empty(await f.Db.rentItemLog.AsNoTracking().Where(l => l.rent_item_id == itemId && l.status == "已发放").ToListAsync());
    }

    [Fact]
    public async Task A_cross_shop_item_with_only_whitespace_as_name_is_still_not_issuable()
    {
        var (f, itemId) = await SeedItemAsync(noCode: true, "万龙体验中心", "异店租赁", name: "   ");
        await using var _ = f;

        Assert.Equal(1, CodeOf(await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(itemId, "已发放", "session-1")));
    }

    [Fact]
    public async Task A_no_code_cross_shop_item_can_be_issued_once_it_has_a_name()
    {
        var (f, itemId) = await SeedItemAsync(noCode: true, "万龙体验中心", "异店租赁");
        await using var _ = f;
        var rent = new RentController(f.NewContext(), f.Config, Http());
        Assert.Equal(1, CodeOf(await rent.SetRentItemStatus(itemId, "已发放", "session-1")));

        Assert.Equal(0, CodeOf(await new RentController(f.NewContext(), f.Config, Http())
            .AssignCrossShopItem(itemId, new RentController.AssignCrossShopItemRequest { name = "雪鞋 红色 42" }, "session-1")));
        Assert.Equal(0, CodeOf(await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(itemId, "已发放", "session-1")));
        Assert.Single(await f.Db.rentItemLog.AsNoTracking().Where(l => l.rent_item_id == itemId && l.status == "已发放").ToListAsync());
    }

    // 本店的无码物品不受影响（只有异店租赁的才要先录入）
    [Fact]
    public async Task A_normal_item_is_not_affected_by_the_cross_shop_issuing_rule()
    {
        var (f, itemId) = await SeedItemAsync(noCode: true, null, "立即租赁");
        await using var _ = f;

        Assert.Equal(0, CodeOf(await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(itemId, "已发放", "session-1")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_reception_draft_with_a_cross_shop_item_that_only_has_a_category_is_saved(bool noCode)
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = new Order
        {
            shop = "崇礼旗舰店", type = "租赁",
            rentals = new List<Rental>
            {
                new Rental { name = "单板", pick_type = "异店租赁", rentItems = new List<RentItem>
                {
                    new RentItem { class_name = "雪鞋", category_id = 31, noCode = noCode, pick_type = "异店租赁", pickup_shop = "万龙体验中心", atOnce = true }
                } }
            }
        };

        var api = (ApiResult<Order?>)((OkObjectResult)(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(order, "session-1")).Result!).Value!;

        Assert.Equal(0, api.code);
        var saved = await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.rental_id == api.data!.rentals[0].id);
        Assert.Equal("万龙体验中心", saved.pickup_shop);
        Assert.True(string.IsNullOrWhiteSpace(saved.code));
        Assert.True(string.IsNullOrWhiteSpace(saved.name));
        Assert.False(saved.atOnce);   // 异店的装备不当场发放
    }

    // 2026-11-19 用户：无码的异店租赁物录入装备时，也可以改成按编码选择具体的租赁物（选了就改成带码的）。
    [Fact]
    public async Task A_no_code_cross_shop_item_can_be_assigned_by_product_and_becomes_a_coded_item()
    {
        var (f, itemId) = await SeedItemAsync(noCode: true, "万龙体验中心", "异店租赁");
        await using var _ = f;

        var result = await new RentController(f.NewContext(), f.Config, Http())
            .AssignCrossShopItem(itemId, new RentController.AssignCrossShopItemRequest { productId = 310 }, "session-1");

        Assert.Equal(0, CodeOf(result));
        var saved = await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.id == itemId);
        Assert.False(saved.noCode);
        Assert.Equal("A310", saved.code);
        Assert.Equal("雪鞋 A", saved.name);
        Assert.Equal(310, saved.rent_product_id);
        Assert.Equal("万龙体验中心", saved.pickup_shop);
        // 已经是带码的了，可以按带码的流程发放（租赁物变成租赁中）
        Assert.Equal(0, CodeOf(await new RentController(f.NewContext(), f.Config, Http()).SetRentItemStatus(itemId, "已发放", "session-1")));
        Assert.Equal("租赁中", (await f.Db.rentProduct.AsNoTracking().SingleAsync(p => p.id == 310)).status);
    }

    [Fact]
    public async Task A_no_code_item_assigned_with_an_unusable_product_stays_no_code()
    {
        var (f, itemId) = await SeedItemAsync(noCode: true, "万龙体验中心", "异店租赁");
        await using var _ = f;
        var db = f.NewContext();
        db.rentProduct.AddRange(
            new RentProduct { id = 311, category_id = 99, name = "雪板 B", barcode = "B311", valid = 1, status = "正常" },     // 分类不符
            new RentProduct { id = 312, category_id = 31, name = "雪鞋 C", barcode = "C312", valid = 1, status = "租赁中" });  // 不可用
        await db.SaveChangesAsync();

        foreach (int productId in new[] { 311, 312, 999 })
        {
            Assert.Equal(1, CodeOf(await new RentController(f.NewContext(), f.Config, Http())
                .AssignCrossShopItem(itemId, new RentController.AssignCrossShopItemRequest { productId = productId }, "session-1")));
        }
        var saved = await f.Db.rentItem.AsNoTracking().SingleAsync(i => i.id == itemId);
        Assert.True(saved.noCode);
        Assert.True(string.IsNullOrWhiteSpace(saved.code));
        Assert.Null(saved.rent_product_id);
    }

    [Fact]
    public async Task Assigning_nothing_is_rejected_for_both_kinds_of_cross_shop_items()
    {
        foreach (bool noCode in new[] { true, false })
        {
            var (f, itemId) = await SeedItemAsync(noCode, "万龙体验中心", "异店租赁");
            await using var _ = f;

            Assert.Equal(1, CodeOf(await new RentController(f.NewContext(), f.Config, Http())
                .AssignCrossShopItem(itemId, new RentController.AssignCrossShopItemRequest(), "session-1")));
            // 带码的租赁物只能按编码选，只给名称不行
            Assert.Equal(1, CodeOf(await new RentController(f.NewContext(), f.Config, Http())
                .AssignCrossShopItem(itemId, new RentController.AssignCrossShopItemRequest { name = noCode ? "" : "随便填" }, "session-1")));
        }
    }
}

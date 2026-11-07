using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RentOrderReplacementStatusTests
{
    private static RentItem Item(string? status, int valid = 1, bool noNeed = false) => new()
    {
        valid = valid,
        noNeed = noNeed,
        logs = status == null ? new List<RentItemLog>() : new List<RentItemLog>
        {
            new() { id = 1, status = status, valid = 1 }
        }
    };

    private static Order OrderWith(params RentItem[] items) => new()
    {
        rentals = new List<Rental>
        {
            new() { valid = 1, start_date = DateTime.Now.AddHours(-1), rentItems = items.ToList() }
        }
    };

    [Theory]
    [InlineData("已发放")]
    [InlineData("暂存")]
    [InlineData("已暂存")]
    public void ReplacedItemDoesNotHideIssuedReplacement(string replacementStatus)
    {
        var order = OrderWith(Item("已更换"), Item(replacementStatus));

        Assert.Equal("租赁中", order.rentProperties!.rentStatus);
    }

    [Fact]
    public void ReplacedItemDoesNotHideUnissuedReplacement()
    {
        var order = OrderWith(Item("已更换"), Item(null));

        Assert.Equal("未开始", order.rentProperties!.rentStatus);
    }

    [Fact]
    public void OtherUnissuedRequiredItemStillPreventsInProgress()
    {
        var order = OrderWith(Item("已更换"), Item("已发放"), Item(null));

        Assert.Equal("未开始", order.rentProperties!.rentStatus);
    }

    [Fact]
    public void InvalidOrUnneededItemsDoNotBlockIssuedItems()
    {
        var order = OrderWith(Item("已发放"), Item(null, valid: 0), Item(null, noNeed: true));

        Assert.Equal("租赁中", order.rentProperties!.rentStatus);
    }

    [Fact]
    public void FutureStartAndSettledOrderKeepTheirExistingStatuses()
    {
        var order = OrderWith(Item("已更换"), Item("已发放"));
        order.rentals[0].start_date = DateTime.Now.AddDays(1);
        Assert.Equal("未开始", order.rentProperties!.rentStatus);

        order.rentals[0].start_date = DateTime.Now.AddHours(-1);
        order.rentals[0].settled = 1;
        Assert.Equal("全部归还", order.rentProperties!.rentStatus);
    }

    [Fact]
    public async Task OrderListLoadsReplacementAndMatchesInProgressFilter()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙体验中心", valid = 1, biz_date = DateTime.Now };
        db.order.Add(order);
        await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1, start_date = DateTime.Now.AddHours(-1) };
        db.rental.Add(rental);
        await db.SaveChangesAsync();
        var original = new RentItem { rental_id = rental.id, valid = 1, name = "原租赁物" };
        var replacement = new RentItem { rental_id = rental.id, valid = 1, name = "更换后租赁物" };
        db.rentItem.AddRange(original, replacement);
        await db.SaveChangesAsync();
        db.rentItemLog.AddRange(
            new RentItemLog { rent_item_id = original.id, status = "已发放", valid = 1 },
            new RentItemLog { rent_item_id = original.id, status = "已更换", valid = 1 },
            new RentItemLog { rent_item_id = replacement.id, status = "已发放", valid = 1 });
        db.rentalDetail.Add(new RentalDetail
        {
            rental_id = rental.id, charge_type = "租金", amount = 180, valid = 1, rental_date = DateTime.Today
        });
        await db.SaveChangesAsync();

        var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var orders = await controller.GetCommonOrders(order.id, null, null, null, "租赁", null, null,
            rentStatus: "租赁中");

        var loaded = Assert.Single(orders);
        Assert.Equal("租赁中", loaded.rentProperties!.rentStatus);
        Assert.Equal(180, Assert.Single(loaded.rentals).totalRentalAmount);
    }
}

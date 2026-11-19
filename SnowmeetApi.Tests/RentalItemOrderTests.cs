using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-19 用户截图：开单时雪板在前、雪杖在后，支付后订单详情页倒过来了。
// 订单详情页用 Rent/GetRental 取每个租赁；GetRental 以前把同一组租赁物按 id 倒序排，改成按开单顺序（id 升序）。
public class RentalItemOrderTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    [Fact]
    public async Task Items_come_back_in_the_order_they_were_entered_at_reception()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        // 开单顺序：双板、雪杖、雪鞋（SaveRentRecept 按列表顺序插入，id 递增）
        db.rentItem.AddRange(
            new RentItem { rental_id = rental.id, valid = 1, class_name = "【FIS】双板", is_associate = false },
            new RentItem { rental_id = rental.id, valid = 1, class_name = "【其他】雪杖", is_associate = true },
            new RentItem { rental_id = rental.id, valid = 1, class_name = "【其他】雪鞋", is_associate = true });
        await db.SaveChangesAsync();

        var result = await new RentController(f.NewContext(), f.Config, Http()).GetRental(rental.id);

        Assert.Equal(new[] { "【FIS】双板", "【其他】雪杖", "【其他】雪鞋" }, result.rentItems.Select(i => i.class_name).ToArray());
    }

    [Fact]
    public async Task Replaced_items_still_go_after_the_current_ones()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var oldBoard = new RentItem { rental_id = rental.id, valid = 1, class_name = "旧双板" };
        var pole = new RentItem { rental_id = rental.id, valid = 1, class_name = "雪杖" };
        db.rentItem.AddRange(oldBoard, pole);
        await db.SaveChangesAsync();
        var newBoard = new RentItem { rental_id = rental.id, valid = 1, class_name = "新双板" };
        db.rentItem.Add(newBoard); await db.SaveChangesAsync();
        oldBoard.next_id = newBoard.id;   // 旧双板被换成新双板
        db.rentItem.Update(oldBoard); await db.SaveChangesAsync();

        var result = await new RentController(f.NewContext(), f.Config, Http()).GetRental(rental.id);

        Assert.Equal(new[] { "雪杖", "新双板", "旧双板" }, result.rentItems.Select(i => i.class_name).ToArray());
    }
}

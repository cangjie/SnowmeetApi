using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RentalPunchDiscountTests
{
    [Fact]
    public async Task PunchingARentalDayRemovesItsDiscountAndReversesItsCardUse()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", member_id = CoachFixture.StudentA, valid = 1, wechat_unverified = true };
        db.order.Add(order);
        await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "雪板", valid = 1, staff_id = 9 };
        var category = new RentCategory { code = "0201", name = "单板", valid = 1 };
        db.rental.Add(rental);
        db.rentCategory.Add(category);
        await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = rental.id, category_id = category.id, valid = 1 });
        var detail = new RentalDetail { rental_id = rental.id, charge_type = "租金", rental_date = DateTime.Today, amount = 200, valid = 1 };
        var tomorrow = new RentalDetail { rental_id = rental.id, charge_type = "租金", rental_date = DateTime.Today.AddDays(1), amount = 100, valid = 1 };
        var card = new PunchCard { member_id = CoachFixture.StudentA, biz_type = "租赁", card_name = "次卡", total = 2, punches = 0 };
        db.rentalDetail.Add(detail);
        db.rentalDetail.Add(tomorrow);
        db.punchCard.Add(card);
        await db.SaveChangesAsync();
        db.discount.Add(new Discount { order_id = order.id, biz_type = "租赁", biz_id = rental.id, sub_biz_type = "日租金", sub_biz_id = detail.id, amount = 100, valid = 1 });
        db.discount.Add(new Discount { order_id = order.id, biz_type = "租赁", biz_id = rental.id, sub_biz_type = "日租金", sub_biz_id = tomorrow.id, amount = 50, valid = 1 });
        db.coachCardUse.Add(new CoachCardUse { card_id = 11, coach_id = f.CoachId, member_id = CoachFixture.StudentA, order_id = order.id,
            biz_type = "租赁", biz_id = rental.id, sub_biz_id = detail.id, mode = "rate", mode_value = .5, base_amount = 200, discount_amount = 100 });
        await db.SaveChangesAsync();

        var controller = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var info = await controller.GetRentalPunchCardInfo(order.id, "session-1");
        var payload = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(info.Result).Value);
        var json = JsonSerializer.SerializeToElement(payload.data);
        Assert.Equal(100, json.GetProperty("skiRentals")[0].GetProperty("rentalDates")[0].GetProperty("amount").GetDouble());
        await controller.UseRentalPunchCard(order.id, new RentController.PunchCardUseRequest { card_id = card.id, punch_count = 1 }, "session-1");

        var after = f.NewContext();
        Assert.Equal(0, (await after.rentalDetail.SingleAsync(d => d.id == detail.id)).valid);
        Assert.Empty(await after.discount.Where(d => d.sub_biz_type == "日租金" && d.sub_biz_id == detail.id && d.valid == 1).ToListAsync());
        Assert.Equal(1, (await after.rentalDetail.SingleAsync(d => d.id == tomorrow.id)).valid);
        Assert.Equal(50, (await after.discount.SingleAsync(d => d.sub_biz_type == "日租金" && d.sub_biz_id == tomorrow.id && d.valid == 1)).amount);
        Assert.Equal(CoachCardUse.StatusReversed, (await after.coachCardUse.SingleAsync(u => u.sub_biz_id == detail.id)).status);
        Assert.Equal(1, (await after.punchCard.SingleAsync(c => c.id == card.id)).punches);
        var updated = await after.rental.Where(r => r.id == rental.id).Include(r => r.details).Include(r => r.discounts).SingleAsync();
        // The punched day contributes zero; tomorrow still contributes its 100 - 50.
        Assert.Equal(50, updated.totalSummary);
    }
}

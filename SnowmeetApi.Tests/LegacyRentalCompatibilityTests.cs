using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class LegacyRentalCompatibilityTests
{
    [Theory]
    [InlineData(false, false, 0)] // old app/order: old flow remains available
    [InlineData(true, false, 1)]  // new app: verification is enforced on the server
    [InlineData(false, true, 1)]  // new-benefit order: omitting the flag cannot bypass verification
    public async Task PunchWriteoffPreservesTheOldClientContract(bool requireVerified, bool newBenefitOrder, int expectedCode)
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1, member_id = CoachFixture.StudentA, wechat_unverified = false };
        db.order.Add(order); await db.SaveChangesAsync();
        if (newBenefitOrder) db.rentalBenefitPreference.Add(new RentalBenefitPreference { order_id = order.id, mode = "auto", staff_id = 9 });
        var rental = new Rental { order_id = order.id, valid = 1, name = "单板" };
        var category = new RentCategory { code = "0201", name = "单板", valid = 1 };
        db.rental.Add(rental); db.rentCategory.Add(category); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = rental.id, category_id = category.id, valid = 1 });
        db.rentalDetail.Add(new RentalDetail { rental_id = rental.id, rental_date = DateTime.Today, charge_type = "租金", amount = 200, valid = 1 });
        var card = new PunchCard { member_id = CoachFixture.StudentA, biz_type = "租赁", card_name = "次卡", total = 2, punches = 0 };
        db.punchCard.Add(card); await db.SaveChangesAsync();
        var ctl = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await ctl.UseRentalPunchCard(order.id, new RentController.PunchCardUseRequest { card_id = card.id, punch_count = 1 },
            "session-1", requireVerified: requireVerified);
        var payload = Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(expectedCode, payload.code);
        Assert.Equal(expectedCode == 0 ? 1 : 0, (await f.NewContext().punchCard.SingleAsync(c => c.id == card.id)).punches);
    }
}

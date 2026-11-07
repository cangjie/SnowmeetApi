using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RentalCompletePickupBillingTests
{
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static async Task<(int rentalId, int firstId, int secondId)> Seed(CoachFixture f, DateTime start)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1, staff_id = 9 };
        db.order.Add(order);
        await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "套餐", valid = 0, category_id = 31, staff_id = 9, start_date = start };
        db.rental.Add(rental);
        await db.SaveChangesAsync();
        var first = new RentItem { rental_id = rental.id, name = "雪鞋", class_name = "雪鞋", noCode = true, atOnce = true, valid = 0 };
        var second = new RentItem { rental_id = rental.id, name = "雪板", class_name = "雪板", noCode = true, atOnce = false, valid = 0 };
        db.rentItem.AddRange(first, second);
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = DateTime.Now.Date, rent_type = "日场", day_type = "平日", scene = "门市", price = 150 });
        await db.SaveChangesAsync();
        return (rental.id, first.id, second.id);
    }

    [Fact]
    public async Task PartiallyIssuedRentalDoesNotStartBillingUntilLastItemIsIssued()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, _, secondId) = await Seed(f, DateTime.Now.Date);
        await Rent(f).EffectRental(rentalId, 9);
        Assert.Empty(await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync());
        Assert.Null((await Rent(f).GetRental(rentalId)).realStartDate);

        await Rent(f).SetRentItemStatus(secondId, "已发放", "session-1");
        var rows = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(DateTime.Now.Date, rows[0].rental_date.Date);
        Assert.Equal(150, rows[0].amount);
        Assert.Equal(DateTime.Now.Date, (await f.Db.rental.AsNoTracking().SingleAsync(r => r.id == rentalId)).start_date!.Value.Date);
    }

    [Fact]
    public async Task NightlyContinuationDoesNotChargePartiallyIssuedRental()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, _, _) = await Seed(f, DateTime.Now.Date);
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).ContinueRentOrder(DateTime.Now.Date.AddDays(1));
        Assert.Empty(await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task ExistingRentDoesNotCauseContinuationBeforeAllItemsAreIssued()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, _, _) = await Seed(f, DateTime.Now.Date);
        f.Db.rentalDetail.Add(new RentalDetail { rental_id = rentalId, charge_type = "租金", rental_date = DateTime.Now.Date, amount = 100, valid = 1 });
        await f.Db.SaveChangesAsync();
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).ContinueRentOrder(DateTime.Now.Date.AddDays(1));
        var rows = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(100, rows[0].amount);
    }

    [Fact]
    public async Task ReturningIssuedItemDoesNotSettleWhileOtherItemsAreUnissued()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, firstId, _) = await Seed(f, DateTime.Now.Date);
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).SetRentItemStatus(firstId, "已归还", "session-1");
        Assert.Equal(0, (await f.Db.rental.AsNoTracking().SingleAsync(r => r.id == rentalId)).settled);
    }

    [Fact]
    public async Task CompletingPickupBeforeScheduledStartKeepsScheduledBillingDate()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        DateTime tomorrow = DateTime.Now.Date.AddDays(1);
        var (rentalId, _, secondId) = await Seed(f, tomorrow);
        f.Db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rentalId, rent_date = tomorrow, rent_type = "日场", day_type = "平日", scene = "门市", price = 180 });
        await f.Db.SaveChangesAsync();
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).SetRentItemStatus(secondId, "已发放", "session-1");
        var row = await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == rentalId && d.valid == 1);
        Assert.Equal(tomorrow, row.rental_date.Date);
        Assert.Equal(180, row.amount);
    }

    [Fact]
    public async Task CompletingPickupOnALaterDayPreservesTheManuallySetRentalPrice()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, _, secondId) = await Seed(f, DateTime.Now.Date.AddDays(-1));
        var db = f.NewContext();
        var original = await db.rentalPricePreset.SingleAsync(p => p.rental_id == rentalId);
        original.rent_date = DateTime.Now.Date.AddDays(-1);
        original.price = 77;
        original.manual = true;
        db.rentalPricePreset.Update(original);
        var day = DateTime.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "平日";
        db.rentPrice.Add(new RentPrice { type = "分类", category_id = 31, shop_id = 1, day_type = day,
            rent_type = "日场", scene = "门市", price = 150, valid = 1 });
        await db.SaveChangesAsync();
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).SetRentItemStatus(secondId, "已发放", "session-1");
        var row = await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == rentalId && d.valid == 1);
        Assert.Equal(77, row.amount);
    }

    [Fact]
    public async Task CompensationOnPickupDayDoesNotSuppressFirstRentCharge()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, firstId, secondId) = await Seed(f, DateTime.Now.Date);
        await Rent(f).EffectRental(rentalId, 9);
        f.Db.rentalDetail.Add(new RentalDetail { rental_id = rentalId, rent_item_id = firstId, charge_type = "赔偿金",
            rental_date = DateTime.Now.Date, amount = 50, valid = 1 });
        await f.Db.SaveChangesAsync();
        await Rent(f).SetRentItemStatus(secondId, "已发放", "session-1");
        var details = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1).ToListAsync();
        Assert.Contains(details, d => d.charge_type == "租金" && d.amount == 150);
        Assert.Contains(details, d => d.charge_type == "赔偿金" && d.amount == 50);
    }

    [Fact]
    public async Task CompensationDoesNotSuppressFollowingDayRent()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, firstId, secondId) = await Seed(f, DateTime.Now.Date);
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).SetRentItemStatus(secondId, "已发放", "session-1");
        var tomorrow = DateTime.Now.Date.AddDays(1);
        f.Db.rentalDetail.Add(new RentalDetail { rental_id = rentalId, rent_item_id = firstId, charge_type = "赔偿金", rental_date = tomorrow, amount = 50, valid = 1 });
        f.Db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rentalId, rent_date = tomorrow, rent_type = "多日", day_type = "平日", scene = "门市", price = 110 });
        await f.Db.SaveChangesAsync();
        await Rent(f).ContinueRentOrder(tomorrow);
        var details = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1 && d.rental_date.Date == tomorrow).ToListAsync();
        Assert.Contains(details, d => d.charge_type == "租金" && d.amount == 110);
        Assert.Contains(details, d => d.charge_type == "赔偿金" && d.amount == 50);
    }

    [Fact]
    public async Task ContinuationRecoversMissingFirstDayAfterPricingBecomesAvailable()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, _, secondId) = await Seed(f, DateTime.Now.Date);
        await f.Db.rentalPricePreset.Where(p => p.rental_id == rentalId).ExecuteDeleteAsync();
        await Rent(f).EffectRental(rentalId, 9);
        await Rent(f).SetRentItemStatus(secondId, "已发放", "session-1");
        Assert.Empty(await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.charge_type == "租金").ToListAsync());
        var today = DateTime.Now.Date;
        var tomorrow = today.AddDays(1);
        f.Db.rentalPricePreset.AddRange(
            new RentalPricePreset { rental_id = rentalId, rent_date = today, rent_type = "日场", day_type = "平日", scene = "门市", price = 150 },
            new RentalPricePreset { rental_id = rentalId, rent_date = tomorrow, rent_type = "多日", day_type = "平日", scene = "门市", price = 100 });
        await f.Db.SaveChangesAsync();
        await Rent(f).ContinueRentOrder(tomorrow);
        var rows = await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId && d.valid == 1 && d.charge_type == "租金")
            .OrderBy(d => d.rental_date).ToListAsync();
        Assert.Equal(new[] { 150d, 100d }, rows.Select(d => d.amount).ToArray());
    }

    [Fact]
    public async Task ContinuationDoesNotRecoverRentForSettledRental()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (rentalId, firstId, secondId) = await Seed(f, DateTime.Now.Date);
        await f.Db.rental.Where(r => r.id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(r => r.valid, 1).SetProperty(r => r.settled, 1));
        await f.Db.rentItem.Where(i => i.rental_id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(i => i.valid, 1));
        f.Db.rentItemLog.AddRange(new RentItemLog { rent_item_id = firstId, status = "已发放", valid = 1 },
            new RentItemLog { rent_item_id = secondId, status = "已发放", valid = 1 });
        await f.Db.SaveChangesAsync();
        var controller = Rent(f);
        await controller.ContinueRental(await controller.GetRental(rentalId), DateTime.Now.Date.AddDays(1));
        Assert.Empty(await f.Db.rentalDetail.AsNoTracking().Where(d => d.rental_id == rentalId).ToListAsync());
    }
}

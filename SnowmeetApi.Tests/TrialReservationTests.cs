using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Rent;

namespace SnowmeetApi.Tests;

public class TrialReservationTests
{
    private static async Task Setup(CoachFixture f)
    {
        f.Config["Coach:SeasonEndDate"] = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");
        f.Db.introducer.Add(new Introducer { id = 5, member_id = CoachFixture.CoachMember,
            coach_id = f.CoachId, name = "武士团", type = "机构", valid = 1 });
        f.Db.rentProduct.AddRange(
            new RentProduct { id = 101, name = "板一", barcode = "108", category_id = 69, valid = 1 },
            new RentProduct { id = 102, name = "板二", barcode = "109", category_id = 69, valid = 1 });
        f.Db.trialBoard.AddRange(new TrialBoard { rent_product_id = 101, introducer_id = 5 },
            new TrialBoard { rent_product_id = 102, introducer_id = 5 });
        f.Db.coachCard.Add(new CoachCard { coach_id = f.CoachId, member_id = CoachFixture.StudentA,
            status = CoachCard.StatusClaimed, token = "trial-test-a", start_date = DateTime.Today.AddDays(-1),
            end_date = DateTime.Today.AddDays(1), season_end_year = DateTime.Today.Year });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task MemberCanCancelAndRebookButNotBookAgainAfterReturn()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var svc = new TrialReservationService(f.NewContext(), f.Config);
        var now = DateTime.Now;
        await Assert.ThrowsAsync<CoachException>(() => svc.ReserveAsync(CoachFixture.StudentB, 101, now, now));
        var first = await svc.ReserveAsync(CoachFixture.StudentA, 101, now, now);
        await Assert.ThrowsAsync<CoachException>(() => svc.ReserveAsync(CoachFixture.StudentA, 102, now, now));
        await svc.CancelAsync(first.id, CoachFixture.StudentA, null, now);
        var second = await svc.ReserveAsync(CoachFixture.StudentA, 102, now, now);
        var qr = await svc.CreateQrAsync(second.id, 9, now);
        await Assert.ThrowsAsync<CoachException>(() => svc.CheckInAsync(qr.qr_token!, CoachFixture.StudentB, now));
        await svc.CheckInAsync(qr.qr_token!, CoachFixture.StudentA, now);
        await svc.ReturnAsync(second.id, 9, now);
        Assert.False(await svc.BoardUnavailableAsync(102, now));
        await Assert.ThrowsAsync<CoachException>(() => svc.ReserveAsync(CoachFixture.StudentA, 101, now, now));
    }

    [Fact]
    public async Task UnreturnedTrialBlocksLaterDatesUntilReturn()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var svc = new TrialReservationService(f.NewContext(), f.Config);
        var now = DateTime.Now;
        var reservation = await svc.ReserveAsync(CoachFixture.StudentA, 101, now, now);
        var qr = await svc.CreateQrAsync(reservation.id, 9, now);
        await svc.CheckInAsync(qr.qr_token!, CoachFixture.StudentA, now);
        Assert.True(await svc.BoardUnavailableAsync(101, now.AddDays(1)));
        Assert.True(await new RentProductAvailability(f.NewContext()).IsBlockedForOpenRentalAsync(101, null, now.AddDays(1), now));
        await svc.ReturnAsync(reservation.id, 9, now.AddDays(1));
        Assert.False(await svc.BoardUnavailableAsync(101, now.AddDays(1)));
    }

    [Fact]
    public async Task ExistingOpenRentalBlocksTrialAndFutureTrialBlocksRental()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var now = DateTime.Now;
        var order = new Order { type = "租赁", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1, start_date = now };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, rent_product_id = 101, code = "108" };
        f.Db.rentItem.Add(item); await f.Db.SaveChangesAsync();
        var svc = new TrialReservationService(f.NewContext(), f.Config);
        await Assert.ThrowsAsync<CoachException>(() => svc.ReserveAsync(CoachFixture.StudentA, 101, now.AddDays(1), now));
        f.Db.rentItemLog.Add(new RentItemLog { rent_item_id = item.id, status = "已归还", valid = 1 });
        await f.Db.SaveChangesAsync();
        await svc.ReserveAsync(CoachFixture.StudentA, 101, now.AddDays(1), now);
        Assert.True(await new RentProductAvailability(f.NewContext()).IsBlockedForOpenRentalAsync(101, null, now, now));
    }

    [Theory]
    [InlineData(null, 69, false, true)]
    [InlineData(null, 70, false, false)]
    [InlineData(null, null, false, false)]
    [InlineData(null, 69, true, false)]
    [InlineData(101, 70, false, true)]
    [InlineData(103, 69, false, false)]
    public async Task RentalConflictUsesProductIdOrCategoryAndBarcode(int? productId, int? categoryId, bool noCode, bool expected)
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var now = DateTime.Now;
        var order = new Order { type = "租赁", valid = 1 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1, start_date = now };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, rent_product_id = productId,
            category_id = categoryId, code = " 108 ", noCode = noCode });
        await f.Db.SaveChangesAsync();
        var svc = new TrialReservationService(f.NewContext(), f.Config);
        Assert.Equal(expected, await svc.BoardUnavailableAsync(101, now));
    }

    [Fact]
    public async Task TrialBlocksOnlyTheMatchingProductOrCategory()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        f.Db.rentProduct.Add(new RentProduct { id = 103, name = "鞋", barcode = "108", category_id = 70, valid = 1 });
        await f.Db.SaveChangesAsync();
        var now = DateTime.Now;
        await new TrialReservationService(f.NewContext(), f.Config).ReserveAsync(CoachFixture.StudentA, 101, now, now);
        var availability = new RentProductAvailability(f.NewContext());
        Assert.True(await availability.IsBlockedForOpenRentalAsync(null, "108", now, now, 69));
        Assert.False(await availability.IsBlockedForOpenRentalAsync(null, "108", now, now, 70));
        Assert.False(await availability.IsBlockedForOpenRentalAsync(null, "108", now, now));
        Assert.False(await availability.IsBlockedForOpenRentalAsync(103, "108", now, now, 69));
        Assert.True(await availability.IsBlockedAsync(null, "108", now, 69));
        Assert.False(await availability.IsBlockedAsync(null, "108", now, 70));
        Assert.False(await availability.IsUnavailableForNewRentalAsync(103, "108", now, 70));
    }

    [Fact]
    public async Task ExpiredOrForeignQrCannotCheckIn()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var svc = new TrialReservationService(f.NewContext(), f.Config);
        var now = DateTime.Now;
        var reservation = await svc.ReserveAsync(CoachFixture.StudentA, 101, now, now);
        var qr = await svc.CreateQrAsync(reservation.id, 9, now);
        var token = qr.qr_token!;
        await Assert.ThrowsAsync<CoachException>(() => svc.CheckInAsync(token, CoachFixture.StudentB, now));
        await Assert.ThrowsAsync<CoachException>(() => svc.CheckInAsync(token, CoachFixture.StudentA, now.AddMinutes(3)));
        Assert.Equal(TrialReservation.Pending, (await f.NewContext().trialReservation.SingleAsync(r => r.id == reservation.id)).status);
        await svc.CheckInAsync(token, CoachFixture.StudentA, now.AddSeconds(1));
        await Assert.ThrowsAsync<CoachException>(() => svc.CheckInAsync(token, CoachFixture.StudentA, now.AddSeconds(2)));
    }

    [Fact]
    public async Task CheckoutRejectsFutureReservationWithoutChangingOrder()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var now = DateTime.Now;
        await new TrialReservationService(f.NewContext(), f.Config).ReserveAsync(CoachFixture.StudentA, 101, now.AddDays(1), now);
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 0 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, start_date = now };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, code = "108", category_id = 69 });
        await f.Db.SaveChangesAsync();
        var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.PlaceRentOrder(order.id, "session-1");
        var response = (ApiResult<Order>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(1, response.code);
        Assert.Equal("该租赁物暂不可用", response.message);
        Assert.Equal(0, (await f.Db.order.AsNoTracking().SingleAsync(o => o.id == order.id)).valid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckoutAllowsAnotherCategoryWithSameBarcode(bool useProductId)
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        f.Db.rentProduct.Add(new RentProduct { id = 103, name = "鞋", barcode = "108", category_id = 70, valid = 1 });
        await f.Db.SaveChangesAsync();
        var now = DateTime.Now;
        await new TrialReservationService(f.NewContext(), f.Config).ReserveAsync(CoachFixture.StudentA, 101, now, now);
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 0 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, start_date = now, noGuaranty = true };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, rent_product_id = useProductId ? 103 : null, code = "108", category_id = 70 });
        await f.Db.SaveChangesAsync();
        var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.PlaceRentOrder(order.id, "session-1");
        Assert.Equal(0, ((ApiResult<Order>)((OkObjectResult)result.Result!).Value!).code);
    }

    [Fact]
    public async Task ConcurrentMembersCannotReserveSameBoard()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        f.Db.coachCard.Add(new CoachCard { coach_id = f.CoachId, member_id = CoachFixture.StudentB,
            status = CoachCard.StatusClaimed, token = "trial-test-b", start_date = DateTime.Today.AddDays(-1),
            end_date = DateTime.Today.AddDays(1), season_end_year = DateTime.Today.Year });
        await f.Db.SaveChangesAsync();
        var now = DateTime.Now;
        async Task<bool> Book(int member)
        {
            try { await new TrialReservationService(f.NewContext(), f.Config).ReserveAsync(member, 101, now, now); return true; }
            catch (CoachException) { return false; }
        }
        var results = await Task.WhenAll(Book(CoachFixture.StudentA), Book(CoachFixture.StudentB));
        Assert.Single(results.Where(x => x));
        Assert.Equal(1, await f.NewContext().trialReservation.CountAsync());
    }

    [Fact]
    public async Task CheckoutAndReservationCannotBothSucceed()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var now = DateTime.Now;
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 0 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, start_date = now, noGuaranty = true };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, rent_product_id = 101, code = "108", category_id = 69 });
        await f.Db.SaveChangesAsync();
        async Task<bool> Book()
        {
            try { await new TrialReservationService(f.NewContext(), f.Config).ReserveAsync(CoachFixture.StudentA, 101, now, now); return true; }
            catch (CoachException) { return false; }
        }
        async Task<bool> Checkout()
        {
            var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var result = await controller.PlaceRentOrder(order.id, "session-1");
            return ((ApiResult<Order>)((OkObjectResult)result.Result!).Value!).code == 0;
        }
        var results = await Task.WhenAll(Book(), Checkout());
        Assert.Single(results.Where(x => x));
    }

    [Fact]
    public async Task ImmediateIssueWithDelayedBillingConflictsBeforeRentalStart()
    {
        await using var f = await CoachFixture.Create(false);
        await Setup(f);
        var now = DateTime.Now;
        await new TrialReservationService(f.NewContext(), f.Config).ReserveAsync(CoachFixture.StudentA, 101, now.AddDays(1), now);
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 0 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, start_date = now.AddDays(2), noGuaranty = true };
        f.Db.rental.Add(rental); await f.Db.SaveChangesAsync();
        f.Db.rentItem.Add(new RentItem { rental_id = rental.id, rent_product_id = 101, code = "108", atOnce = true });
        await f.Db.SaveChangesAsync();
        var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.PlaceRentOrder(order.id, "session-1");
        Assert.Equal(1, ((ApiResult<Order>)((OkObjectResult)result.Result!).Value!).code);
    }
}

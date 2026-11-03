using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 夜间续租任务（Rent/ContinueRentOrder）只给还在进行的租赁订单续租：订单了结关闭（closed = 1）就不再续租。
/// 2026-11-03 用户定：「只要是订单了结关闭，就不要续租了」。以前任务只看租赁有没有归还，
/// 订单关了、租赁没归还的旧单（如关单后装备一直没还的）每晚都会继续加明细行，租金明细越积越大。
/// </summary>
public class ContinueRentOrderClosedTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private const int CategoryId = 31;

    /// <summary>一单一个租赁、一件已发放的装备，没归还；target 当天有价格预设（100），续租会据此记一行。</summary>
    private static async Task<int> SeedRental(CoachFixture f, int closed, DateTime target)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order
        {
            type = "租赁", shop = "万龙服务中心", member_id = CoachFixture.StudentA, valid = 1, closed = closed,
            staff_id = 9, biz_date = target.AddDays(-2),
        };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "雪板套餐", valid = 1, settled = 0, staff_id = 9, category_id = CategoryId, start_date = target.AddDays(-2) };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, name = "雪板", class_name = "双板", noCode = true, valid = 1, category_id = CategoryId };
        db.rentItem.Add(item); await db.SaveChangesAsync();
        db.rentItemLog.Add(new RentItemLog { rent_item_id = item.id, status = "已发放", valid = 1 });
        db.rentalPricePreset.Add(new RentalPricePreset { rental_id = rental.id, rent_date = target, rent_type = "多日", day_type = "平日", scene = "门市", price = 100 });
        await db.SaveChangesAsync();
        return rental.id;
    }

    private static async Task<int> RentRows(CoachFixture f, int rentalId, DateTime target) =>
        await f.Db.rentalDetail.AsNoTracking().CountAsync(d => d.rental_id == rentalId && d.valid == 1 && d.charge_type == "租金" && d.rental_date.Date == target.Date);

    [Fact]
    public async Task ClosedOrder_IsNotContinued_WhileAnOpenOrderInTheSameRunIs()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        DateTime target = DateTime.Now.Date.AddDays(1);
        int openRental = await SeedRental(f, closed: 0, target);
        int closedRental = await SeedRental(f, closed: 1, target);

        await Rent(f).ContinueRentOrder(target);

        Assert.Equal(1, await RentRows(f, openRental, target));      // 进行中的订单照常续租
        Assert.Equal(0, await RentRows(f, closedRental, target));    // 了结关闭的订单不再续租
    }

    [Fact]
    public async Task ReopenedOrder_IsContinuedAgain()
    {
        // 订单重开（closed 改回 0）后又是进行中的订单，续租恢复
        await using var f = await CoachFixture.Create(withPricing: false);
        DateTime target = DateTime.Now.Date.AddDays(1);
        int rentalId = await SeedRental(f, closed: 1, target);
        await Rent(f).ContinueRentOrder(target);
        Assert.Equal(0, await RentRows(f, rentalId, target));

        await f.Db.order.Where(o => o.rentals.Any(r => r.id == rentalId)).ExecuteUpdateAsync(s => s.SetProperty(o => o.closed, 0));
        await Rent(f).ContinueRentOrder(target);
        Assert.Equal(1, await RentRows(f, rentalId, target));
    }
}

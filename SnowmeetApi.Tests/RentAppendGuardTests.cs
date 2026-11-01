using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 主单没付款、没生效时不能确认追加：否则免押追加项会当场「已发放」，订单显示未开始却有已发放的装备，
/// 续租任务还会每晚给它加租金（WT_ZL_261031_00006）。删除追加草稿也不能清掉主单的待付金额。
/// </summary>
public class RentAppendGuardTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static readonly string ClerkSession = "session-" + CoachFixture.Clerk;
    private static RentController Rent(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private static OrderController Orders(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private static async Task<(int orderId, int rentalId, int itemId)> Seed(CoachFixture f, int dealed, bool paid, double? payingAmount)
    {
        var db = f.NewContext();
        db.shop.Add(new Shop { name = "万龙体验中心" });
        var order = new Order { type = "租赁", shop = "万龙体验中心", member_id = CoachFixture.StudentA, valid = 1, staff_id = 10,
            dealed = dealed, paying_amount = payingAmount };
        db.order.Add(order); await db.SaveChangesAsync();
        db.rental.Add(new Rental { order_id = order.id, name = "雪板", valid = 1, staff_id = 10, guaranty = 6000, guaranty_discount = 0 });
        if (paid)
        {
            db.orderPayment.Add(new OrderPayment { order_id = order.id, amount = 6000, pay_method = "微信支付", status = "支付成功", valid = 1 });
        }
        var appending = new Rental { order_id = order.id, name = "护具", valid = 1, staff_id = 10, appending = true, guaranty = 0,
            guaranty_discount = 0, start_date = DateTime.Now, pick_type = "立即租赁" };
        db.rental.Add(appending); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = appending.id, name = "护具", class_name = "护具", noCode = true, atOnce = true, valid = 1, pick_type = "立即租赁" };
        db.rentItem.Add(item); await db.SaveChangesAsync();
        return (order.id, appending.id, item.id);
    }

    private static async Task<dynamic> Commit(CoachFixture f, int orderId, bool commit)
    {
        var appendings = (await Orders(f).GetOrder(orderId)).appendingRentals;
        var res = await Rent(f).SaveAppendings(orderId, appendings, ClerkSession, commit: commit);
        return ((OkObjectResult)res.Result!).Value!;
    }

    [Fact]
    public async Task Confirming_an_append_is_refused_while_the_main_order_is_unpaid()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, rentalId, itemId) = await Seed(f, dealed: 0, paid: false, payingAmount: 6000);

        var result = await Commit(f, orderId, commit: true);

        Assert.Equal(1, (int)result.code);
        Assert.Contains("付款", (string)result.message);
        var db = f.NewContext();
        var rental = await db.rental.AsNoTracking().SingleAsync(r => r.id == rentalId);
        Assert.True(rental.appending);
        Assert.Null(rental.append_commit_time);
        Assert.False(await db.rentItemLog.AnyAsync(l => l.rent_item_id == itemId));
        Assert.Equal(6000, (await db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).paying_amount);
    }

    [Theory]
    [InlineData(1, true)]    // 正常：已付款、已生效
    [InlineData(1, false)]   // 招待等 0 元单：没有付款记录，但已生效
    [InlineData(0, true)]    // 有成功付款、生效标记没置上的历史单，钱已经收了，不拦
    public async Task Confirming_an_append_still_issues_items_on_an_effective_order(int dealed, bool paid)
    {
        await using var f = await CoachFixture.Create();
        var (orderId, rentalId, itemId) = await Seed(f, dealed, paid, payingAmount: null);

        var result = await Commit(f, orderId, commit: true);

        Assert.Equal(0, (int)result.code);
        var db = f.NewContext();
        var rental = await db.rental.AsNoTracking().SingleAsync(r => r.id == rentalId);
        Assert.False(rental.appending);
        Assert.NotNull(rental.append_commit_time);
        var log = await db.rentItemLog.AsNoTracking().SingleAsync(l => l.rent_item_id == itemId);
        Assert.Equal("已发放", log.status);
    }

    [Fact]
    public async Task Saving_a_draft_is_still_allowed_on_an_unpaid_order()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, rentalId, itemId) = await Seed(f, dealed: 0, paid: false, payingAmount: 6000);

        var result = await Commit(f, orderId, commit: false);

        Assert.Equal(0, (int)result.code);
        var db = f.NewContext();
        Assert.True((await db.rental.AsNoTracking().SingleAsync(r => r.id == rentalId)).appending);
        Assert.False(await db.rentItemLog.AnyAsync(l => l.rent_item_id == itemId));
    }

    [Fact]
    public async Task Removing_a_draft_keeps_the_unpaid_main_order_amount()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, rentalId, _) = await Seed(f, dealed: 0, paid: false, payingAmount: 6000);

        await Rent(f).RemoveAppendingRental(rentalId, ClerkSession);

        var db = f.NewContext();
        Assert.Equal(0, (await db.rental.AsNoTracking().SingleAsync(r => r.id == rentalId)).valid);
        Assert.Equal(6000, (await db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).paying_amount);
    }

    [Fact]
    public async Task Removing_the_last_draft_clears_the_amount_on_an_effective_order()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, rentalId, _) = await Seed(f, dealed: 1, paid: true, payingAmount: 300);

        await Rent(f).RemoveAppendingRental(rentalId, ClerkSession);

        var db = f.NewContext();
        Assert.Equal(0, (await db.order.AsNoTracking().SingleAsync(o => o.id == orderId)).paying_amount);
    }
}

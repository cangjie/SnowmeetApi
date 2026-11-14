using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-12 生产实录：租赁开单页「去结算」后，小程序把本地订单脱钩成 id=0 的新单，却保留了每个租赁里刚生成的
// guaranties（PlaceRentOrder 返回时带着）。再保存时 SaveRentRecept 把它们当新实体一起插入：
// 带着已有 id 插入 → SQL 544「Cannot insert explicit value for identity column in table 'guaranty'」，
// 同一条保证金出现在两处 → InvalidOperationException「Guaranty cannot be tracked」，页面只看到 500。
// 保证金记录只由 PlaceRentOrder 生成，草稿保存不该接收、更不该改写它。
public class SaveRentReceptGuarantyTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    private static Guaranty Stale(int id = 16118, double amount = 3000) => new()
    {
        id = id, order_id = 72702, guaranty_type = "在线支付", biz_type = "租赁", biz_id = 54901,
        amount = amount, valid = 1, staff_id = 60
    };

    private static Order ReceptOrder(params Rental[] rentals) => new()
    {
        shop = "万龙服务中心", type = "租赁", rentals = rentals.ToList()
    };

    private static Order? Data(ActionResult<ApiResult<Order?>> result, int expectedCode = 0)
    {
        var api = (ApiResult<Order?>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(expectedCode, api.code);
        return api.data;
    }

    [Fact]
    public async Task NewOrderSavedAfterCheckoutIgnoresTheStaleGuarantyCarriedOver()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = ReceptOrder(new Rental { name = "中级双板套餐", guaranty = 3000, guaranties = new List<Guaranty> { Stale() } });

        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(order, "session-1"));

        Assert.NotNull(saved);
        Assert.True(saved!.id > 0);
        Assert.Single(await f.Db.rental.AsNoTracking().Where(r => r.order_id == saved.id).ToListAsync());
        Assert.Empty(await f.Db.guaranty.AsNoTracking().ToListAsync());
        Assert.All(saved.rentals, r => Assert.Empty(r.guaranties));
    }

    [Fact]
    public async Task SameStaleGuarantyOnTwoRentalsNoLongerBreaksTheSave()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = ReceptOrder(
            new Rental { name = "中级双板套餐", guaranty = 3000, guaranties = new List<Guaranty> { Stale() } },
            new Rental { name = "儿童雪服套装", guaranty = 2000, guaranties = new List<Guaranty> { Stale() } });

        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(order, "session-1"));

        Assert.Equal(2, (await f.Db.rental.AsNoTracking().Where(r => r.order_id == saved!.id).ToListAsync()).Count);
        Assert.Empty(await f.Db.guaranty.AsNoTracking().ToListAsync());
    }

    // 2026-11-14 生产实录（e0f9804a 部署后仍 500）：PlaceRentOrder 新增的保证金记录同时挂到订单级 order.guarantys
    // （Guaranty.order_id）和租赁级 rental.guaranties（biz_id），小程序脱钩时两处都带着，只清租赁级还不够。
    [Fact]
    public async Task OrderLevelGuarantyCarriedOverAfterCheckoutIsIgnoredToo()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = ReceptOrder(new Rental { name = "中级双板套餐", guaranty = 3000 });
        order.guarantys = new List<Guaranty> { Stale() };

        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(order, "session-1"));

        Assert.True(saved!.id > 0);
        Assert.Empty(await f.Db.guaranty.AsNoTracking().ToListAsync());
        Assert.Empty(saved.guarantys);
    }

    [Fact]
    public async Task SameGuarantyInBothTheOrderAndTheRentalNoLongerBreaksTheSave()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = ReceptOrder(new Rental { name = "中级双板套餐", guaranty = 3000, guaranties = new List<Guaranty> { Stale() } });
        order.guarantys = new List<Guaranty> { Stale() };

        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(order, "session-1"));

        Assert.True(saved!.id > 0);
        Assert.Empty(await f.Db.guaranty.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SavingAnExistingOrderNeverRewritesGuarantyRows()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var seed = f.NewContext();
        var order = new Order { shop = "万龙服务中心", type = "租赁", valid = 0, recepting = 1, create_date = DateTime.Now };
        seed.order.Add(order); await seed.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "中级双板套餐", guaranty = 3000, create_date = DateTime.Now };
        seed.rental.Add(rental); await seed.SaveChangesAsync();
        var real = new Guaranty { order_id = order.id, biz_type = "租赁", biz_id = rental.id, amount = 3000, valid = 1 };
        seed.guaranty.Add(real); await seed.SaveChangesAsync();

        var posted = new Order { id = order.id, shop = "万龙服务中心", type = "租赁", valid = 0, recepting = 1, create_date = order.create_date,
            rentals = new List<Rental> { new Rental { id = rental.id, order_id = order.id, name = "中级双板套餐", guaranty = 3000,
                create_date = rental.create_date, guaranties = new List<Guaranty> { Stale(real.id, amount: 1) } } } };
        Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(posted, "session-1"));

        var rows = await f.Db.guaranty.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal(3000, rows[0].amount);
        Assert.Equal(rental.id, rows[0].biz_id);
    }
}

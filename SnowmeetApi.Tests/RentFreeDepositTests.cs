using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-13 租赁开单页增加「免押金」勾选（以及勾选招待时询问是否免押金）。小程序只负责把 rental.noGuaranty 随
// SaveRentRecept 存下来、把押金归零；后端这一半早就按 noGuaranty 处理，这里把它钉住，免得以后改动把免押金悄悄改回收押金：
//   1) SaveRentRecept 会保存 noGuaranty，再保存成 false 能改回来（取消勾选免押金）；
//   2) PlaceRentOrder 对 noGuaranty 的租赁不生成押金记录、不计入应付，即使同时带着押金金额（标志位优先于金额）。
public class RentFreeDepositTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    private static Order Data(ActionResult<ApiResult<Order?>> result)
    {
        var api = (ApiResult<Order?>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!;
    }

    [Fact]
    public async Task SaveRentReceptPersistsNoGuarantyAndTheFlagCanBeClearedAgain()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(
            new Order { shop = "万龙服务中心", type = "租赁", rentals = new List<Rental> {
                new Rental { name = "中级双板套餐", guaranty = 0, noGuaranty = true } } }, "session-1"));

        var rental = await f.Db.rental.AsNoTracking().SingleAsync(r => r.order_id == saved.id);
        Assert.True(rental.noGuaranty);
        Assert.Equal(0, rental.guaranty);

        // 取消勾选免押金：小程序把标志清掉、押金恢复成标准押金，再保存一次
        Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(
            new Order { id = saved.id, shop = "万龙服务中心", type = "租赁", valid = 0, recepting = 1, create_date = saved.create_date,
                rentals = new List<Rental> { new Rental { id = rental.id, order_id = saved.id, name = "中级双板套餐",
                    guaranty = 3000, noGuaranty = false, create_date = rental.create_date } } }, "session-1"));

        var again = await f.Db.rental.AsNoTracking().SingleAsync(r => r.id == rental.id);
        Assert.False(again.noGuaranty);
        Assert.Equal(3000, again.guaranty);
    }

    [Fact]
    public async Task PlaceRentOrderSkipsDepositForNoGuarantyRentalsEvenIfTheyCarryAnAmount()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(
            new Order { shop = "万龙服务中心", type = "租赁", rentals = new List<Rental> {
                new Rental { name = "免押金套餐", guaranty = 0, noGuaranty = true },
                new Rental { name = "免押金但带着金额", guaranty = 2000, noGuaranty = true },
                new Rental { name = "正常收押金", guaranty = 3000 } } }, "session-1"));
        int normalId = (await f.Db.rental.AsNoTracking().SingleAsync(r => r.order_id == saved.id && r.name == "正常收押金")).id;

        var placeResult = await new OrderController(f.NewContext(), f.Config, Http()).PlaceRentOrder(saved.id, "session-1");
        var placed = (ApiResult<Order>)((OkObjectResult)placeResult.Result!).Value!;

        Assert.Equal(0, placed.code);
        var rows = await f.Db.guaranty.AsNoTracking().ToListAsync();
        Assert.Single(rows);
        Assert.Equal(normalId, rows[0].biz_id);
        Assert.Equal(3000, rows[0].amount);
        Assert.Equal(3000, placed.data!.paying_amount);
    }

    [Fact]
    public async Task PlaceRentOrderWithOnlyNoGuarantyRentalsOwesNothing()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var saved = Data(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(
            new Order { shop = "万龙服务中心", type = "租赁", rentals = new List<Rental> {
                new Rental { name = "免押金套餐", guaranty = 0, noGuaranty = true } } }, "session-1"));

        var placeResult = await new OrderController(f.NewContext(), f.Config, Http()).PlaceRentOrder(saved.id, "session-1");
        var placed = (ApiResult<Order>)((OkObjectResult)placeResult.Result!).Value!;

        Assert.Equal(0, placed.code);
        Assert.Empty(await f.Db.guaranty.AsNoTracking().ToListAsync());
        Assert.Equal(0, placed.data!.paying_amount);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-09 租赁订单详情页改「商品备注」一直提示「保存失败」：页面先用 Rent/GetRentalByStaff 取回租赁，改了备注再原样 POST 回 UpdateRentalByStaff。
// GetRental 带出了 rental.order，EF 把 order.rentals 补成 [这个租赁自己]，输出 JSON 时循环引用被写成 null（Startup 里的 IgnoreCycles），
// 所以取回来的 rental.order.rentals 是 [null]；再 POST 回去时模型验证会读 Order 的每个公开属性，hasCrossShopRent（11-06 跨店租赁加的）
// 对 null 元素抛 NullReferenceException → 500。
public class RentalPostBackValidationTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    // 和 Startup 一致：AddControllers().AddJsonOptions(IgnoreCycles)
    private static readonly JsonSerializerOptions MvcJson = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    private static async Task<Rental> LoadAndRoundTripAsync(CoachFixture f, bool crossShop) => (await LoadJsonAndRoundTripAsync(f, crossShop)).posted;

    private static async Task<(Rental posted, string json)> LoadJsonAndRoundTripAsync(CoachFixture f, bool crossShop)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "崇礼旗舰店", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1 };
        db.rental.Add(rental); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = rental.id, valid = 1, class_name = "雪鞋", pickup_shop = crossShop ? "万龙体验中心" : null });
        await db.SaveChangesAsync();
        Rental loaded = await new RentController(f.NewContext(), f.Config, Http()).GetRental(rental.id);
        string json = JsonSerializer.Serialize(loaded, MvcJson);
        return (JsonSerializer.Deserialize<Rental>(json, MvcJson)!, json);
    }

    // 和 MVC 绑定 [FromBody] 之后做的验证一样：逐个读公开属性（ValidationVisitor）
    private static ModelStateDictionary Validate(object model)
    {
        var provider = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var context = new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        provider.GetRequiredService<IObjectModelValidator>().Validate(context, null, "", model);
        return context.ModelState;
    }

    [Fact]
    public async Task 页面取回的租赁原样POST回去_模型验证不能因为order里的rentals是null而抛异常()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        Rental posted = await LoadAndRoundTripAsync(f, crossShop: false);
        Assert.NotNull(posted.order);
        var modelState = Validate(posted);
        Assert.True(modelState.IsValid, string.Join("; ", modelState.Select(e => e.Key + ":" + string.Join(",", e.Value!.Errors.Select(x => x.ErrorMessage)))));
    }

    [Fact]
    public async Task 页面取回的租赁改了备注原样POST_UpdateRental能保存且不被order图绊倒()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        Rental posted = await LoadAndRoundTripAsync(f, crossShop: true);
        int rentalId = posted.id;
        posted.memo = "鞋带需要更换";

        await new RentController(f.NewContext(), f.Config, Http()).UpdateRental(posted, "租赁订单详细页修改备注", null, null);

        var saved = await f.NewContext().rental.AsNoTracking().FirstAsync(r => r.id == rentalId);
        Assert.Equal("鞋带需要更换", saved.memo);
    }

    [Fact]
    public async Task 接口输出里order的rentals确实带null_反序列化后Order不保留null元素()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (posted, json) = await LoadJsonAndRoundTripAsync(f, crossShop: true);
        // 线上现象：order.rentals 里指回自己的那条租赁被 IgnoreCycles 写成 null
        Assert.Contains("\"rentals\":[null]", json);
        // 不变量：Order.rentals 里没有 null
        Assert.DoesNotContain(posted.order!.rentals, r => r == null);
        Assert.False(posted.order.hasCrossShopRent);
    }

    [Fact]
    public void Order的跨店标记对rentals和rentItems里的null元素也不抛异常()
    {
        // 直接往列表里 Add(null) 绕过 setter 的情况，计算属性本身也要扛得住
        var order = new Order();
        order.rentals.Add(null!);
        order.rentals.Add(new Rental { valid = 1, rentItems = new List<RentItem> { null!, new RentItem { valid = 1, pickup_shop = "万龙体验中心" } } });
        Assert.True(order.hasCrossShopRent);
    }

    [Fact]
    public void 给rentals赋值时丢弃null元素_赋null仍保持null()
    {
        var order = new Order { rentals = new List<Rental> { null!, new Rental { id = 7 }, null! } };
        Assert.Single(order.rentals);
        Assert.Equal(7, order.rentals[0].id);
        order.rentals = null!;
        Assert.Null(order.rentals);
    }
}

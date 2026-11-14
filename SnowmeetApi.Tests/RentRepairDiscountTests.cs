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
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-14 租赁订单详细页：赔偿除了设金额，还要能设「赔偿减免」，页面显示 赔偿金额 / 赔偿减免 / 实际赔偿。
// 赔偿减免 = 一条挂在「赔偿金」明细上的 discount（biz_id=rental.id、sub_biz_id=赔偿明细 id）。它已含在
// Rental.totalDiscountAmount 里，所以应退押金等金额自动扣减；挂在赔偿明细上，万龙租赁分账不会把它当租金减免。
public class RentRepairDiscountTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    private sealed record Seeded(int OrderId, int RentalId, int ItemId);

    // 一单一个租赁、一件租赁物，有一天 ¥200 的有效租金
    private static async Task<Seeded> Seed(CoachFixture f)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, valid = 1, name = "中级双板套餐" };
        db.rental.Add(rental); await db.SaveChangesAsync();
        var item = new RentItem { rental_id = rental.id, valid = 1, class_name = "双板" };
        db.rentItem.Add(item);
        db.rentalDetail.Add(new RentalDetail { rental_id = rental.id, charge_type = "租金", rental_date = DateTime.Today, amount = 200, valid = 1 });
        await db.SaveChangesAsync();
        return new Seeded(order.id, rental.id, item.id);
    }

    private static async Task<ApiResult<Rental?>> Set(CoachFixture f, Seeded s, double amount, double? discount = null)
    {
        var result = await new RentController(f.NewContext(), f.Config, Http())
            .SetRentItemRepairAmount(s.ItemId, amount, "session-1", discount: discount);
        return (ApiResult<Rental?>)((OkObjectResult)result.Result!).Value!;
    }

    private static Task<List<Discount>> ValidDiscounts(CoachFixture f, Seeded s) =>
        f.Db.discount.AsNoTracking().Where(d => d.valid == 1 && d.biz_id == s.RentalId).ToListAsync();

    [Fact]
    public async Task SettingAmountAndDiscountStoresBothAndSplitsThemOnTheRentalAndItem()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);

        var api = await Set(f, s, 100, discount: 30);

        Assert.Equal(0, api.code);
        var rental = api.data!;
        Assert.Equal(100, rental.totalRepairationAmount);
        Assert.Equal(30, rental.totalRepairationDiscountAmount);
        Assert.Equal(70, rental.actualRepairationAmount);
        Assert.Equal(30, rental.totalDiscountAmount);
        Assert.Equal(200 + 100 - 30, rental.totalSummary);   // 减免只扣一次：租金 + 赔偿 - 赔偿减免
        var item = rental.rentItems.Single();
        Assert.Equal(100, item.totalRepairationAmount);
        Assert.Equal(30, item.totalRepairationDiscountAmount);
        Assert.Equal(70, item.actualRepairationAmount);

        var detail = await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == s.RentalId && d.charge_type == "赔偿金" && d.valid == 1);
        Assert.Equal(100, detail.amount);
        Assert.Equal(s.ItemId, detail.rent_item_id);
        var discount = Assert.Single(await ValidDiscounts(f, s));
        Assert.Equal(30, discount.amount);
        Assert.Equal(detail.id, discount.sub_biz_id);
        Assert.Equal("租赁", discount.biz_type);
        Assert.Equal("赔偿金", discount.sub_biz_type);
        Assert.Equal(s.OrderId, discount.order_id);

        var logs = await f.Db.coreDataModLog.AsNoTracking().Where(l => l.table_name == "rental" && l.key_value == s.RentalId).ToListAsync();
        Assert.Contains(logs, l => l.field_name == "赔偿金额" && l.prev_value == "0" && l.current_value == "100");
        Assert.Contains(logs, l => l.field_name == "赔偿减免" && l.prev_value == "0" && l.current_value == "30");
    }

    [Fact]
    public async Task ChangingTheAmountWithoutADiscountKeepsTheDiscountCappedAtTheNewAmount()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        await Set(f, s, 100, discount: 30);

        var again = (await Set(f, s, 50)).data!;           // 旧版小程序只传金额
        Assert.Equal(50, again.totalRepairationAmount);
        Assert.Equal(30, again.totalRepairationDiscountAmount);
        Assert.Equal(20, again.actualRepairationAmount);
        Assert.Single(await ValidDiscounts(f, s));          // 旧的作废了，不累加

        var capped = (await Set(f, s, 20)).data!;
        Assert.Equal(20, capped.totalRepairationDiscountAmount);
        Assert.Equal(0, capped.actualRepairationAmount);
        Assert.Equal(20, Assert.Single(await ValidDiscounts(f, s)).amount);
    }

    [Fact]
    public async Task ZeroDiscountClearsItAndZeroAmountCancelsTheCompensationAndItsDiscount()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        await Set(f, s, 100, discount: 30);

        var cleared = (await Set(f, s, 100, discount: 0)).data!;
        Assert.Equal(100, cleared.totalRepairationAmount);
        Assert.Equal(0, cleared.totalRepairationDiscountAmount);
        Assert.Empty(await ValidDiscounts(f, s));

        await Set(f, s, 100, discount: 40);
        var cancelled = (await Set(f, s, 0)).data!;
        Assert.Equal(0, cancelled.totalRepairationAmount);
        Assert.Equal(0, cancelled.totalRepairationDiscountAmount);
        Assert.Equal(200, cancelled.totalSummary);
        Assert.Empty(await ValidDiscounts(f, s));
    }

    [Fact]
    public async Task ADiscountLargerThanTheAmountOrBelowZeroIsRejectedAndNothingChanges()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        await Set(f, s, 100, discount: 30);

        var tooBig = await Set(f, s, 100, discount: 100.5);
        Assert.Equal(1, tooBig.code);
        Assert.Contains("不能大于", tooBig.message);
        var negative = await Set(f, s, 100, discount: -1);
        Assert.Equal(1, negative.code);
        var onZeroAmount = await Set(f, s, 0, discount: 10);
        Assert.Equal(1, onZeroAmount.code);

        // 原来的赔偿和减免原样还在
        var detail = await f.Db.rentalDetail.AsNoTracking().SingleAsync(d => d.rental_id == s.RentalId && d.charge_type == "赔偿金" && d.valid == 1);
        Assert.Equal(100, detail.amount);
        Assert.Equal(30, Assert.Single(await ValidDiscounts(f, s)).amount);
        // 等于赔偿金额（全免）是允许的
        var full = (await Set(f, s, 100, discount: 100)).data!;
        Assert.Equal(0, full.actualRepairationAmount);
    }

    // 和 Startup 一致：AddControllers().AddJsonOptions(IgnoreCycles)；MVC 绑定 [FromBody] 后会逐个读公开属性做验证
    // （11-09、11-12 两次「保存失败 / 500」都是计算属性的 getter 在回传的对象图上抛异常），新加的计算属性不能重蹈覆辙。
    private static readonly JsonSerializerOptions MvcJson = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    private static ModelStateDictionary Validate(object model)
    {
        var provider = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var context = new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        provider.GetRequiredService<IObjectModelValidator>().Validate(context, null, "", model);
        return context.ModelState;
    }

    [Fact]
    public async Task ARentalWithCompensationAndDiscountPostedBackByThePageStillValidatesAndKeepsTheOldFields()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);
        var loaded = (await Set(f, s, 100, discount: 30)).data!;     // 页面取回的租赁（GetRental 的结果）

        string json = JsonSerializer.Serialize(loaded, MvcJson);
        Rental posted = JsonSerializer.Deserialize<Rental>(json, MvcJson)!;       // 页面原样 POST 回来

        var modelState = Validate(posted);                                         // 不能抛异常
        Assert.True(modelState.IsValid, string.Join("; ", modelState.Select(e => e.Key + ":" + string.Join(",", e.Value!.Errors.Select(x => x.ErrorMessage)))));

        // 老版小程序读的旧字段口径不变；新增的两个字段只是多出来的 JSON 属性
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(100, root.GetProperty("totalRepairationAmount").GetDouble());
        Assert.Equal(30, root.GetProperty("totalDiscountAmount").GetDouble());
        Assert.Equal(270, root.GetProperty("totalSummary").GetDouble());
        Assert.Equal(30, root.GetProperty("totalRepairationDiscountAmount").GetDouble());
        var item = root.GetProperty("rentItems").EnumerateArray().Single();
        Assert.Equal(100, item.GetProperty("totalRepairationAmount").GetDouble());
        Assert.Equal(30, item.GetProperty("totalRepairationDiscountAmount").GetDouble());
    }

    [Fact]
    public void TheNewComputedPropertiesDoNotThrowOnEmptyOrNullishGraphs()
    {
        var bare = new Rental();                                    // details / discounts 都是空
        Assert.Equal(0, bare.totalRepairationDiscountAmount);
        Assert.Equal(0, bare.actualRepairationAmount);
        var noDetails = new Rental { details = null!, discounts = null! };
        Assert.Equal(0, noDetails.totalRepairationDiscountAmount);
        var item = new RentItem { repairationCharges = null! };
        Assert.Equal(0, item.totalRepairationDiscountAmount);
        Assert.Equal(0, item.actualRepairationAmount);
        Assert.True(Validate(new Rental { rentItems = new List<RentItem> { new RentItem() } }).IsValid);
    }

    [Fact]
    public async Task CompensationDiscountIsNotTreatedAsARentDiscountByTheWanlongShare()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var s = await Seed(f);

        var rental = (await Set(f, s, 100, discount: 30)).data!;

        // 分账基数只看租金：¥200 租金，赔偿减免 ¥30 挂在赔偿明细上，不能扣进去
        Assert.Equal(200, WanlongShareRules.RentAfterDiscounts(rental));
    }
}

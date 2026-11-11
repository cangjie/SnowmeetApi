using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 万龙体验中心也能开养护单（2026-11-09 起；生产库里体验中心 shop_list.care 置 1 就是这道开关）。
/// 规则（用户 2026-11-09 定）：
///   · 订单店名就是开单的门店——体验中心开的养护单记「万龙体验中心」，单号 WT_YH_…，不再改记成服务中心；
///   · 钱收进「万龙服务中心」的微信支付账户：万龙的养护一律走 3 号商户（TenpayController.GetMchId），
///     不是体验中心自己的租赁(5) / 零售(12)商户；
///   · 价目 / 单刮蜡一口价 / 开板刮蜡券 / 教练卡仍按服务中心算（ResolveCareShop 在各规则入口自己归并），所以价格不变。
/// 这里锁的是后端这一半：落库的店名、单号前缀、收款商户、金额。
/// </summary>
public class CareExperienceCenterOrderTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private const string Service = "万龙服务中心";
    private const string Experience = "万龙体验中心";
    private const int ServiceCareMerchant = 3;

    private static async Task<CoachFixture> Create()
    {
        var f = await CoachFixture.Create();
        // 照生产的目标状态：体验中心 care=1（店码 wt），服务中心店码 wl（夹具里已有）
        f.Db.shop.Add(new Shop { id = 10, name = Experience, code = "wt", care = 1, rent = 1, sale = 1 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static Care C(int edge = 1, int wax = 1, int unwax = 1) =>
        new() { equipment = "双板", need_edge = edge, need_wax = wax, need_unwax = unwax, biz_type = "雪季养护", brand = "品牌", scale = "170" };

    private static async Task<(int orderId, int careId)> Draft(CoachFixture f, string shop, Care? care = null)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = shop, member_id = CoachFixture.StudentA, valid = 0, recepting = 1, staff_id = 9,
            code = "YH_20261109_" + Guid.NewGuid().ToString("N")[..6] };
        db.order.Add(order); await db.SaveChangesAsync();
        care ??= C();
        care.order_id = order.id; care.valid = 0;
        db.care.Add(care); await db.SaveChangesAsync();
        return (order.id, care.id);
    }

    private static async Task<ApiResult<SnowmeetApi.Models.Order>> Place(CoachFixture f, int orderId)
    {
        var res = await new OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(orderId, "session-1");
        return Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(res.Result).Value);
    }

    private static TenpayController Tenpay(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    [Fact]
    public async Task SaveDraft_StartedAtTheExperienceCenter_KeepsTheExperienceCenterAsTheOrderShop()
    {
        await using var f = await Create();
        var order = new SnowmeetApi.Models.Order { shop = Experience, member_id = CoachFixture.StudentA, cares = new List<Care> { C() } };
        var res = await new CareController(f.NewContext(), f.Config, new Http()).SaveCareRecept(order, "session-1");
        var env = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal(0, env.code);
        var saved = await f.NewContext().order.AsNoTracking().SingleAsync(o => o.type == "养护");
        Assert.Equal(Experience, saved.shop);
    }

    [Fact]
    public async Task PlaceOrder_AtTheExperienceCenter_KeepsTheShopAndUsesTheExperienceCenterOrderCode()
    {
        await using var f = await Create();
        var (orderId, _) = await Draft(f, Experience);
        var r = await Place(f, orderId);
        Assert.Equal(0, r.code);
        var order = await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(Experience, order.shop);
        Assert.StartsWith("wt_YH_", order.code);          // 体验中心自己的店码，不是服务中心的 wl
        Assert.True(order.total_amount > 0);
    }

    [Fact]
    public async Task PlaceOrder_AtTheServiceCenter_IsUnchanged()
    {
        await using var f = await Create();
        var (orderId, _) = await Draft(f, Service);
        Assert.Equal(0, (await Place(f, orderId)).code);
        var order = await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(Service, order.shop);
        Assert.StartsWith("wl_YH_", order.code);
    }

    /// <summary>价格不能因为店名保留体验中心而变：双项 168、单项 120（夹具里服务中心的价目）、单刮蜡一口价 100（非老会员）。</summary>
    [Theory]
    [InlineData(Experience, 1, 1, 1, 168)]
    [InlineData(Service, 1, 1, 1, 168)]
    [InlineData(Experience, 1, 0, 0, 120)]
    [InlineData(Service, 1, 0, 0, 120)]
    [InlineData(Experience, 0, 0, 1, 100)]
    [InlineData(Service, 0, 0, 1, 100)]
    public async Task PlaceOrder_ChargesTheServiceCenterPriceListAtBothShops(string shop, int edge, int wax, int unwax, double expected)
    {
        await using var f = await Create();
        var (orderId, careId) = await Draft(f, shop, C(edge, wax, unwax));
        Assert.Equal(0, (await Place(f, orderId)).code);
        var saved = await f.NewContext().care.AsNoTracking().SingleAsync(c => c.id == careId);
        Assert.Equal(expected, saved.common_charge);
        var order = await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(expected, order.total_amount);
        Assert.Equal(shop, order.shop);                    // 店名原样保留
    }

    [Fact]
    public async Task PlacedExperienceCenterCareOrder_PaysIntoTheServiceCenterCareMerchant()
    {
        await using var f = await Create();
        var (orderId, _) = await Draft(f, Experience);
        Assert.Equal(0, (await Place(f, orderId)).code);
        var order = await f.NewContext().order.AsNoTracking().SingleAsync(o => o.id == orderId);
        Assert.Equal(Experience, order.shop);              // 订单是体验中心的……
        // ……付款却用服务中心的养护商户：付款走的就是这个落库订单
        Assert.Equal(ServiceCareMerchant, Tenpay(f).GetMchId(order));
        Assert.Equal(Tenpay(f).GetMchId(new SnowmeetApi.Models.Order { shop = Service, type = "养护" }), Tenpay(f).GetMchId(order));
    }

    [Theory]
    [InlineData(Experience, "养护", ServiceCareMerchant)]   // 体验中心的养护 → 服务中心养护商户
    [InlineData(Service, "养护", ServiceCareMerchant)]
    [InlineData(Experience, "租赁", 5)]                     // 体验中心自己的其它业务不受影响
    [InlineData(Experience, "零售", 12)]
    public void WanlongMerchants_ByShopAndBusiness(string shop, string type, int expected)
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().Options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:AppId"] = "local-test" }).Build();
        var tenpay = new TenpayController(db, config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        Assert.Equal(expected, tenpay.GetMchId(new SnowmeetApi.Models.Order { shop = shop, type = type }));
    }

    [Theory]
    [InlineData(Experience, Service)]
    [InlineData(Service, Service)]
    [InlineData(" 万龙体验中心 ", Service)]
    [InlineData("崇礼旗舰店", "崇礼旗舰店")]
    public void ResolveCareShop_TreatsTheExperienceCenterAsTheServiceCenterForPricingRules(string input, string expected) =>
        Assert.Equal(expected, CarePricingRules.ResolveCareShop(input));
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护订单列表里每件装备显示「金额」和「减免」（Care.displayCharge / displayDiscount，只用于展示，不改库）：
///   · 普通装备：金额 = 项目收费 + 附加费，减免 = 手动减免 + 券减免，都是下单时落库的值；
///   · 招待 / 质保装备：库里项目收费、减免都是 0（下单时不计价），展示时按这件装备所做的项目算标准价，
///     「0 元订单无非是按所做项目计算金额，再减免到 0 元」——金额 = 标准价 + 附加费，减免 = 金额，净额 0；
///   · 标准价和开单时的服务费走同一套定价（CareController.CalcCharge），不另写一份价格表。
/// 万龙服务中心的单项价在夹具里是 120、双项 168，只选刮蜡一口价 80 / 100，一眼能看出走的是哪条规则。
/// </summary>
public class CareDisplayAmountTests
{
    private const string Wanlong = "万龙服务中心";
    private static readonly DateTime Now = DateTime.Now;

    private static async Task<int> AddOrder(CoachFixture f, int? member, params Care[] cares)
    {
        var o = new Order { type = "养护", shop = Wanlong, valid = 1, is_test = 0, member_id = member, biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
        foreach (var c in cares) { c.valid = 1; c.create_date = Now.AddHours(-1); if (string.IsNullOrEmpty(c.equipment)) c.equipment = "双板"; o.cares.Add(c); }
        f.Db.order.Add(o);
        await f.Db.SaveChangesAsync();
        return o.id;
    }

    private static Care Double(Action<Care>? tweak = null)
    {
        var c = new Care { equipment = "双板", need_edge = 1, need_wax = 1, biz_type = "雪季养护" };   // 双项
        tweak?.Invoke(c); return c;
    }

    private static Care Single(Action<Care>? tweak = null)
    {
        var c = new Care { equipment = "双板", need_edge = 1, biz_type = "雪季养护" };               // 单项
        tweak?.Invoke(c); return c;
    }

    private static async Task<List<Order>> List(CoachFixture f)
    {
        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, Now.AddDays(-30).Date, Now.Date, "session-7", null, pageIndex: 1, pageSize: 50);
        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items;
    }

    private static async Task<Care> OnlyCare(CoachFixture f, int orderId) =>
        Assert.Single((await List(f)).Single(o => o.id == orderId).cares);

    // ───────────── 纯规则 ─────────────

    [Fact]
    public void FromStored_IsChargePlusRepair_AndDiscountPlusTicketDiscount_RoundedToCents()
    {
        var c = new Care { common_charge = 300.1, repair_charge = 20.2, discount = 30.05, ticket_discount = 20.1 };
        Assert.Equal((320.3, 50.15), CareAmountRules.FromStored(c));
        Assert.Equal((0.0, 0.0), CareAmountRules.FromStored(new Care()));
    }

    [Fact]
    public void ForFreeCare_ChargeIsStandardPricePlusRepair_DiscountEqualsCharge()
    {
        var c = new Care { repair_charge = 50, discount = 10, ticket_discount = 5, warranty = true };   // 库里的减免不参与：净额恒为 0
        Assert.Equal((170.0, 170.0), CareAmountRules.ForFreeCare(c, 120));
        Assert.Equal((0.0, 0.0), CareAmountRules.ForFreeCare(new Care { entertain = true }, 0));
    }

    [Fact]
    public void IsFreeCare_IsWarrantyOrEntertain()
    {
        Assert.True(CareAmountRules.IsFreeCare(new Care { warranty = true }));
        Assert.True(CareAmountRules.IsFreeCare(new Care { entertain = true }));
        Assert.False(CareAmountRules.IsFreeCare(new Care { use_card = true }));
        Assert.False(CareAmountRules.IsFreeCare(new Care()));
    }

    // ───────────── 列表接口 ─────────────

    [Fact]
    public async Task NormalCare_ShowsTheStoredChargeAndDiscounts()
    {
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA, Double(c => { c.common_charge = 168; c.repair_charge = 20; c.discount = 30; c.ticket_discount = 20; }));
        var care = await OnlyCare(f, id);
        Assert.Equal(188.0, care.displayCharge);
        Assert.Equal(50.0, care.displayDiscount);
    }

    [Fact]
    public async Task EntertainCare_ShowsTheStandardPriceOfItsItems_DiscountedToZero()
    {
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA, Double(c => c.entertain = true));   // 库里 0 / 0
        var care = await OnlyCare(f, id);
        Assert.Equal(168.0, care.displayCharge);
        Assert.Equal(168.0, care.displayDiscount);
    }

    [Fact]
    public async Task WarrantyCare_AddsTheRepairFee_AndIgnoresStoredDiscounts()
    {
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA, Single(c => { c.warranty = true; c.repair_charge = 50; c.discount = 10; }));
        var care = await OnlyCare(f, id);
        Assert.Equal(170.0, care.displayCharge);      // 单项 120 + 附加费 50
        Assert.Equal(170.0, care.displayDiscount);    // 减免到 0 元
    }

    [Fact]
    public async Task FreeCare_UsesTheSameSpecialPricingAsBilling_ScrapeOnlyIsTheFlatPrice()
    {
        await using var f = await CoachFixture.Create();
        // 只选了刮蜡：万龙服务中心一口价，没有消费记录的会员 100（开单时同样这么算）
        int id = await AddOrder(f, CoachFixture.StudentA, new Care { equipment = "双板", need_unwax = 1, biz_type = "雪季养护", entertain = true });
        var care = await OnlyCare(f, id);
        Assert.Equal(100.0, care.displayCharge);
        Assert.Equal(100.0, care.displayDiscount);
    }

    [Fact]
    public async Task FreeCare_WithNothingToCharge_ShowsZeroAndZero()
    {
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA, new Care { equipment = "双板", biz_type = "雪季养护", warranty = true });
        var care = await OnlyCare(f, id);
        Assert.Equal(0.0, care.displayCharge);
        Assert.Equal(0.0, care.displayDiscount);
    }

    [Fact]
    public async Task ListingDoesNotChangeTheStoredAmounts()
    {
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA, Double(c => c.entertain = true));
        await List(f);
        var stored = await f.NewContext().care.AsNoTracking().SingleAsync(c => c.order_id == id);
        Assert.Equal((0.0, 0.0, 0.0, 0.0), (stored.common_charge, stored.repair_charge, stored.discount, stored.ticket_discount));
    }

    [Fact]
    public async Task EachCareOfAnOrderIsPricedOnItsOwn()
    {
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA,
            Double(c => c.entertain = true),
            Single(c => { c.common_charge = 120; c.discount = 20; }));
        var cares = (await List(f)).Single(o => o.id == id).cares.OrderBy(c => c.id).ToList();
        Assert.Equal(new[] { 168.0, 120.0 }, cares.Select(c => c.displayCharge!.Value).ToArray());
        Assert.Equal(new[] { 168.0, 20.0 }, cares.Select(c => c.displayDiscount!.Value).ToArray());
    }

    [Fact]
    public async Task WhenPricingFails_TheListStillWorks_AndFreeCaresJustHaveNoDisplayAmounts()
    {
        // 价目读不出来时不能把整个列表弄坏：免费装备不带展示金额（小程序退回显示库里的值），普通装备照常
        await using var f = await CoachFixture.Create();
        int id = await AddOrder(f, CoachFixture.StudentA,
            Double(c => c.entertain = true),
            Single(c => { c.common_charge = 120; c.discount = 20; }));
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE product_ticket_template");
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE product");

        var cares = (await List(f)).Single(o => o.id == id).cares.OrderBy(c => c.id).ToList();

        Assert.Null(cares[0].displayCharge);
        Assert.Null(cares[0].displayDiscount);
        Assert.Equal(120.0, cares[1].displayCharge);
        Assert.Equal(20.0, cares[1].displayDiscount);
    }
}

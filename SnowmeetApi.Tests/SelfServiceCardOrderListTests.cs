using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 零售订单列表（员工用）不显示「顾客自助购买次卡/季卡、但还没付款」的订单：
/// 腾讯小程序安全检测会用固定账号反复走自助购买流程（PlaceMyPunchCardOrder）下单后不付款，
/// 每天堆出几单 ¥4500 的待支付单。付过款的自助卡单是真实收入，仍然要显示。
/// 过滤只加在员工列表接口 GetOrdersByStaff 上，GetCommonOrders 不动（会员自己查订单也走它）。
/// </summary>
public class SelfServiceCardOrderListTests
{
    private static Retail Card(string retailType = "租赁卡类") => new() { product_id = 716, sale_price = 1500, deal_price = 1500, order_type = "普通", retail_type = retailType, valid = 1 };

    private static Order SelfService(int id, string retailType = "租赁卡类", int items = 3) => new()
    {
        id = id, type = "零售", staff_id = null, valid = 1,
        retails = Enumerable.Range(0, items).Select(_ => Card(retailType)).ToList(),
        payments = new() { new OrderPayment { pay_method = "微信支付", status = "待支付", amount = 4500 } }
    };

    [Fact]
    public void UnpaidSelfServiceCardOrders_AreHidden_BothRentalAndCareCards()
    {
        var kept = OrderQueryRules.ExcludeUnpaidSelfServiceCardOrders(new List<Order> { SelfService(1), SelfService(2, "养护卡类", 1) });
        Assert.Empty(kept);
    }

    [Fact]
    public void PaidSelfServiceCardOrders_StayVisible()
    {
        var paid = SelfService(1);
        paid.payments = new() { new OrderPayment { pay_method = "微信支付", status = "支付成功", amount = 4500 } };
        Assert.Equal(new[] { 1 }, OrderQueryRules.ExcludeUnpaidSelfServiceCardOrders(new List<Order> { paid }).Select(o => o.id));
    }

    [Fact]
    public void OrderMarkedPaidByFlowStatus_StaysVisibleEvenWithoutPaymentRows()
    {
        // 保守：任一支付信号成立就当已付款，宁可多显示也不能把真实收入藏起来
        var paid = SelfService(1);
        paid.payments = new();
        paid.pay_flow_status = "已支付";
        Assert.Single(OrderQueryRules.ExcludeUnpaidSelfServiceCardOrders(new List<Order> { paid }));
    }

    [Fact]
    public void StaffCreatedCardOrders_StayVisibleEvenIfUnpaid()
    {
        var byStaff = SelfService(1);
        byStaff.staff_id = 7;
        Assert.Single(OrderQueryRules.ExcludeUnpaidSelfServiceCardOrders(new List<Order> { byStaff }));
    }

    [Fact]
    public void OtherRetailOrders_StayVisible()
    {
        var mixed = SelfService(1);
        mixed.retails.Add(new Retail { retail_type = "雪具", order_type = "普通", valid = 1 });     // 卡 + 别的商品
        var other = SelfService(2, "雪具", 1);                                                      // 非卡类零售
        var noItems = SelfService(3, items: 0);                                                      // 没有明细
        var kept = OrderQueryRules.ExcludeUnpaidSelfServiceCardOrders(new List<Order> { mixed, other, noItems }).Select(o => o.id).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, kept);
    }

    [Fact]
    public void KeepsOrderOfRemainingOrders_AndDoesNotMutateInput()
    {
        var input = new List<Order> { SelfService(5), new Order { id = 4, type = "零售", staff_id = 7 }, SelfService(3), new Order { id = 2, type = "零售", staff_id = 7 } };
        var kept = OrderQueryRules.ExcludeUnpaidSelfServiceCardOrders(input);
        Assert.Equal(new[] { 4, 2 }, kept.Select(o => o.id));
        Assert.Equal(4, input.Count);
    }

    [Fact]
    public async Task GetOrdersByStaff_Retail_HidesUnpaidSelfServiceCardOrders_ButOtherTypesAreUntouched()
    {
        await using var f = await CoachFixture.Create();
        void Add(int id, string type, int? staffId, string status, string retailType)
        {
            f.Db.order.Add(new Order
            {
                id = id, type = type, shop = "万龙体验中心", valid = 1, staff_id = staffId, member_id = CoachFixture.StudentA,
                biz_date = DateTime.Now, create_date = DateTime.Now, pay_option = "普通",
                retails = new() { new Retail { product_id = 716, sale_price = 1500, deal_price = 1500, order_type = "普通", retail_type = retailType, valid = 1, create_date = DateTime.Now } },
                payments = new() { new OrderPayment { pay_method = "微信支付", status = status, amount = 1500, valid = 1, create_date = DateTime.Now } }
            });
        }
        Add(9001, "零售", null, "待支付", "租赁卡类");   // 自助 + 未付款 → 隐藏
        Add(9002, "零售", null, "支付成功", "租赁卡类");  // 自助 + 已付款 → 显示
        Add(9003, "零售", 9, "待支付", "租赁卡类");       // 店员开的卡单，未付款 → 显示
        Add(9004, "零售", null, "待支付", "雪具");        // 非卡类 → 显示
        await f.Db.SaveChangesAsync();

        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaff(null, null, "零售", null, DateTime.Today, DateTime.Today, "session-7", null);

        var api = (ApiResult<List<Order>>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        Assert.Equal(new[] { 9004, 9003, 9002 }, api.data!.Select(o => o.id).OrderByDescending(i => i));

        // 会员自己查订单走的是 GetCommonOrders，不能被这个过滤影响
        var common = await new OrderController(f.NewContext(), f.Config, new HttpContextAccessor())
            .GetCommonOrders(null, null, CoachFixture.StudentA, null, "零售", null, null);
        Assert.Contains(common, o => o.id == 9001);
    }
}

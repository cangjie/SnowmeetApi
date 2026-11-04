using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class CareOrderPagedTotalsTests
{
    [Fact]
    public void TotalIncludesAllFilteredPagesAndUsesNetReceivedAmount()
    {
        var orders = new List<Order>
        {
            new() { id = 1, contact_num = "13800137788", biz_date = new DateTime(2026, 11, 1), payments = new() { new OrderPayment { pay_method = "微信支付", status = "支付成功", amount = 100, refunds = new() { new OrderPaymentRefund { amount = 20, state = 1 } } } } },
            new() { id = 2, contact_num = "13800137788", biz_date = new DateTime(2026, 11, 2), payments = new() { new OrderPayment { pay_method = "现金", status = "支付成功", amount = 50 }, new OrderPayment { pay_method = "微信支付", status = "待支付", amount = 300 } } },
            new() { id = 3, contact_num = "13900136699", payments = new() { new OrderPayment { pay_method = "现金", status = "支付成功", amount = 999 } } }
        };
        var filtered = OrderQueryRules.FilterByCustomerCellSuffix(orders, "7788");
        var first = OrderController.PagedOrderResult.Create(filtered, 1, 1, true);
        var second = OrderController.PagedOrderResult.Create(filtered, 2, 1, true);
        Assert.Equal(2, first.total);
        Assert.Equal(130, first.totalEarnAmount);
        Assert.Equal(130, second.totalEarnAmount);
        Assert.Equal(2, Assert.Single(first.items).id);
        Assert.Equal(1, Assert.Single(second.items).id);
        Assert.Equal(0, OrderController.PagedOrderResult.Create(new List<Order>(), 1, 10, true).totalEarnAmount);
    }
}

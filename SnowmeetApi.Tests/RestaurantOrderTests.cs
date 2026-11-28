using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

public class RestaurantOrderTests
{
    private static RestaurantOrderRequest Request(string token) => new() { requestId = token, tableNo = "卡1", amount = 38.50m, memberId = CoachFixture.StudentA, isTest = true };

    [Fact]
    public async Task CheckoutTimeIsRecordedAndChangingItOnRetryIsRejected()
    {
        await using var fixture = await CoachFixture.Create();
        var token = Guid.NewGuid().ToString();
        RestaurantOrderRequest Make(string checkoutTime) => System.Text.Json.JsonSerializer.Deserialize<RestaurantOrderRequest>(
            $"{{\"requestId\":\"{token}\",\"tableNo\":\"卡1\",\"amount\":38.50,\"isTest\":true,\"checkoutTime\":\"{checkoutTime}\"}}")!;
        var service = new RestaurantOrderService(fixture.NewContext());
        var order = await service.CreateAsync(Make("2026-11-27 18:35"), 9);
        var infoJson = System.Text.Json.JsonSerializer.SerializeToElement(order.restaurantInfo);
        Assert.Equal("2026-11-27T18:35:00", infoJson.GetProperty("checkout_time").GetString());
        var loaded = await new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() }).GetOrder(order.id);
        Assert.Equal("2026-11-27T18:35:00", System.Text.Json.JsonSerializer.SerializeToElement(loaded.restaurantInfo).GetProperty("checkout_time").GetString());
        await service.CreateAsync(Make("2026-11-27 18:35"), 9);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Make("2026-11-27 18:36"), 9));
    }

    [Fact]
    public async Task OlderClientDefaultsCheckoutTimeToCurrentMinuteAndCanRetry()
    {
        await using var fixture = await CoachFixture.Create();
        var service = new RestaurantOrderService(fixture.NewContext());
        var request = Request(Guid.NewGuid().ToString());
        var before = DateTime.Now.AddMinutes(-1);
        var order = await service.CreateAsync(request, 9);
        var checkoutTime = order.restaurantInfo!.checkout_time!.Value;
        Assert.InRange(checkoutTime, before, DateTime.Now);
        Assert.Equal(0, checkoutTime.Second);
        Assert.Equal(order.id, (await service.CreateAsync(request, 9)).id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-02-30 18:35")]
    [InlineData("2026-11-27 18:35:00")]
    public async Task InvalidCheckoutTimeDoesNotCreateAnOrder(string checkoutTime)
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString());
        request.checkoutTime = checkoutTime;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RestaurantOrderService(fixture.NewContext()).CreateAsync(request, 9));
        Assert.Equal(0, await fixture.NewContext().order.CountAsync(o => o.type == "餐饮"));
    }

    [Fact]
    public async Task CreatesRestaurantOrderWithTableAndIdempotentRetry()
    {
        await using var fixture = await CoachFixture.Create();
        var service = new RestaurantOrderService(fixture.NewContext());
        var request = Request(Guid.NewGuid().ToString());
        var order = await service.CreateAsync(request, 9);
        var retry = await service.CreateAsync(request, 9);
        Assert.Equal(order.id, retry.id);
        Assert.Equal("多呆一会儿吧", order.shop);
        Assert.Equal("餐饮", order.type);
        Assert.StartsWith("QJ_CY_" + DateTime.Now.ToString("yyMMdd") + "_", order.code);
        Assert.Equal(38.50, order.total_amount);
        Assert.Equal(38.50, order.paying_amount);
        Assert.Equal(1, order.is_test);
        Assert.Equal("卡1", order.restaurantInfo!.table_no);
        Assert.Equal(1, await fixture.NewContext().order.CountAsync(o => o.type == "餐饮"));
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var loaded = await controller.GetOrder(order.id);
        Assert.Equal("卡1", loaded.restaurantInfo!.table_no);
    }

    [Fact]
    public async Task ReusedRequestCannotChangeTableOrAmount()
    {
        await using var fixture = await CoachFixture.Create();
        var service = new RestaurantOrderService(fixture.NewContext());
        var request = Request(Guid.NewGuid().ToString());
        await service.CreateAsync(request, 9);
        request.amount = 99m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request, 9));
    }

    [Theory]
    [InlineData(0, 38.50)]
    [InlineData(8.50, 30)]
    [InlineData(38.50, 0)]
    public async Task DiscountIsRecordedAndOnlyNetAmountIsPayable(double discount, double net)
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString());
        request.discount = (decimal)discount;
        var service = new RestaurantOrderService(fixture.NewContext());
        var order = await service.CreateAsync(request, 9);
        var retry = await service.CreateAsync(request, 9);
        Assert.Equal(order.id, retry.id);
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var loaded = await controller.GetOrder(order.id);
        Assert.Equal(38.50, loaded.total_amount);
        Assert.Equal((decimal)discount, loaded.restaurantInfo!.discount_amount);
        Assert.Equal(discount, loaded.discountAmount);
        Assert.Equal(net, loaded.totalCharge);
        Assert.Equal(net, loaded.paying_amount);
        var rows = await fixture.NewContext().discount.Where(x => x.order_id == order.id && x.valid == 1).ToListAsync();
        Assert.Equal(discount > 0 ? 1 : 0, rows.Count);
        if (rows.Count > 0)
        {
            Assert.Equal(discount, rows[0].amount);
            Assert.Null(rows[0].biz_id); // 不关联旧的 fd_order 菜品明细
        }
        request.discount = request.discount == 0 ? 1 : 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request, 9));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(38.51)]
    [InlineData(0.001)]
    public async Task InvalidDiscountDoesNotCreateAnOrder(double discount)
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString()); request.discount = (decimal)discount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RestaurantOrderService(fixture.NewContext()).CreateAsync(request, 9));
        Assert.Equal(0, await fixture.NewContext().order.CountAsync(o => o.type == "餐饮"));
    }

    [Theory]
    [InlineData("微信支付")]
    [InlineData("支付宝")]
    public async Task OnlinePaymentUsesNetAmountAndRejectsDifferentAmount(string method)
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString()); request.discount = 8.50m;
        var order = await new RestaurantOrderService(fixture.NewContext()).CreateAsync(request, 9);
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        ApiResult<OrderPayment?> Unwrap(object? value) => Assert.IsType<ApiResult<OrderPayment?>>(Assert.IsType<OkObjectResult>(value).Value);
        ApiResult<OrderPayment?> rejected = method == "微信支付"
            ? Unwrap((await controller.GetWepayPayment(order.id, 38.5, "session-1")).Result)
            : Unwrap((await controller.GetAlipayMiniPayment(order.id, 38.5, "session-1")).Result);
        Assert.Equal(1, rejected.code);
        Assert.Equal("付款金额与实收金额不一致", rejected.message);
        ApiResult<OrderPayment?> result = method == "微信支付"
            ? Unwrap((await controller.GetWepayPayment(order.id, null, "session-1")).Result)
            : Unwrap((await controller.GetAlipayMiniPayment(order.id, null, "session-1")).Result);
        Assert.Equal(0, result.code);
        Assert.Equal(30, result.data!.amount);
        if (method == "微信支付") Assert.Equal(15, result.data.mch_id);
    }

    [Fact]
    public async Task HistoricalRestaurantOrderRetainsPartialAlipayPayment()
    {
        await using var fixture = await CoachFixture.Create();
        var order = await new RestaurantOrderService(fixture.NewContext()).CreateAsync(Request(Guid.NewGuid().ToString()), 9);
        var db = fixture.NewContext();
        var historical = await db.order.AsTracking().SingleAsync(o => o.id == order.id);
        historical.order_source = null;
        await db.SaveChangesAsync();
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var response = await controller.GetAlipayMiniPayment(order.id, 10, "session-1");
        var result = Assert.IsType<ApiResult<OrderPayment?>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code);
        Assert.Equal(10, result.data!.amount);
    }

    [Fact]
    public async Task FullyDiscountedOrderUsesFreeConfirmation()
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString()); request.discount = request.amount;
        var order = await new RestaurantOrderService(fixture.NewContext()).CreateAsync(request, 9);
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var payment = await controller.GetWepayPayment(order.id, null, "session-1");
        var rejected = Assert.IsType<ApiResult<OrderPayment?>>(Assert.IsType<OkObjectResult>(payment.Result).Value);
        Assert.Equal("本单无需在线支付", rejected.message);
        var response = await controller.EffectUnpaidOrder(order.id, "免费", false, "session-1");
        var result = Assert.IsType<ApiResult<Order>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code);
        var saved = await fixture.NewContext().order.AsNoTracking().SingleAsync(o => o.id == order.id);
        Assert.Equal(1, saved.dealed);
        Assert.Equal(0, await fixture.NewContext().orderPayment.CountAsync(p => p.order_id == order.id));
    }

    [Fact]
    public async Task CashPaymentRecordsTheNetAmount()
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString()); request.discount = 8.50m;
        var order = await new RestaurantOrderService(fixture.NewContext()).CreateAsync(request, 9);
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var response = await controller.EffectUnpaidOrder(order.id, "现金", false, "session-1");
        var result = Assert.IsType<ApiResult<Order>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code);
        var payment = await fixture.NewContext().orderPayment.AsNoTracking().SingleAsync(p => p.order_id == order.id);
        Assert.Equal(30, payment.amount);
        Assert.Equal("支付成功", payment.status);
    }

    [Fact]
    public void CentRoundingDoesNotMarkFullyPaidRestaurantOrderAsPartiallyPaid()
    {
        var order = new Order {
            type = "餐饮", shop = "多呆一会儿吧", order_source = RestaurantOrder.OrderSource, total_amount = 0.05,
            discounts = new List<Discount> { new() { amount = 0.03, valid = 1 } },
            payments = new List<OrderPayment> { new() { amount = 0.02, valid = 1, pay_method = "现金", status = "支付成功" } }
        };
        Assert.Equal(0.02, order.totalCharge);
        Assert.Equal("支付成功", order.orderStatus);
    }

    [Theory]
    [InlineData("微信支付", "wepay")]
    [InlineData("支付宝", "alipay")]
    public async Task CustomerPaymentGetsCurrentRestaurantOrderAndTable(string method, string expectedStage)
    {
        await using var fixture = await CoachFixture.Create();
        var order = await new RestaurantOrderService(fixture.NewContext()).CreateAsync(Request(Guid.NewGuid().ToString()), 9);
        var payment = new OrderPayment { order_id = order.id, pay_method = method, amount = 38.50, status = "待支付", valid = 1 };
        fixture.Db.orderPayment.Add(payment);
        await fixture.Db.SaveChangesAsync();
        var controller = new OrderController(fixture.NewContext(), fixture.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var response = await controller.GetOrderFromPaymentByCustomer(payment.id, "session-3");
        var result = Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code);
        Assert.Equal(order.id, result.data!.id);
        Assert.Equal("卡1", result.data.restaurantInfo!.table_no);
        var scan = new ScanQrCode { scaned = 1, scaner_member_id = CoachFixture.StudentA, cell = "13800000000", scan_time = payment.create_date.AddMinutes(-1) };
        payment.order = result.data;
        var stage = ReceptCheckinRules.CustomerStage(scan, new[] { payment }, DateTime.Now);
        Assert.Equal(expectedStage, stage.stage);
        Assert.Equal(payment.id, stage.paymentId);
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData("卡1", 0)]
    [InlineData("卡1", -1)]
    public async Task InvalidDataCannotCreateOrder(string table, decimal amount)
    {
        await using var fixture = await CoachFixture.Create();
        var request = Request(Guid.NewGuid().ToString()); request.tableNo = table; request.amount = amount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RestaurantOrderService(fixture.NewContext()).CreateAsync(request, 9));
        Assert.Equal(0, await fixture.NewContext().order.CountAsync(o => o.type == "餐饮"));
    }
}

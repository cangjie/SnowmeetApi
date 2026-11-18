using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 2026-11-17 用户定三条：
//   1) 万龙租赁商户（1604236346，wepay_key id 5）下的租赁订单一律是「非分账订单」（下微信单不带 profit_sharing，need_share = 0）；
//   2) 所有给万龙生成的分账一律标成不自动分账（生成时、重算时都是）；
//   3) 租赁订单的微信付款立刻解冻剩余资金（夜间「关闭微信分账」任务不用再等分账成功后的 1 天）；其它类型（雪票等）仍按原规则。
// 起因：微信的最大可分账金额按「付款 − 退款」的净额算，万龙租赁应分是租金的一半、押金退完后占净额 50%，11-01 起几乎都被微信拒绝。
public class WanlongNoShareRulesTests
{
    // ---------- 0) 要给万龙分账的门店 ----------

    [Theory]
    [InlineData("万龙体验中心", true)]
    [InlineData("万龙服务中心", true)]
    [InlineData(" 万龙服务中心 ", true)]
    [InlineData("崇礼旗舰店", false)]
    [InlineData("万龙", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Both_wanlong_shops_share_rentals_with_wanlong(string? shop, bool expected)
        => Assert.Equal(expected, WanlongShareRules.IsWanlongShop(shop));

    // ---------- 1) 非分账订单 ----------

    [Theory]
    [InlineData("租赁", 5, true)]     // 万龙租赁商户下的租赁订单
    [InlineData(" 租赁 ", 5, true)]
    [InlineData("租赁", 10, false)]   // 旗舰租赁商户本来就不分账，不在这条规则里
    [InlineData("租赁", 17, false)]
    [InlineData("租赁", null, false)]
    [InlineData("雪票", 5, false)]    // 别的订单类型不受影响
    [InlineData("养护", 5, false)]
    [InlineData(null, 5, false)]
    public void Rental_orders_under_the_wanlong_rental_merchant_are_non_sharing_orders(string? orderType, int? mchId, bool expected)
        => Assert.Equal(expected, WanlongShareRules.IsNoShareOrder(orderType, mchId));

    [Fact]
    public void The_wanlong_rental_merchant_is_the_one_GetMchId_picks_for_wanlong_rentals()
    {
        var helper = new TenpayControllerProbe();
        Assert.Equal(WanlongShareRules.WanlongRentalMchId, helper.MchIdOf("万龙体验中心", "租赁"));
        Assert.Equal(WanlongShareRules.WanlongRentalMchId, helper.MchIdOf("万龙服务中心", "租赁"));
        Assert.NotEqual(WanlongShareRules.WanlongRentalMchId, helper.MchIdOf("万龙体验中心", "养护"));
        Assert.NotEqual(WanlongShareRules.WanlongRentalMchId, helper.MchIdOf("崇礼旗舰店", "租赁"));
    }

    // GetMchId 只看订单的 shop / type，不碰数据库；用 null 依赖构造会被基类拒绝，这里直接走它的公开方法
    private sealed class TenpayControllerProbe
    {
        public int MchIdOf(string shop, string type)
        {
            using var db = ShareFixture.NewDbAsync().GetAwaiter().GetResult();
            var controller = new TenpayController(db, ShareFixture.Config,
                new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() });
            return controller.GetMchId(new Order { shop = shop, type = type });
        }
    }

    // ---------- 2) 给万龙生成的分账一律不自动分 ----------

    [Theory]
    [InlineData("微信支付", 5000, 0, 400)]    // 远没超 30%
    [InlineData("支付宝", 5000, 0, 400)]
    [InlineData("微信支付", 500, 430, 70)]    // 超净额 30%
    public async Task Every_wanlong_share_record_is_generated_as_not_auto(string payMethod, double paid, double refunded, double rent)
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync(payMethod, paid, refunded, rent);
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var order = await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1);
        var orderShares = await helper.CreateOrderShares(order);
        db.ChangeTracker.Clear();
        var rows = new List<PaymentShare>();
        foreach (var share in orderShares) rows.AddRange(await helper.CreatePaymentShare(share));

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.can_not_share));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.memo)));
    }

    [Fact]
    public async Task Under_the_cap_the_memo_is_the_new_wanlong_rule_not_an_over_cap_text()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var share = Assert.Single(await helper.CreateOrderShares(await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1)));
        db.ChangeTracker.Clear();

        var row = Assert.Single(await helper.CreatePaymentShare(share));

        Assert.True(row.can_not_share);
        Assert.Equal(WanlongShareRules.NoAutoMemo, row.memo);
    }

    [Fact]
    public async Task The_nightly_job_never_picks_up_a_wanlong_share()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var share = Assert.Single(await helper.CreateOrderShares(await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1)));
        db.ChangeTracker.Clear();
        await helper.CreatePaymentShare(share);
        db.ChangeTracker.Clear();

        // 订单 11-01 关单，11-02 起任何一晚的任务都不该提交它
        Assert.Empty(await helper.GetDueWanlongShares(new DateTime(2026, 11, 20)));
    }

    [Fact]
    public async Task Reevaluating_an_unsubmitted_wanlong_record_keeps_it_not_auto()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var share = Assert.Single(await helper.CreateOrderShares(await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1)));
        db.ChangeTracker.Clear();
        await helper.CreatePaymentShare(share);
        db.ChangeTracker.Clear();

        await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http).ReevaluateAsync(1);

        var row = await db.paymentShare.AsNoTracking().SingleAsync(r => r.share_id == share.id);
        Assert.True(row.can_not_share);
        Assert.Equal(WanlongShareRules.NoAutoMemo, row.memo);
    }

    // ---------- 3) 租赁付款立刻解冻 ----------

    [Theory]
    [InlineData("租赁", 5, true)]       // 万龙租赁商户（1604236346）下的租赁订单
    [InlineData(" 租赁", 5, true)]
    [InlineData("租赁", 10, false)]     // 旗舰租赁等其它商户的老付款不动（用户只要求解冻万龙租赁商户）
    [InlineData("租赁", 17, false)]
    [InlineData("租赁", null, false)]
    [InlineData("雪票", 5, false)]      // 雪票还有 1 元分账等着提交
    [InlineData("养护", 5, false)]
    [InlineData(null, 5, false)]
    public void Only_wanlong_rental_merchant_rental_payments_unfreeze_immediately(string? orderType, int? mchId, bool expected)
        => Assert.Equal(expected, WanlongShareRules.UnfreezeImmediately(orderType, mchId));

    // 11-17 实跑：189 笔里 105 笔回「订单已完结分账，不支持分账」、62 笔回「订单已逾期解冻，不支持分账」，这两种记为已完结，不再每晚重试
    [Theory]
    [InlineData("订单已完结分账，不支持分账", true)]
    [InlineData("订单已逾期解冻，不支持分账", true)]
    [InlineData("INVALID_REQUEST:订单已完结分账，不支持分账", true)]
    [InlineData("分账金额超出最大分账比例", false)]
    [InlineData("系统繁忙，请稍后再试", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_two_already_closed_wechat_replies_count_as_closed(string? message, bool expected)
        => Assert.Equal(expected, WanlongShareRules.IsAlreadyClosedAtWechat(message));

    private static Task<bool> CanClose(ApplicationDBContext db, int paymentId)
    {
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var method = typeof(OrderShareController).GetMethod("CanCloseWechatShareAsync");
        Assert.NotNull(method);
        return (Task<bool>)method!.Invoke(helper, new object[] { paymentId, DateTime.Today })!;
    }

    [Fact]
    public async Task A_rental_wechat_payment_can_be_unfrozen_right_away_even_without_any_successful_share()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 4600, 400);
        await db.orderPayment.Where(p => p.id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.mch_id, WanlongShareRules.WanlongRentalMchId));
        db.ChangeTracker.Clear();

        Assert.True(await CanClose(db, 4));
    }

    [Fact]
    public async Task A_rental_payment_of_another_merchant_is_not_unfrozen_right_away()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 4600, 400);
        await db.orderPayment.Where(p => p.id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.mch_id, 10));   // 旗舰租赁
        db.ChangeTracker.Clear();

        Assert.False(await CanClose(db, 4));
    }

    [Fact]
    public async Task Other_order_types_still_wait_for_the_old_rule()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 4600, 400);
        await db.orderPayment.Where(p => p.id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.mch_id, WanlongShareRules.WanlongRentalMchId));
        await db.order.ExecuteUpdateAsync(s => s.SetProperty(o => o.type, "雪票"));
        db.ChangeTracker.Clear();

        Assert.False(await CanClose(db, 4));
    }

    [Fact]
    public async Task A_rental_payment_with_a_pending_auto_share_is_not_unfrozen()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        await db.orderPayment.Where(p => p.id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.mch_id, WanlongShareRules.WanlongRentalMchId));
        db.ChangeTracker.Clear();
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 10, null, DateTime.Today.AddDays(7), true, "auto");
        db.ChangeTracker.Clear();

        Assert.False(await CanClose(db, 4));   // 还有分销商的自动分账没提交，不能解冻
    }

    [Fact]
    public async Task A_payment_that_is_not_a_sharing_order_is_never_picked_for_unfreezing()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 4600, 400);
        await db.orderPayment.Where(p => p.id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.mch_id, WanlongShareRules.WanlongRentalMchId).SetProperty(p => p.need_share, 0));
        db.ChangeTracker.Clear();

        Assert.False(await CanClose(db, 4));
    }

    [Fact]
    public async Task An_already_unfrozen_payment_is_not_picked_again()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 4600, 400);
        await db.orderPayment.Where(p => p.id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.mch_id, WanlongShareRules.WanlongRentalMchId).SetProperty(p => p.share_close_date, DateTime.Now));
        db.ChangeTracker.Clear();

        Assert.False(await CanClose(db, 4));
    }
}

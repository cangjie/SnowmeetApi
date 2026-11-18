using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 2026-11-18 用户定：微信分账成功的付款，第二天夜里随定时任务（23:15 OrderShare/CloseWepayShare）解冻剩余资金，
// 原来是「最后一笔分账成功满 4 天」，现在缩短为 1 天多（按日期算：分账成功的第二天起就可以）。其它条件不变。
public sealed class WechatUnfreezeWaitTests
{
    private static readonly DateTime NightJob = new(2026, 11, 18, 23, 15, 0);

    [Fact]
    public void The_wait_is_one_calendar_day()
    {
        Assert.Equal(1, ShareWorkflowRules.WechatUnfreezeWaitDays);
    }

    [Theory]
    [InlineData("2026-11-18 08:00", false)]   // 当天分账成功，当晚的任务不解冻
    [InlineData("2026-11-18 23:00", false)]
    [InlineData("2026-11-17 08:26", true)]    // 昨天成功，今晚的任务就解冻
    [InlineData("2026-11-17 23:59", true)]    // 按日期不按小时：昨天深夜成功，今晚也解冻
    [InlineData("2026-11-16 10:00", true)]    // 更早的也解冻（比如上一晚任务没跑成）
    [InlineData("2026-11-10 10:00", true)]
    public void Unfreeze_is_due_from_the_day_after_the_last_successful_share(string lastShare, bool expected)
    {
        Assert.Equal(expected, ShareWorkflowRules.UnfreezeWaitOver(DateTime.Parse(lastShare), NightJob));
    }

    [Fact]
    public void A_share_at_the_end_of_one_day_is_due_right_after_midnight()
    {
        // 日期比较：11-18 23:50 的分账，11-19 的任何一次任务都算到期
        Assert.True(ShareWorkflowRules.UnfreezeWaitOver(new DateTime(2026, 11, 18, 23, 50, 0), new DateTime(2026, 11, 19, 0, 5, 0)));
        Assert.False(ShareWorkflowRules.UnfreezeWaitOver(new DateTime(2026, 11, 18, 23, 50, 0), new DateTime(2026, 11, 18, 23, 59, 0)));
    }

    // ---------- 经 CanCloseWechatShareAsync 走完整条件 ----------

    private static async Task<ApplicationDBContext> SkiPassPaymentWithSuccessfulShareAsync(DateTime successTime)
    {
        var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 500, 0, 70);
        await db.order.ExecuteUpdateAsync(s => s.SetProperty(o => o.type, "雪票"));   // 非万龙租赁，走「分账成功后等 N 天」的规则
        db.orderShare.Add(new OrderShare { id = 900, order_id = 1, relation_id = 4, amount = 1, valid = true, dealed = true,
            create_date = successTime.AddHours(-1), auto_share = true });
        db.paymentShare.Add(new PaymentShare { id = 901, payment_id = 4, share_id = 900, out_trade_no = "WT_ZL_261031_00001_FZ_01", amount = 1,
            success = true, valid = true, submit_time = successTime, response_time = successTime, create_date = successTime.AddHours(-1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static Task<bool> CanClose(ApplicationDBContext db, DateTime now)
    {
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var method = typeof(OrderShareController).GetMethod("CanCloseWechatShareAsync");
        Assert.NotNull(method);
        return (Task<bool>)method!.Invoke(helper, new object[] { 4, now })!;
    }

    [Fact]
    public async Task A_payment_shared_successfully_yesterday_is_unfrozen_by_tonights_job()
    {
        await using var db = await SkiPassPaymentWithSuccessfulShareAsync(new DateTime(2026, 11, 17, 8, 26, 0));
        Assert.True(await CanClose(db, NightJob));
    }

    [Fact]
    public async Task A_payment_shared_successfully_today_waits_for_tomorrows_job()
    {
        await using var db = await SkiPassPaymentWithSuccessfulShareAsync(new DateTime(2026, 11, 18, 10, 11, 0));
        Assert.False(await CanClose(db, NightJob));
        Assert.True(await CanClose(db, NightJob.AddDays(1)));
    }

    [Fact]
    public async Task Bank_transfer_batches_follow_the_same_one_day_wait()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var orderService = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);
        var bank = await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "bank");
        await new ShareListService(db).CompleteManualAsync(ShareFixture.Manager(300), new[] { bank.orderShareId }, "MS-ONE-DAY");
        await db.order.ExecuteUpdateAsync(s => s.SetProperty(o => o.type, "养护"));
        var batch = await db.shareBatch.AsNoTracking().SingleAsync(b => b.batch_no == "MS-ONE-DAY");

        batch.create_date = NightJob.Date;                // 今天记的账：今晚不解冻
        db.shareBatch.Update(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False(await CanClose(db, NightJob));

        batch.create_date = NightJob.Date.AddDays(-1);    // 昨天记的账：今晚解冻
        db.shareBatch.Update(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True(await CanClose(db, NightJob));
    }

    [Fact]
    public async Task Everything_else_is_unchanged_pending_channel_shares_still_block_the_unfreeze()
    {
        await using var db = await SkiPassPaymentWithSuccessfulShareAsync(new DateTime(2026, 11, 16, 10, 0, 0));
        Assert.True(await CanClose(db, NightJob));

        var intro = await ShareFixture.AddIntroducerAsync(db, 101);
        var orderService = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);
        await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 10, null, DateTime.Today.AddDays(7), true, "auto");
        Assert.False(await CanClose(db, NightJob));   // 还有待提交的自动分账，仍然不解冻
    }

    [Fact]
    public async Task Everything_else_is_unchanged_a_payment_without_any_successful_share_is_still_not_unfrozen()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 500, 0, 70);
        await db.order.ExecuteUpdateAsync(s => s.SetProperty(o => o.type, "雪票"));
        db.ChangeTracker.Clear();
        Assert.False(await CanClose(db, NightJob));
    }
}

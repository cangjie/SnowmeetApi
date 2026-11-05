using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

public class OrderShareServiceTests
{
    private const string WechatOverMemo = "分账金额超过实收（扣除退款）的30%，不自动分账";

    private static OrderShareService Svc(ApplicationDBContext db) => new(db, ShareFixture.Config, ShareFixture.Http);

    // 关单后生成万龙那份应分和分账记录（和夜间关单任务一样）
    private static async Task<OrderShare> WanlongAsync(ApplicationDBContext db)
    {
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        await helper.CreateOrderShares(await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1));
        db.ChangeTracker.Clear();
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == 4 && s.valid);
        await helper.CreatePaymentShare(share);
        db.ChangeTracker.Clear();
        return share;
    }

    private static Task<List<PaymentShare>> RecordsAsync(ApplicationDBContext db, int shareId)
        => db.paymentShare.AsNoTracking().Where(r => r.share_id == shareId && r.valid).ToListAsync();

    [Fact]
    public async Task Failed_application_log_rolls_back_the_entire_share_application()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 1000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_share_log BEFORE INSERT ON core_data_mod_log WHEN NEW.scene = '设置介绍人分账' BEGIN SELECT RAISE(ABORT, 'injected failure'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.orderShare.AsNoTracking().Where(s => s.relation_id == introducer.relation_id).ToListAsync());
        Assert.Empty(await db.paymentShare.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Order_view_does_not_report_partial_or_unanswered_share_as_success()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 1000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        db.orderShare.Add(new OrderShare { id = 90, order_id = 1, relation_id = intro.relation_id, amount = 40, valid = true, create_date = DateTime.Now });
        db.paymentShare.AddRange(
            new PaymentShare { id = 901, share_id = 90, payment_id = 4, out_trade_no = "A", amount = 20, valid = true, success = true, submit_time = DateTime.Now, response_time = DateTime.Now },
            new PaymentShare { id = 902, share_id = 90, payment_id = 4, out_trade_no = "B", amount = 20, valid = true, success = null, submit_time = DateTime.Now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("处理中", Assert.Single(await Svc(db).ListAsync(1)).state);
        var failed = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 902);
        failed.response_time = DateTime.Now;
        db.paymentShare.Update(failed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("被拒绝", Assert.Single(await Svc(db).ListAsync(1)).state);
    }

    [Fact]
    public async Task Set_creates_an_automatic_share()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 70);   // 万龙 35
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "userId", aliNum: "2088000000000001", aliBound: true);
        await WanlongAsync(db);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 7, "备注");

        Assert.Equal(7, view.amount, 2);
        Assert.Equal("待自动分账", view.state);
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid);
        Assert.Equal(7, share.amount, 2);
        var record = Assert.Single(await RecordsAsync(db, share.id));
        Assert.Equal(7, record.amount, 2);
        Assert.False(record.can_not_share);
        Assert.Equal("备注", record.memo);
    }

    [Fact]
    public async Task Set_that_crosses_the_cap_flips_the_unsubmitted_wanlong_record_to_manual()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 500, 200);   // 万龙 100，门槛 150
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var wanlong = await WanlongAsync(db);
        Assert.False(Assert.Single(await RecordsAsync(db, wanlong.id)).can_not_share);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 60, null);

        Assert.Equal("手动处理", view.state);
        var wanlongRecord = Assert.Single(await RecordsAsync(db, wanlong.id));
        Assert.True(wanlongRecord.can_not_share);
        Assert.Equal(WechatOverMemo, wanlongRecord.memo);
        var introRecord = Assert.Single(await db.paymentShare.AsNoTracking().Where(r => r.share_id != wanlong.id).ToListAsync());
        Assert.True(introRecord.can_not_share);
        Assert.Equal(WechatOverMemo, introRecord.memo);
    }

    [Fact]
    public async Task Set_after_wanlong_was_shared_leaves_it_untouched()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 500, 200);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var wanlong = await WanlongAsync(db);
        var record = await db.paymentShare.AsNoTracking().SingleAsync(r => r.share_id == wanlong.id);
        record.success = true;
        record.submit_time = DateTime.Now;
        db.paymentShare.Update(record);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 60, null);

        var after = await db.paymentShare.AsNoTracking().SingleAsync(r => r.id == record.id);
        Assert.False(after.can_not_share);
        Assert.True(after.success);
        var intro = await db.paymentShare.AsNoTracking().SingleAsync(r => r.share_id != wanlong.id);
        Assert.True(intro.can_not_share);
    }

    [Fact]
    public async Task Set_rejects_bad_requests()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 500, 430, 70);   // 万龙 35，净额 70
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var stopped = await ShareFixture.AddIntroducerAsync(db, memberId: 101);
        var stoppedRow = await db.introducer.AsNoTracking().SingleAsync(i => i.id == stopped.id);
        stoppedRow.valid = 0;
        db.introducer.Update(stoppedRow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await WanlongAsync(db);
        var svc = Svc(db);

        Assert.Equal(3, (await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(100), 1, introducer.id, 7, null))).Code);
        Assert.Equal(1, (await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 0, null))).Code);
        Assert.Equal(1, (await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, stopped.id, 7, null))).Code);
        var over = await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null));
        Assert.Equal("分账合计不能超过实收扣掉已退款", over.Message);

        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        order.closed = 0;
        order.close_date = null;
        db.order.Update(order);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var open = await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 7, null));
        Assert.Equal("该订单还没有关单", open.Message);
    }

    [Fact]
    public async Task Set_twice_updates_the_same_row_until_it_is_submitted()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "userId", aliNum: "2088000000000001", aliBound: true);
        await WanlongAsync(db);
        var svc = Svc(db);

        await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 30, null);
        await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null);

        var shares = await db.orderShare.AsNoTracking().Where(s => s.relation_id == introducer.relation_id && s.valid).ToListAsync();
        var share = Assert.Single(shares);
        Assert.Equal(40, share.amount, 2);
        Assert.Equal(40, Assert.Single(await RecordsAsync(db, share.id)).amount, 2);

        var record = (await RecordsAsync(db, share.id)).Single();
        record.submit_time = DateTime.Now;
        db.paymentShare.Update(record);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 50, null));
        Assert.Equal("已提交，不能修改", ex.Message);
    }

    [Fact]
    public async Task Remove_restores_automatic_sharing_and_refuses_after_submit()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 500, 200);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var wanlong = await WanlongAsync(db);
        var svc = Svc(db);
        await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 60, null);
        var introShare = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid);

        await svc.RemoveIntroducerShareAsync(ShareFixture.Manager(), introShare.id);

        Assert.False((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == introShare.id)).valid);
        Assert.Empty(await RecordsAsync(db, introShare.id));
        var wanlongRecord = Assert.Single(await RecordsAsync(db, wanlong.id));
        Assert.False(wanlongRecord.can_not_share);          // 合计回到 100，门槛 150，万龙那条恢复自动
        Assert.Null(wanlongRecord.memo);

        await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 20, null);
        var again = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid);
        var rec = (await RecordsAsync(db, again.id)).Single();
        rec.submit_time = DateTime.Now;
        db.paymentShare.Update(rec);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<ShareException>(() => svc.RemoveIntroducerShareAsync(ShareFixture.Manager(), again.id));
        Assert.Equal("已提交，不能删除", ex.Message);
    }

    [Fact]
    public async Task Reevaluate_keeps_coupon_records_manual()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        db.Set<Discount>().Add(new Discount { id = 21, amount = 50, order_id = 1, biz_type = "租赁", biz_id = 2, ticket_code = "123456789", valid = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var wanlong = await WanlongAsync(db);
        Assert.True(Assert.Single(await RecordsAsync(db, wanlong.id)).can_not_share);

        await Svc(db).ReevaluateAsync(1);

        var record = Assert.Single(await RecordsAsync(db, wanlong.id));
        Assert.True(record.can_not_share);
        Assert.Equal(ShareMemoRules.TicketMemo, record.memo);
    }

    [Fact]
    public async Task An_introducer_without_alipay_bind_is_flagged_but_wanlong_is_not()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 70);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "userId", aliNum: "2088000000000001", aliBound: false);
        var wanlong = await WanlongAsync(db);

        await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 7, null);

        var intro = await db.paymentShare.AsNoTracking().SingleAsync(r => r.share_id != wanlong.id);
        Assert.True(intro.can_not_share);
        Assert.Equal("未绑定分账关系", intro.memo);
        Assert.False(Assert.Single(await RecordsAsync(db, wanlong.id)).can_not_share);
    }

    [Fact]
    public async Task Void_marks_unsubmitted_introducer_rows_invalid_and_leaves_wanlong()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "userId", aliNum: "2088000000000001", aliBound: true);
        var wanlong = await WanlongAsync(db);
        var svc = Svc(db);
        await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null);

        await svc.VoidUnsubmittedIntroducerSharesAsync(1);

        Assert.Empty(await db.orderShare.AsNoTracking().Where(s => s.relation_id == introducer.relation_id && s.valid).ToListAsync());
        Assert.Empty(await db.paymentShare.AsNoTracking().Where(r => r.share_id != wanlong.id && r.valid).ToListAsync());
        Assert.Single(await RecordsAsync(db, wanlong.id));
    }

    [Fact]
    public async Task Suggested_amount_is_ten_percent_of_rent_after_discounts()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 400);
        Assert.Equal(40, await Svc(db).SuggestedAmountAsync(1), 2);
    }

    [Fact]
    public async Task Trigger_rules_for_rental_orders()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync();
        var order = await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1);
        Assert.Null(ShareTriggerRules.ReasonCannotSet(order));
        order.closed = 0;
        Assert.Equal("该订单还没有关单", ShareTriggerRules.ReasonCannotSet(order));
        order.type = "养护";
        Assert.Equal("该类订单暂不支持设置介绍人分账", ShareTriggerRules.ReasonCannotSet(order));
    }
}

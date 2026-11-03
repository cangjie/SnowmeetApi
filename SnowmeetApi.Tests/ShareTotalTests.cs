using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 自动还是手动，按这一单所有分账的合计判断；介绍人那几行不会被关单时重新生成的万龙应分冲掉。
public class ShareTotalTests
{
    private static OrderShareController Helper(SnowmeetApi.Data.ApplicationDBContext db)
        => new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);

    private static async Task<OrderShare> AddShareAsync(SnowmeetApi.Data.ApplicationDBContext db, int relationId, double amount)
    {
        var share = new OrderShare { order_id = 1, relation_id = relationId, amount = amount, valid = true, create_date = DateTime.Now };
        db.orderShare.Add(share);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return share;
    }

    [Fact]
    public async Task CreateOrderShares_leaves_introducer_shares_alone()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 500, 430, 70);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        await AddShareAsync(db, introducer.relation_id, 7);

        await Helper(db).CreateOrderShares(await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1));
        db.ChangeTracker.Clear();

        Assert.True((await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id)).valid);
        var wanlong = await db.orderShare.AsNoTracking().Where(s => s.relation_id == 4 && s.valid).ToListAsync();
        Assert.Single(wanlong);
        Assert.Equal(35, wanlong[0].amount, 2);
    }

    [Fact]
    public async Task Total_over_the_cap_sends_every_record_to_manual()
    {
        // 微信付 1000、退 500：基数 500，门槛 150；万龙 100 + 介绍人 60 = 160
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 500, 200);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var wanlong = await AddShareAsync(db, 4, 100);
        await AddShareAsync(db, introducer.relation_id, 60);

        var records = await Helper(db).CreatePaymentShare(wanlong);

        var record = Assert.Single(records);
        Assert.Equal(100, record.amount, 2);
        Assert.True(record.can_not_share);
        Assert.Equal("分账金额超过实收（扣除退款）的30%，不自动分账", record.memo);
    }

    [Fact]
    public async Task Under_the_cap_splits_by_remaining_capacity()
    {
        // 两笔微信付款 A（100）、B（900），无退款：门槛 300；万龙 280、介绍人 10
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 100, 0, 560);
        await ShareFixture.AddPaymentAsync(db, 5, 900);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var wanlong = await AddShareAsync(db, 4, 280);
        var intro = await AddShareAsync(db, introducer.relation_id, 10);

        await Helper(db).CreatePaymentShare(wanlong);
        await Helper(db).CreatePaymentShare(intro);

        var all = await db.paymentShare.AsNoTracking().ToListAsync();
        Assert.All(all, r => Assert.False(r.can_not_share));
        Assert.Equal(261, all.Where(r => r.payment_id == 5).Sum(r => r.amount), 2);
        Assert.Equal(29, all.Where(r => r.payment_id == 4).Sum(r => r.amount), 2);
        var introRecord = Assert.Single(all, r => r.share_id == intro.id);
        Assert.Equal(4, introRecord.payment_id);
        Assert.Equal(10, introRecord.amount, 2);
    }

    [Fact]
    public async Task Records_on_the_same_payment_get_distinct_request_numbers()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "userId", aliNum: "2088000000000001", aliBound: true);
        var wanlong = await AddShareAsync(db, 4, 200);
        var intro = await AddShareAsync(db, introducer.relation_id, 40);

        await Helper(db).CreatePaymentShare(wanlong);
        await Helper(db).CreatePaymentShare(intro);
        db.ChangeTracker.Clear();

        var numbers =(await db.paymentShare.AsNoTracking().OrderBy(r => r.id).ToListAsync()).Select(r => r.out_trade_no).ToList();
        Assert.Equal(new[] { "WT_ZL_261031_00001_ZF_01_FZ_01", "WT_ZL_261031_00001_ZF_01_FZ_02" }, numbers);

        // 第一条作废后再生成一条：序号不回收
        var first = await db.paymentShare.AsNoTracking().OrderBy(r => r.id).FirstAsync();
        first.valid = false;
        db.paymentShare.Update(first);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var third = await AddShareAsync(db, introducer.relation_id, 5);
        var records = await Helper(db).CreatePaymentShare(third);
        Assert.Equal("WT_ZL_261031_00001_ZF_01_FZ_03", Assert.Single(records).out_trade_no);
    }

    [Fact]
    public async Task Introducer_without_alipay_account_is_flagged()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 70);
        var introducer = await ShareFixture.AddIntroducerAsync(db);   // 只有微信账号
        var intro = await AddShareAsync(db, introducer.relation_id, 7);

        var record = Assert.Single(await Helper(db).CreatePaymentShare(intro));

        Assert.True(record.can_not_share);
        Assert.Equal("介绍人没有支付宝收款账号", record.memo);
    }

    // 新版后端先于建表上线时，introducer 表还不存在：雪票付款回调和夜间关单都要调 CreatePaymentShare，不能因此报错
    [Fact]
    public async Task A_missing_introducer_table_does_not_break_sharing()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 70);
        var wanlong = await AddShareAsync(db, 4, 35);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE introducer");

        var record = Assert.Single(await Helper(db).CreatePaymentShare(wanlong));

        Assert.Equal(35, record.amount, 2);
        Assert.False(record.can_not_share);
    }

    [Fact]
    public async Task Wanlong_relation_is_not_flagged_for_a_missing_alipay_bind()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 70);
        var wanlong = await AddShareAsync(db, 4, 35);   // relation 4 不在 introducer 表里，没有支付宝绑定记录

        var record = Assert.Single(await Helper(db).CreatePaymentShare(wanlong));

        Assert.False(record.can_not_share);
    }
}

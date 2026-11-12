using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 手动分账（批次记账）：只记账，整批原子，批次号唯一。规格 §7。
public class ShareListManualTests
{
    private static readonly Staff Op = new() { id = 28, name = "苍杰", valid = 1, title_level = 300 };

    private static ShareBatch Batch(string no, DateTime when) => new() { batch_no = no, share_count = 1, total_amount = 1, created_by_staff_id = 28, create_date = when };

    [Fact]
    public async Task Next_batch_no_counts_todays_batches_only()
    {
        await using var db = await ShareFixture.NewDbAsync();
        db.shareBatch.Add(Batch("A", DateTime.Now));
        db.shareBatch.Add(Batch("B", DateTime.Now));
        db.shareBatch.Add(Batch("C", DateTime.Now.AddDays(-1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal("MS-" + DateTime.Today.ToString("yyyyMMdd") + "-03", await new ShareListService(db).NextBatchNoAsync());
    }

    [Fact]
    public async Task Completing_marks_everything_in_one_batch()
    {
        await using var db = await ShareListServiceTests.SeedAsync();
        var rejected = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 104);
        rejected.response_content = "INVALID_REQUEST:分账金额超出最大分账比例";
        db.paymentShare.Update(rejected);
        db.orderShare.Add(new OrderShare { id = 17, order_id = 1, relation_id = 6, amount = 2, auto_share = false,
            share_method = "bank", valid = true, create_date = DateTime.Now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var svc = new ShareListService(db);

        var result = await svc.CompleteManualAsync(Op, new[] { 11, 17 }, "MS-TEST-01");

        Assert.Equal(("MS-TEST-01", 2, 42d), (result.batchNo, result.count, result.total));
        var batch = await db.shareBatch.AsNoTracking().SingleAsync(b => b.batch_no == "MS-TEST-01");
        Assert.Equal((2, 42d, 28), (batch.share_count, batch.total_amount, batch.created_by_staff_id));
        foreach (int id in new[] { 11, 17 })
        {
            var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == id);
            Assert.Equal(batch.id, share.batch_id);
            Assert.False(share.dealed);                       // dealed 的意思是「已生成分账记录」，记账不动它
        }
        var introRecord = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 102);
        Assert.Null(introRecord.success);
        Assert.Null(introRecord.submit_time);
        Assert.Null(introRecord.response_time);
        Assert.Null(introRecord.memo);
        var rejectedAfter = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 104);
        Assert.Null(rejectedAfter.success);
        Assert.Equal("INVALID_REQUEST:分账金额超出最大分账比例", rejectedAfter.response_content);   // 原来的拒绝回执保留
        Assert.Equal(new DateTime(2026, 10, 31, 23, 0, 3), rejectedAfter.submit_time);              // 原来的提交时间也保留

        var page = await svc.ListAsync(ShareListServiceTests.Q());
        foreach (int id in new[] { 11, 17 })
        {
            var item = page.items.Single(i => i.orderShareId == id);
            Assert.Equal(("已分", "银行转账", "MS-TEST-01", false), (item.status, item.mode, item.batch, item.selectable));
        }
        Assert.Equal(0, page.selectableCount);
    }

    [Fact]
    public async Task The_order_page_sees_them_as_shared_and_reevaluation_leaves_them_alone()
    {
        await using var db = await ShareListServiceTests.SeedAsync();
        await new ShareListService(db).CompleteManualAsync(Op, new[] { 11 }, "MS-TEST-02");
        var before = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 102);
        var orderShares = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);

        Assert.Equal("已分账", (await orderShares.ListAsync(1)).Single(v => v.introducerId == 1).state);
        await orderShares.ReevaluateAsync(1);

        var after = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 102);
        Assert.Equal((before.success, before.can_not_share, before.memo), (after.success, after.can_not_share, after.memo));
        // 已提交的不能再改、不能再删
        await Assert.ThrowsAsync<ShareException>(() => orderShares.RemoveIntroducerShareAsync(ShareFixture.Manager(), 11));
    }

    [Theory]
    [InlineData(12)]    // 自动已分
    [InlineData(10)]    // 自动待分
    [InlineData(15)]    // 已记过批次
    [InlineData(16)]    // 已作废
    [InlineData(999)]   // 不存在
    public async Task Whole_batch_is_rejected_when_any_id_is_not_selectable(int badId)
    {
        await using var db = await ShareListServiceTests.SeedAsync();

        var e = await Assert.ThrowsAsync<ShareException>(() => new ShareListService(db).CompleteManualAsync(Op, new[] { 11, badId }, "MS-TEST-03"));

        Assert.Equal(1, e.Code);
        Assert.Equal(1, await db.shareBatch.AsNoTracking().CountAsync());                               // 只有种子里那个批次
        Assert.Null((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == 11)).batch_id);        // 合法的那条也没动
        Assert.Null((await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 102)).success);
    }

    [Fact]
    public async Task The_same_ids_cannot_be_recorded_twice()
    {
        await using var db = await ShareListServiceTests.SeedAsync();
        var svc = new ShareListService(db);
        await svc.CompleteManualAsync(Op, new[] { 11 }, "MS-FIRST");

        await Assert.ThrowsAsync<ShareException>(() => svc.CompleteManualAsync(Op, new[] { 11 }, "MS-SECOND"));

        Assert.Equal(new[] { "MS-20261103-01", "MS-FIRST" }, (await db.shareBatch.AsNoTracking().Select(b => b.batch_no).ToListAsync()).OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData("MS-20261103-01", "批次号已存在")]
    [InlineData(" ms-20261103-01 ", "批次号已存在")]       // 不分大小写、去首尾空格
    [InlineData("", "请填写批次号")]
    [InlineData("   ", "请填写批次号")]
    public async Task Batch_number_must_be_filled_and_unique_ignoring_case_and_spaces(string batchNo, string message)
    {
        await using var db = await ShareListServiceTests.SeedAsync();

        var e = await Assert.ThrowsAsync<ShareException>(() => new ShareListService(db).CompleteManualAsync(Op, new[] { 11 }, batchNo));

        Assert.Equal(message, e.Message);
        Assert.Null((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == 11)).batch_id);
        Assert.Equal(1, await db.shareBatch.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Too_long_batch_number_is_refused()
    {
        await using var db = await ShareListServiceTests.SeedAsync();

        await Assert.ThrowsAsync<ShareException>(() => new ShareListService(db).CompleteManualAsync(Op, new[] { 11 }, new string('A', 41)));
        Assert.Equal(1, await db.shareBatch.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Limits_and_permission()
    {
        await using var db = await ShareListServiceTests.SeedAsync();
        var svc = new ShareListService(db);

        Assert.Equal(1, (await Assert.ThrowsAsync<ShareException>(() => svc.CompleteManualAsync(Op, Array.Empty<int>(), "X1"))).Code);
        Assert.Equal(1, (await Assert.ThrowsAsync<ShareException>(() => svc.CompleteManualAsync(Op, Enumerable.Range(1000, 201).ToArray(), "X2"))).Code);
        var clerk = new Staff { id = 5, name = "店员", valid = 1, title_level = 100 };
        Assert.Equal(3, (await Assert.ThrowsAsync<ShareException>(() => svc.CompleteManualAsync(clerk, new[] { 11 }, "X3"))).Code);
        Assert.Equal(1, await db.shareBatch.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Total_is_rounded_to_cents()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "万龙租赁分账", valid = true });
        foreach (var (id, amount) in new[] { (1, 0.1), (2, 0.2) })
        {
            db.orderShare.Add(new OrderShare { id = id, order_id = 1, relation_id = 4, amount = amount, create_date = new DateTime(2026, 11, 1), valid = true });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new ShareListService(db).CompleteManualAsync(Op, new[] { 1, 2 }, "MS-ROUND");

        Assert.Equal(0.3, result.total);
        Assert.Equal(0.3, (await db.shareBatch.AsNoTracking().SingleAsync()).total_amount);
    }
}

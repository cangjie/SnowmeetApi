using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 手动分账批次：批次号唯一（不分大小写），应分上有可空的 batch_id。
public class ShareListSchemaTests
{
    private static ShareBatch Batch(string no) => new() { batch_no = no, share_count = 1, total_amount = 10, created_by_staff_id = 1, create_date = DateTime.Now };

    [Fact]
    public async Task Batch_numbers_are_unique_ignoring_case()
    {
        await using var db = await ShareFixture.NewDbAsync();
        db.shareBatch.Add(Batch("MS-1"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.shareBatch.Add(Batch("ms-1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_share_has_a_nullable_batch_id()
    {
        await using var db = await ShareFixture.NewDbAsync();
        var batch = Batch("MS-2");
        db.shareBatch.Add(batch);
        db.orderShare.Add(new OrderShare { id = 1, order_id = 1, relation_id = 1, amount = 5, create_date = DateTime.Now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Null((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == 1)).batch_id);

        var row = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == 1);
        row.batch_id = batch.id;
        db.orderShare.Update(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(batch.id, (await db.orderShare.AsNoTracking().SingleAsync(s => s.id == 1)).batch_id);
    }
}

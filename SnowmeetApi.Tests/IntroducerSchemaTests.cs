using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class IntroducerSchemaTests
{
    // 一个会员同一时间只能有一条有效的介绍人记录；停用后可以再加
    [Fact]
    public async Task One_valid_introducer_per_member()
    {
        await using var db = await ShareFixture.NewDbAsync();
        db.introducer.Add(new Introducer { member_id = 5, type = "KOL", name = "A", relation_id = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.introducer.Add(new Introducer { member_id = 5, type = "KOL", name = "B", relation_id = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var first = await db.introducer.SingleAsync(x => x.name == "A");
        first.valid = 0;
        db.introducer.Update(first);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.introducer.Add(new Introducer { member_id = 5, type = "KOL", name = "C", relation_id = 3 });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.introducer.AsNoTracking().CountAsync(x => x.member_id == 5 && x.valid == 1));
    }
}

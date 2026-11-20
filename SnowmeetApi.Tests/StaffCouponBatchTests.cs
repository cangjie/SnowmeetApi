using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;
using Xunit;
using static SnowmeetApi.Tests.TicketMigrationTests;

namespace SnowmeetApi.Tests;

public class StaffCouponBatchTests
{
    private static async Task<int> Create(Fixture f, string mode, int total, string? request = null)
    {
        var result = Result(await new StaffCouponBatchController(f.Db).Create("session-1", new(12, total, mode, request ?? Guid.NewGuid().ToString())));
        Assert.Equal(0, result.code);
        return TicketMigrationTests.Data(result).GetProperty("batchId").GetInt32();
    }

    [Fact]
    public async Task BatchSettingsKeyMustNotBeIdentityGenerated()
    {
        await using var f = await Fixture.Create();
        var property = f.Db.Model.FindEntityType(typeof(SnowmeetApi.Models.StaffCouponBatch))!.FindProperty("batch_id")!;
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, property.ValueGenerated);
    }

    [Fact]
    public async Task StaffCanCreateSingleRecipientBatchClaimAllOnceAndSeeStats()
    {
        await using var f = await Fixture.Create(); await f.AddStaff();
        await f.Db.staff.Where(x => x.id == 9).ExecuteUpdateAsync(s => s.SetProperty(x => x.title_level, 100));
        f.Db.ChangeTracker.Clear();
        int id = await Create(f, "single", 5);
        Assert.Empty(await f.Db.ticket.ToListAsync());
        f.Db.ChangeTracker.Clear();
        var claim = Result(await f.Share().ClaimStaffBatch(id, "session-2"));
        Assert.Equal(0, claim.code); Assert.Equal(5, TicketMigrationTests.Data(claim).GetProperty("count").GetInt32());
        Assert.Equal(5, await f.Db.ticket.CountAsync(t => t.member_id == 2));
        Assert.Equal(5, await f.Db.staffCouponBatchTicket.CountAsync(t => t.batch_id == id));
        Assert.Single(await f.Db.ticketShareClaim.Where(t => t.batch_id == id).ToListAsync());
        Assert.Equal(1, Result(await f.Share().ClaimStaffBatch(id, "session-3")).code);
        Assert.Equal(1, Result(await f.Share().ClaimStaffBatch(id, "session-2")).code);
        f.Db.ChangeTracker.Clear();
        var batches = Result(await new StaffCouponBatchController(f.Db).Batches(12, "session-1"));
        var row = TicketMigrationTests.Data(batches).GetProperty("batches").EnumerateArray().First(x => x.GetProperty("batchId").GetInt32() == id);
        Assert.Equal(5, row.GetProperty("total").GetInt32()); Assert.Equal(5, row.GetProperty("claimed").GetInt32());
        Assert.Equal(1, row.GetProperty("people").GetInt32()); Assert.Equal(0, row.GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task MultipleRecipientsCanClaimOneEachAndNotTwice()
    {
        await using var f = await Fixture.Create(); await f.AddStaff(); int id = await Create(f, "multiple", 2);
        Assert.Equal(0, Result(await f.Share().ClaimStaffBatch(id, "session-2")).code);
        Assert.Equal(1, Result(await f.Share().ClaimStaffBatch(id, "session-2")).code);
        Assert.Equal(0, Result(await f.Share().ClaimStaffBatch(id, "session-3")).code);
        f.Db.ChangeTracker.Clear(); var batch = await f.Db.ticketShareBatch.SingleAsync(b => b.id == id);
        Assert.Equal(2, batch.claim_count); Assert.Equal(2, await f.Db.ticketShareClaim.CountAsync(c => c.batch_id == id));
    }

    [Fact]
    public async Task ConcurrentPeopleCannotBothClaimTheSameSingleRecipientBatch()
    {
        await using var f = await Fixture.Create(); await f.AddStaff(); int id = await Create(f, "single", 4);
        async Task<int> Claim(string session) { await using var db = f.NewContext(); return Result(await new TicketShareController(db, f.Config).ClaimStaffBatch(id, session)).code; }
        var codes = await Task.WhenAll(Claim("session-2"), Claim("session-3"));
        Assert.Equal(1, codes.Count(c => c == 0)); Assert.Equal(4, await f.Db.staffCouponBatchTicket.CountAsync(c => c.batch_id == id));
    }

    [Fact]
    public async Task CreateRetryIsIdempotentAndOtherStaffCannotRevoke()
    {
        await using var f = await Fixture.Create(); await f.AddStaff(); string request = Guid.NewGuid().ToString();
        int id = await Create(f, "multiple", 8, request); f.Db.ChangeTracker.Clear();
        Assert.Equal(id, await Create(f, "multiple", 8, request));
        f.Db.ChangeTracker.Clear(); await f.AddStaff(2, 19);
        var controller = new StaffCouponBatchController(f.Db);
        Assert.Equal(1, Result(await controller.Revoke(id, "session-2")).code);
        f.Db.ChangeTracker.Clear(); Assert.Equal(0, Result(await controller.Revoke(id, "session-1")).code);
        Assert.Equal(1, Result(await f.Share().ClaimStaffBatch(id, "session-3")).code);
    }

    [Fact]
    public async Task FailedIssueRollsBackEveryCouponAndDoesNotConsumeBatch()
    {
        await using var f = await Fixture.Create(); await f.AddStaff(); int id = await Create(f, "single", 4);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_staff_coupon BEFORE INSERT ON staff_coupon_batch_ticket BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Share().ClaimStaffBatch(id, "session-2"));
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.ticket.ToListAsync());
        Assert.Empty(await f.Db.ticketShareClaim.Where(c => c.batch_id == id).ToListAsync());
        Assert.Equal(0, (await f.Db.ticketShareBatch.SingleAsync(b => b.id == id)).claim_count);
    }

    [Fact]
    public async Task NonstaffAndClosedTemplatesCannotCreateAndLegacyRulesRemain()
    {
        await using var f = await Fixture.Create(); await f.AddStaff();
        var admin = new StaffCouponBatchController(f.Db);
        Assert.Equal(1, Result(await admin.Create("session-2", new(12, 2, "multiple", Guid.NewGuid().ToString()))).code);
        await f.Db.ticketTemplate.Where(t => t.id == 12).ExecuteUpdateAsync(s => s.SetProperty(t => t.marketing_promotion, 0));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, Result(await admin.Create("session-1", new(12, 2, "multiple", Guid.NewGuid().ToString()))).code);
        Assert.True(MiniTicketRules.IsMini(MiniTicketRules.Group));
    }

    [Fact]
    public async Task OnlyOwnerCanDeleteRevokedUnclaimedBatchWithoutReactivatingItsLink()
    {
        await using var f = await Fixture.Create(); await f.AddStaff();
        string request = Guid.NewGuid().ToString(); int id = await Create(f, "multiple", 2, request);
        var controller = new StaffCouponBatchController(f.Db);
        Assert.Equal(1, Result(await controller.Delete(id, "session-1")).code);
        Assert.Equal(0, Result(await controller.Revoke(id, "session-1")).code);
        await f.AddStaff(2, 19);
        Assert.Equal(1, Result(await controller.Delete(id, "session-2")).code);
        var row = TicketMigrationTests.Data(Result(await controller.Batches(12, "session-1"))).GetProperty("batches")
            .EnumerateArray().First(x => x.GetProperty("batchId").GetInt32() == id);
        Assert.True(row.GetProperty("canDelete").GetBoolean());
        Assert.Equal(0, Result(await controller.Delete(id, "session-1")).code);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(TicketShareBatch.Deleted, (await f.Db.ticketShareBatch.SingleAsync(b => b.id == id)).valid);
        Assert.True(await f.Db.staffCouponBatch.AnyAsync(b => b.batch_id == id));
        Assert.True(await f.Db.coreDataModLog.AnyAsync(l => l.table_name == "ticket_share_batch" && l.key_value == id));
        Assert.DoesNotContain(TicketMigrationTests.Data(Result(await controller.Batches(12, "session-1"))).GetProperty("batches").EnumerateArray(), x => x.GetProperty("batchId").GetInt32() == id);
        Assert.Equal(1, Result(await f.Share().ClaimStaffBatch(id, "session-3")).code);
        Assert.Equal(1, Result(await controller.Revoke(id, "session-1")).code);
        Assert.Equal(1, Result(await controller.Create("session-1", new(12, 2, "multiple", request))).code);
    }

    [Fact]
    public async Task ClaimedBatchCannotBeDeletedEvenAfterRevokingOrInconsistentCounter()
    {
        await using var f = await Fixture.Create(); await f.AddStaff(); int id = await Create(f, "multiple", 2);
        Assert.Equal(0, Result(await f.Share().ClaimStaffBatch(id, "session-2")).code);
        var controller = new StaffCouponBatchController(f.Db);
        Assert.Equal(0, Result(await controller.Revoke(id, "session-1")).code);
        await f.Db.ticketShareBatch.Where(b => b.id == id).ExecuteUpdateAsync(s => s.SetProperty(b => b.claim_count, 0));
        f.Db.ChangeTracker.Clear();
        var row = TicketMigrationTests.Data(Result(await controller.Batches(12, "session-1"))).GetProperty("batches")
            .EnumerateArray().First(x => x.GetProperty("batchId").GetInt32() == id);
        Assert.False(row.GetProperty("canDelete").GetBoolean());
        Assert.Equal(1, Result(await controller.Delete(id, "session-1")).code);
        Assert.Equal(1, await f.Db.ticket.CountAsync());
    }

    [Theory]
    [InlineData("qrcode")]
    [InlineData("mini_q")]
    public async Task DeletedOldFixedQrDoesNotGetRevivedWhenRecreated(string type)
    {
        await using var f = await Fixture.Create(type); await f.AddStaff();
        await f.Db.ticketShareBatch.Where(b => b.id == 1).ExecuteUpdateAsync(s => s.SetProperty(b => b.valid, 0));
        Assert.Equal(0, Result(await new StaffCouponBatchController(f.Db).Delete(1, "session-1")).code);
        f.Db.ChangeTracker.Clear();
        var admin = new TicketTemplateAdminController(f.Db, f.Config);
        var result = type == "qrcode" ? Result(await admin.CreateQrCodeBatchByStaff(12, "门店", "session-1"))
            : Result(await admin.CreateMiniQrCodeBatchByStaff(12, "门店", "session-1"));
        Assert.Equal(0, result.code);
        Assert.NotEqual(1, TicketMigrationTests.Data(result).GetProperty("batchId").GetInt32());
        Assert.Equal(TicketShareBatch.Deleted, (await f.Db.ticketShareBatch.AsNoTracking().SingleAsync(b => b.id == 1)).valid);
    }

    [Fact]
    public async Task StaleClaimCannotRestoreDeletedBatch()
    {
        await using var f = await Fixture.Create();
        await using var stale = f.NewContext();
        var batch = await stale.ticketShareBatch.AsTracking().SingleAsync(b => b.id == 1);
        await f.Db.ticketShareBatch.Where(b => b.id == 1).ExecuteUpdateAsync(s => s.SetProperty(b => b.valid, TicketShareBatch.Deleted));
        batch.claim_count++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }
}

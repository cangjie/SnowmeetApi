using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using Xunit;
using static SnowmeetApi.Tests.TicketMigrationTests;

namespace SnowmeetApi.Tests;

public class TicketMarketingPromotionTests
{
    [Fact]
    public async Task PromotionWorksWhenCustomerTransfersAreDisabled()
    {
        await using var f = await Fixture.Create(); await f.AddStaff();
        await f.Db.ticketTemplate.Where(t => t.id == 12).ExecuteUpdateAsync(s => s.SetProperty(t => t.sharable, 0));
        var staff = new StaffCouponBatchController(f.Db);
        Assert.Single(TicketMigrationTests.Data(Result(await staff.Templates("session-1"))).GetProperty("templates").EnumerateArray());
        var batch = Result(await staff.Create("session-1", new(12, 2, "multiple", Guid.NewGuid().ToString())));
        Assert.Equal(0, batch.code);
        int id = TicketMigrationTests.Data(batch).GetProperty("batchId").GetInt32();
        Assert.Equal(0, Result(await f.Share().ClaimStaffBatch(id, "session-2")).code);
        Assert.Equal(0, Result(await new TicketTemplateAdminController(f.Db, f.Config).ShareMiniTicketTemplateByStaff(12, "group", "session-1")).code);
        f.Db.ticket.Add(f.Ticket()); await f.Db.SaveChangesAsync();
        Assert.Equal(1, Result(await f.Transfer().SetTicketToMiniShare("111111111", "session-1")).code);
    }

    [Fact]
    public async Task TurningPromotionOffBlocksNewSharingButNotClaimsOrCustomerTransfer()
    {
        await using var f = await Fixture.Create(); await f.AddStaff();
        var staff = new StaffCouponBatchController(f.Db);
        var created = Result(await staff.Create("session-1", new(12, 2, "multiple", Guid.NewGuid().ToString())));
        int id = TicketMigrationTests.Data(created).GetProperty("batchId").GetInt32();
        await f.Db.ticketTemplate.Where(t => t.id == 12).ExecuteUpdateAsync(s => s.SetProperty(t => t.marketing_promotion, 0));
        Assert.Empty(TicketMigrationTests.Data(Result(await staff.Templates("session-1"))).GetProperty("templates").EnumerateArray());
        Assert.Equal(1, Result(await staff.Create("session-1", new(12, 2, "multiple", Guid.NewGuid().ToString()))).code);
        Assert.Equal(1, Result(await staff.Batches(12, "session-1")).code);
        var admin = new TicketTemplateAdminController(f.Db, f.Config);
        Assert.Equal(1, Result(await admin.ShareMiniTicketTemplateByStaff(12, "group", "session-1")).code);
        Assert.Equal(1, Result(await admin.CreateMiniQrCodeBatchByStaff(12, "门店", "session-1")).code);
        var qr = new MiniTicketQrTests.Qr();
        Assert.IsType<BadRequestObjectResult>(await new TicketPosterController(f.Db, f.Config).GetMiniQrCode(id, "session-1", qr));
        Assert.Equal(0, qr.Calls);
        Assert.Equal(0, Result(await f.Share().ClaimStaffBatch(id, "session-2")).code);
        Assert.Equal(0, Result(await f.Share().ClaimMiniSharedTicket(1, "session-3")).code);
        f.Db.ticket.Add(f.Ticket()); await f.Db.SaveChangesAsync();
        Assert.Equal(0, Result(await f.Transfer().SetTicketToMiniShare("111111111", "session-1")).code);
    }

    [Fact]
    public async Task OldClientSavingTemplateWithoutNewFieldPreservesExistingSetting()
    {
        await using var f = await Fixture.Create(); await f.AddStaff();
        var admin = new TicketTemplateAdminController(f.Db, f.Config);
        var req = new TicketTemplateAdminController.SaveTemplateRequest {
            id = 12, name = "测试券", type = "养护券", bizType = "养护", validityMode = "forever", sharable = 1
        };
        Assert.Equal(0, Result(await admin.SaveTemplateByStaff(req, "session-1")).code);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, (await f.Db.ticketTemplate.SingleAsync(t => t.id == 12)).marketing_promotion);
        req.marketingPromotion = 0;
        Assert.Equal(0, Result(await admin.SaveTemplateByStaff(req, "session-1")).code);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await f.Db.ticketTemplate.SingleAsync(t => t.id == 12)).marketing_promotion);
        req.marketingPromotion = null;
        Assert.Equal(0, Result(await admin.SaveTemplateByStaff(req, "session-1")).code);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await f.Db.ticketTemplate.SingleAsync(t => t.id == 12)).marketing_promotion);
    }
}

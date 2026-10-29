using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public sealed class TicketTransferRewardTests
{
    [Theory]
    [InlineData(12, 0)]
    [InlineData(16, 1)]
    public async Task MiniTransferRewardsOnlyOldCustomerCoupon(int templateId, int expectedRewards)
    {
        await using var f = await TicketMigrationTests.Fixture.Create();
        if (templateId == 16)
            f.Db.ticketTemplate.Add(new TicketTemplate { id=16, name="老顾客优惠券", memo="说明", sharable=1, valid=1 });
        var ticket = f.Ticket(); ticket.template_id = templateId;
        f.Db.ticket.Add(ticket); await f.Db.SaveChangesAsync();
        var controller = f.Transfer();
        Assert.Equal(0, TicketMigrationTests.Result(await controller.SetTicketToMiniShare(ticket.code, "session-1")).code);
        Assert.Equal(0, TicketMigrationTests.Result(await f.Transfer().AcceptMiniTicket(ticket.code, "", "session-2")).code);
        Assert.Equal(2, (await f.Db.ticket.SingleAsync(t => t.code == ticket.code)).member_id);
        var rewards = await f.Db.ticket.Where(t => t.create_memo == "转赠被领取回赠").ToListAsync();
        Assert.Equal(expectedRewards, rewards.Count);
        if (expectedRewards == 1) {
            Assert.Equal(1, rewards[0].member_id);
            Assert.Equal(16, rewards[0].template_id);
        }
        Assert.Equal(1, TicketMigrationTests.Result(await f.Transfer().AcceptMiniTicket(ticket.code, "", "session-3")).code);
        Assert.Equal(expectedRewards, await f.Db.ticket.CountAsync(t => t.create_memo == "转赠被领取回赠"));
    }

    [Fact]
    public async Task LegacyRewardDoesNotIssueFreeWaxCoupon()
    {
        await using var f = await TicketMigrationTests.Fixture.Create();
        var sender = await new MemberController(f.Db, f.Config).GetWholeMemberById(1);
        await f.Transfer().RewardSenderForAcceptedTransfer(f.Ticket(), sender);
        Assert.Empty(await f.Db.ticket.ToListAsync());
    }
}

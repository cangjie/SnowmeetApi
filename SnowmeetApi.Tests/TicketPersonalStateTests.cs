using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests;

public sealed class TicketPersonalStateTests
{
    private static Ticket Ticket(string code, int member = 3, Action<Ticket>? change = null)
    {
        var ticket = new Ticket { code = code, name = "测试券", memo = "", open_id = "mini-" + member,
            member_id = member, valid = 1, used = 0, shared = 0, is_active = 1, template_id = 12,
            channel = "", create_date = DateTime.Now };
        change?.Invoke(ticket);
        return ticket;
    }

    private static async Task<ApiResult<List<Ticket>>> Get(CoachFixture f, string state, string session = "session-3")
    {
        var result = await new TicketController(f.NewContext(), f.Config).GetMyTicketsByState(state, session);
        return Assert.IsType<ApiResult<List<Ticket>>>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task UnusedTicketsArePartitionedByExpiryAndActivationWithoutLeakingSharedOrOtherMembers()
    {
        await using var f = await CoachFixture.Create();
        var today = DateTime.Now.Date;
        f.Db.ticket.AddRange(
            Ticket("available"),
            Ticket("today", change: t => { t.start_date = today.AddHours(23); t.expire_date = today; }),
            Ticket("inactive", change: t => t.is_active = 0),
            Ticket("future", change: t => t.start_date = today.AddDays(1)),
            Ticket("both", change: t => { t.is_active = 0; t.start_date = today.AddDays(1); }),
            Ticket("expired", change: t => t.expire_date = today.AddDays(-1)),
            Ticket("expired-inactive", change: t => { t.is_active = 0; t.expire_date = today.AddDays(-1); }),
            Ticket("shared", change: t => t.shared = 1),
            Ticket("used", change: t => t.used = 1),
            Ticket("invalid", change: t => t.valid = 0),
            Ticket("other", 4));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var available = (await Get(f, "available")).data!;
        var inactive = (await Get(f, "inactive")).data!;
        var expired = (await Get(f, "expired")).data!;
        Assert.Equal(new[] { "available", "today" }, available.Select(t => t.code).Order().ToArray());
        Assert.Equal(new[] { "both", "future", "inactive" }, inactive.Select(t => t.code).Order().ToArray());
        Assert.Equal(new[] { "expired", "expired-inactive" }, expired.Select(t => t.code).Order().ToArray());
        Assert.All(expired, t => Assert.True(t.expired));
        Assert.All(available.Concat(inactive), t => Assert.False(t.expired));

        // 老接口未改变：仍返回未过期的未核销券，包括未激活和分享中的券。
        var old = await new TicketController(f.NewContext(), f.Config).GetMyTickets(0, "session-3");
        var oldTickets = Assert.IsType<ApiResult<List<Ticket>>>(Assert.IsType<OkObjectResult>(old.Result).Value).data!;
        Assert.Contains(oldTickets, t => t.code == "inactive");
        Assert.Contains(oldTickets, t => t.code == "shared");
        Assert.DoesNotContain(oldTickets, t => t.code == "expired");
    }

    [Fact]
    public async Task InvalidStateAndMissingSessionDoNotReturnTickets()
    {
        await using var f = await CoachFixture.Create();
        Assert.Equal(1, (await Get(f, "other")).code);
        Assert.Null((await Get(f, "available", "no-such-session")).data);
    }
}

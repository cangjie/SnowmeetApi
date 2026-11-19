using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests;

// 「我的优惠券」新增「已接受」：列出我通过别人分享（真转赠）收到的券（2026-11-19 用户要求）。
// 范围：最新一次转赠的接受人是我、现在还在我名下的券，不论已使用、已过期；还挂着「分享中」的不算（它们在「已分享」里）。
public sealed class TicketAcceptedListTests
{
    private const string Me = "mini-3", Other = "mini-4";

    private static TicketLog Log(string code, string sender, string accepter, DateTime at, string memo = "分享获得")
        => new() { code = code, sender_open_id = sender, accepter_open_id = accepter, memo = memo, transact_time = at };

    // ---------- 规则 ----------

    [Fact]
    public void Accepted_list_shows_the_accept_time_with_the_label_领取时间()
    {
        var accepted = new DateTime(2026, 11, 10, 10, 0, 0);
        var ticket = new Ticket { code = "x", create_date = new DateTime(2026, 3, 1), used_time = new DateTime(2026, 11, 11), shared_time = new DateTime(2026, 11, 12) };
        var shown = TicketTransferRules.ResolveDisplayTime(ticket, TicketListContext.Accepted, accepted);
        Assert.Equal(accepted, shown.Time);
        Assert.Equal("领取时间", shown.Label);
        Assert.Equal("2026-11-10 10:00", shown.Text);
    }

    [Fact]
    public void ReceivedCodes_only_counts_real_transfers_whose_latest_receiver_is_me()
    {
        var t = new DateTime(2026, 11, 1, 10, 0, 0);
        var logs = new List<TicketLog>
        {
            Log("A", Other, Me, t),                                                            // 别人转给我：算
            Log("B", Other, Me, t), Log("B", Me, Other, t.AddHours(1)),                        // 我收到后又转出去，最新接受人是对方：不算
            Log("C", Me, Other, t), Log("C", Other, Me, t.AddHours(1)),                        // 送出去又被送回来，最新接受人是我：算
            Log("D", "", "mini-7", t, "核销"),                                                 // 核销日志不是转赠
            Log("E", "mini-7", Me, t, "养护订单获得,ID:1"),                                    // 店员开单发券不是转赠
            Log("F", "mini-7", Me, t, "体验订单获得,ID:2"),
            Log("G", "mini-7", Me, t, "管理员赠送"),
            Log("H", Me, "", t, "撤回分享"),                                                   // 撤回分享不是转赠
            Log("I", Other, "mini-5", t),                                                      // 别人之间的转赠
        };
        Assert.Equal(new[] { "A", "C" }, TicketTransferRules.ReceivedCodes(logs, Me).OrderBy(c => c).ToArray());
        Assert.Empty(TicketTransferRules.ReceivedCodes(logs, ""));
    }

    // ---------- 接口 ----------

    private static Ticket NewTicket(string code, int memberId, Action<Ticket>? tweak = null)
    {
        var t = new Ticket { code = code, name = "免费打蜡券", memo = "", open_id = "mini-" + memberId, member_id = memberId, valid = 1, used = 0, shared = 0,
            template_id = 12, channel = "", create_date = new DateTime(2026, 3, 1, 9, 0, 0), expire_date = null };
        tweak?.Invoke(t);
        return t;
    }

    private static async Task<List<Ticket>> Accepted(CoachFixture f, string sessionKey = "session-3")
    {
        var result = await new TicketController(f.NewContext(), f.Config).GetMyAcceptedTickets(sessionKey);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var api = Assert.IsType<ApiResult<List<Ticket>>>(ok.Value);
        return api.data ?? new List<Ticket>();
    }

    [Fact]
    public async Task The_endpoint_lists_tickets_I_received_including_used_and_expired_newest_first()
    {
        await using var f = await CoachFixture.Create();
        var yesterday = DateTime.Now.Date.AddDays(-1);
        f.Db.ticket.AddRange(
            NewTicket("T1", 3),                                              // 收到，未使用
            NewTicket("T2", 3, t => { t.used = 1; t.used_time = new DateTime(2026, 11, 6); }),   // 收到，已使用
            NewTicket("T3", 3),                                              // 自己得到的（养护订单获得）：不算
            NewTicket("T4", 3, t => { t.shared = 1; t.shared_time = DateTime.Now; }),            // 收到后又挂出去分享中：在「已分享」里，不算
            NewTicket("T5", 4),                                              // 收到后转给了别人（现在是 4 的）：不算
            NewTicket("T6", 3, t => t.expire_date = yesterday),              // 收到，已过期
            NewTicket("T7", 3, t => t.valid = 0),                            // 作废的券：不算
            NewTicket("T8", 5));                                             // 别人的券
        f.Db.ticketLog.AddRange(
            Log("T1", Other, Me, new DateTime(2026, 11, 10, 10, 0, 0)),
            Log("T2", Other, Me, new DateTime(2026, 11, 5, 8, 30, 0)),
            Log("T3", "mini-7", Me, new DateTime(2026, 11, 9), "养护订单获得,ID:9"),
            Log("T4", Other, Me, new DateTime(2026, 11, 8)),
            Log("T5", Other, Me, new DateTime(2026, 11, 1)), Log("T5", Me, Other, new DateTime(2026, 11, 2)),
            Log("T6", Other, Me, new DateTime(2026, 11, 12, 18, 0, 0)),
            Log("T7", Other, Me, new DateTime(2026, 11, 7)),
            Log("T8", Other, "mini-5", new DateTime(2026, 11, 7)));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var list = await Accepted(f);
        Assert.Equal(new[] { "T6", "T1", "T2" }, list.Select(t => t.code).ToArray());         // 按领取时间倒序
        Assert.All(list, t => Assert.Equal("领取时间", t.displayTimeLabel));
        Assert.Equal("2026-11-10 10:00", list.Single(t => t.code == "T1").displayTimeText);
        Assert.False(list.Single(t => t.code == "T1").transferredOut);
        Assert.Equal(1, list.Single(t => t.code == "T2").used);
        Assert.True(list.Single(t => t.code == "T6").expired);
        Assert.False(list.Single(t => t.code == "T1").expired);
        Assert.All(list, t => Assert.False(string.IsNullOrEmpty(t.expireText)));
    }

    [Fact]
    public async Task Someone_who_received_nothing_gets_an_empty_list()
    {
        await using var f = await CoachFixture.Create();
        f.Db.ticket.Add(NewTicket("T1", 3));
        f.Db.ticketLog.Add(Log("T1", Other, Me, new DateTime(2026, 11, 10)));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await Accepted(f, "session-5"));       // 会员 5 什么都没收到
    }

    [Fact]
    public async Task Without_a_member_session_it_answers_not_logged_in_like_the_other_list_endpoints()
    {
        await using var f = await CoachFixture.Create();
        var result = await new TicketController(f.NewContext(), f.Config).GetMyAcceptedTickets("no-such-session");
        var api = Assert.IsType<ApiResult<List<Ticket>>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(0, api.code);
        Assert.Null(api.data);
        Assert.Contains("未登录", api.message);
    }

    [Fact]
    public async Task The_expired_flag_is_filled_for_every_ticket_card()
    {
        var now = new DateTime(2026, 11, 19, 10, 0, 0);
        var past = NewTicket("a", 3, t => t.expire_date = new DateTime(2026, 11, 18));
        var today = NewTicket("b", 3, t => t.expire_date = new DateTime(2026, 11, 19));      // 当天到期的当天仍可用
        var forever = NewTicket("c", 3);
        foreach (var t in new[] { past, today, forever }) TicketController.FillIntrinsicFields(t, now);
        Assert.True(past.expired);
        Assert.False(today.expired);
        Assert.False(forever.expired);
        await Task.CompletedTask;
    }
}

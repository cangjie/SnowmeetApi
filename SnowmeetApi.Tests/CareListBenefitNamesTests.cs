using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护订单列表里，装备块要列出这件装备用的所有权益——次卡（见 CareListCardInfoTests）、券、学员卡、质保 / 招待。
/// 这里管券名和学员卡名（Care.ticketDisplayName / coachCardDisplayName，只用于展示，不改库）：
///   · 选了券（ticket_code）→ 券的名字，如「老顾客优惠券」；券找不到 / 没名字 → null（小程序显示「优惠券」，券号照常显示）；
///   · 选了学员卡（coach_card_id）→「某某教练的学员卡」（与开单页同一叫法）；教练找不到 → null（小程序显示「学员卡」）；
///   · 没选就都没有；读不出来只是这件装备没有名字，不能把整个列表弄坏；
///   · 质保 / 招待是装备自带的标记（warranty / entertain），列表接口照常返回。
/// </summary>
public class CareListBenefitNamesTests
{
    private const string Wanlong = "万龙服务中心";
    private static readonly DateTime Now = DateTime.Now;

    private static async Task<int> AddOrder(CoachFixture f, params Care[] cares)
    {
        var o = new Order { type = "养护", shop = Wanlong, valid = 1, is_test = 0, member_id = CoachFixture.StudentA, biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
        foreach (var c in cares) { c.valid = 1; c.create_date = Now.AddHours(-1); if (string.IsNullOrEmpty(c.equipment)) c.equipment = "双板"; o.cares.Add(c); }
        f.Db.order.Add(o);
        await f.Db.SaveChangesAsync();
        return o.id;
    }

    private static Care PlainCare(Action<Care>? tweak = null)
    {
        var c = new Care { equipment = "双板", need_edge = 1, need_wax = 1, biz_type = "雪季养护", common_charge = 230 };
        tweak?.Invoke(c); return c;
    }

    private static async Task<List<Care>> ListCares(CoachFixture f, int orderId)
    {
        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, Now.AddDays(-30).Date, Now.Date, "session-7", null, pageIndex: 1, pageSize: 50);
        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items.Single(o => o.id == orderId).cares.OrderBy(c => c.id).ToList();
    }

    private static async Task AddTicket(CoachFixture f, string code, string name)
    {
        f.Db.ticket.Add(new Ticket { code = code, name = name, template_id = 16, member_id = CoachFixture.StudentA, open_id = "student-openid", valid = 1, is_active = 1 });
        await f.Db.SaveChangesAsync();
    }

    private static async Task<int> AddCoachCard(CoachFixture f, int coachId, string token)
    {
        var card = new CoachCard { coach_id = coachId, token = token, status = CoachCard.StatusClaimed, member_id = CoachFixture.StudentA };
        f.Db.coachCard.Add(card);
        await f.Db.SaveChangesAsync();
        return card.id;
    }

    // ───────────── 纯规则 ─────────────

    [Fact]
    public void Rule_TicketName_NeedsATicketCodeAndAName()
    {
        Assert.Equal("老顾客优惠券", CareCardInfoRules.TicketName(new Care { ticket_code = "997053438" }, " 老顾客优惠券 "));
        Assert.Null(CareCardInfoRules.TicketName(new Care { ticket_code = "997053438" }, null));
        Assert.Null(CareCardInfoRules.TicketName(new Care { ticket_code = "997053438" }, "  "));
        Assert.Null(CareCardInfoRules.TicketName(new Care { ticket_code = null }, "老顾客优惠券"));
        Assert.Null(CareCardInfoRules.TicketName(new Care { ticket_code = "  " }, "老顾客优惠券"));
    }

    [Fact]
    public void Rule_CoachCardName_IsTheCoachsStudentCard_AndNeedsACoachCardAndACoachName()
    {
        Assert.Equal("张教练的学员卡", CareCardInfoRules.CoachCardName(new Care { coach_card_id = 5 }, "张教练"));
        Assert.Null(CareCardInfoRules.CoachCardName(new Care { coach_card_id = 5 }, null));
        Assert.Null(CareCardInfoRules.CoachCardName(new Care { coach_card_id = 5 }, " "));
        Assert.Null(CareCardInfoRules.CoachCardName(new Care { coach_card_id = null }, "张教练"));
    }

    // ───────────── 列表接口 ─────────────

    [Fact]
    public async Task TicketCare_ShowsTheTicketName_AndKeepsTheTicketCode()
    {
        await using var f = await CoachFixture.Create();
        await AddTicket(f, "997053438", "老顾客优惠券");
        int orderId = await AddOrder(f, PlainCare(c => { c.ticket_code = "997053438"; c.discount = 230; }));
        var care = Assert.Single(await ListCares(f, orderId));
        Assert.Equal("老顾客优惠券", care.ticketDisplayName);
        Assert.Equal("997053438", care.ticket_code);
        Assert.Equal(230.0, care.displayDiscount);   // 金额 / 减免照常
    }

    [Fact]
    public async Task TicketNotFound_HasNoName_ButStillHasTheCode_AndTwoCaresEachGetTheirOwnTicket()
    {
        await using var f = await CoachFixture.Create();
        await AddTicket(f, "T-OLD", "老顾客优惠券");
        await AddTicket(f, "T-SCRAPE", "开板刮蜡券");
        int orderId = await AddOrder(f,
            PlainCare(c => c.ticket_code = "T-OLD"),
            PlainCare(c => c.ticket_code = "T-SCRAPE"),
            PlainCare(c => c.ticket_code = "T-GONE"),
            PlainCare(c => c.ticket_code = null),
            PlainCare(c => c.ticket_code = ""));
        var cares = await ListCares(f, orderId);
        Assert.Equal(new string?[] { "老顾客优惠券", "开板刮蜡券", null, null, null }, cares.Select(c => c.ticketDisplayName).ToArray());
        Assert.Equal("T-GONE", cares[2].ticket_code);
    }

    [Fact]
    public async Task CoachCardCare_ShowsTheCoachsStudentCardName()
    {
        await using var f = await CoachFixture.Create();
        int card1 = await AddCoachCard(f, f.CoachId, "tokentokentokentokentok1");
        int card2 = await AddCoachCard(f, f.Coach2Id, "tokentokentokentokentok2");
        int cardNoCoach = await AddCoachCard(f, 9999, "tokentokentokentokentok3");
        int orderId = await AddOrder(f,
            PlainCare(c => c.coach_card_id = card1),
            PlainCare(c => c.coach_card_id = card2),
            PlainCare(c => c.coach_card_id = cardNoCoach),
            PlainCare(c => c.coach_card_id = null));
        var cares = await ListCares(f, orderId);
        Assert.Equal(new string?[] { "张教练的学员卡", "李教练的学员卡", null, null }, cares.Select(c => c.coachCardDisplayName).ToArray());
        Assert.Equal(card1, cares[0].coach_card_id);   // 卡号 = 学员卡 id，随装备返回
    }

    [Fact]
    public async Task WarrantyAndEntertainFlags_AreReturnedWithTheCare()
    {
        await using var f = await CoachFixture.Create();
        int orderId = await AddOrder(f, PlainCare(c => { c.warranty = true; c.common_charge = 0; }), PlainCare(c => { c.entertain = true; c.common_charge = 0; }), PlainCare());
        var cares = await ListCares(f, orderId);
        Assert.Equal(new[] { (true, false), (false, true), (false, false) }, cares.Select(c => (c.warranty, c.entertain)).ToArray());
    }

    [Fact]
    public async Task WhenTheCoachCardsCannotBeRead_TheListStillWorks_AndTheTicketNameIsStillThere()
    {
        await using var f = await CoachFixture.Create();
        await AddTicket(f, "T-OLD", "老顾客优惠券");
        int card = await AddCoachCard(f, f.CoachId, "tokentokentokentokentok1");
        int orderId = await AddOrder(f, PlainCare(c => c.ticket_code = "T-OLD"), PlainCare(c => c.coach_card_id = card));
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE coach_card");
        var cares = await ListCares(f, orderId);
        Assert.Equal("老顾客优惠券", cares[0].ticketDisplayName);
        Assert.Null(cares[1].coachCardDisplayName);
        Assert.Equal(card, cares[1].coach_card_id);
    }

    [Fact]
    public async Task ListingDoesNotChangeTheTicket()
    {
        await using var f = await CoachFixture.Create();
        await AddTicket(f, "T-OLD", "老顾客优惠券");
        int orderId = await AddOrder(f, PlainCare(c => c.ticket_code = "T-OLD"));
        await ListCares(f, orderId);
        var stored = await f.NewContext().ticket.AsNoTracking().SingleAsync(t => t.code == "T-OLD");
        Assert.Equal((0, "老顾客优惠券"), (stored.used, stored.name));
    }
}

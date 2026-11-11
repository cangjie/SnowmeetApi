using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护订单列表里，用次卡的装备要能看出「用的是哪张次卡、还剩几次」（Care.cardDisplayName / cardRemaining / cardUnlimited，只用于展示，不改库）：
///   · 次卡装备（use_card）：库里项目收费、减免本来就是 0（卡抵掉了），金额 / 减免照常显示库里的值，不补价；
///   · 卡名取次卡本身的名字，次卡找不到时退回装备上记的 card_name；
///   · 剩余次数 = 总次数 - 已用次数（现在这一刻的余量，不为负）；季卡（总次数为空）不限次，标 cardUnlimited；
///   · 没用次卡的装备、还没绑上卡的装备（购卡处理中 card_id 为空）都没有剩余次数；
///   · 读卡失败只是这页没有这些展示信息，不能把整个列表弄坏。
/// </summary>
public class CareListCardInfoTests
{
    private const string Wanlong = "万龙服务中心";
    private static readonly DateTime Now = DateTime.Now;

    private static async Task<int> AddCard(CoachFixture f, int? total, int? punches, string name = "孙伟博学员专用")
    {
        var card = new PunchCard { biz_type = "养护", card_name = name, member_id = CoachFixture.StudentA, total = total, punches = punches, care_project_count = 2 };
        f.Db.punchCard.Add(card);
        await f.Db.SaveChangesAsync();
        return card.id;
    }

    private static async Task<int> AddOrder(CoachFixture f, params Care[] cares)
    {
        var o = new Order { type = "养护", shop = Wanlong, valid = 1, is_test = 0, member_id = CoachFixture.StudentA, biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
        foreach (var c in cares) { c.valid = 1; c.create_date = Now.AddHours(-1); if (string.IsNullOrEmpty(c.equipment)) c.equipment = "双板"; o.cares.Add(c); }
        f.Db.order.Add(o);
        await f.Db.SaveChangesAsync();
        return o.id;
    }

    private static Care CardCare(int? cardId, string? cardName = "孙伟博学员专用", Action<Care>? tweak = null)
    {
        var c = new Care { equipment = "双板", need_edge = 1, need_wax = 1, biz_type = "雪季养护", use_card = true, card_id = cardId, card_name = cardName };
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

    // ───────────── 纯规则 ─────────────

    [Fact]
    public void Rule_RemainingIsTotalMinusPunches_NeverNegative_SeasonCardIsUnlimited()
    {
        var care = new Care { use_card = true, card_name = "装备上记的卡名" };
        Assert.Equal(("次卡本名", 5, false), CareCardInfoRules.Build(care, new PunchCard { card_name = "次卡本名", total = 10, punches = 5 }));
        Assert.Equal(("次卡本名", 10, false), CareCardInfoRules.Build(care, new PunchCard { card_name = "次卡本名", total = 10, punches = null }));   // 已用为空按 0
        Assert.Equal(("次卡本名", 0, false), CareCardInfoRules.Build(care, new PunchCard { card_name = "次卡本名", total = 3, punches = 5 }));      // 超扣不显示负数
        Assert.Equal(("次卡本名", null, true), CareCardInfoRules.Build(care, new PunchCard { card_name = "次卡本名", total = null, punches = 7 })); // 季卡
    }

    [Fact]
    public void Rule_NameFallsBackToTheNameKeptOnTheCare_AndNoCardMeansNoRemaining()
    {
        var care = new Care { use_card = true, card_name = "装备上记的卡名" };
        Assert.Equal(("装备上记的卡名", 4, false), CareCardInfoRules.Build(care, new PunchCard { card_name = "", total = 5, punches = 1 }));
        Assert.Equal(("装备上记的卡名", null, false), CareCardInfoRules.Build(care, null));                                        // 卡找不到 / 还没绑卡
        Assert.Equal((null, null, false), CareCardInfoRules.Build(new Care { use_card = true }, null));
        Assert.Equal((null, null, false), CareCardInfoRules.Build(new Care { use_card = false, card_name = "不该显示" }, new PunchCard { total = 5 }));
    }

    // ───────────── 列表接口 ─────────────

    [Fact]
    public async Task CardCare_ShowsTheCardNameAndHowManyTimesAreLeft_AndTheAmountsStayAsStored()
    {
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 5);
        int orderId = await AddOrder(f, CardCare(cardId));
        var care = Assert.Single(await ListCares(f, orderId));
        Assert.Equal("孙伟博学员专用", care.cardDisplayName);
        Assert.Equal(5, care.cardRemaining);
        Assert.False(care.cardUnlimited);
        Assert.Equal(0.0, care.displayCharge);      // 次卡抵掉了：金额、减免直接显示库里的 0
        Assert.Equal(0.0, care.displayDiscount);
    }

    [Fact]
    public async Task TwoEquipmentOnTheSameCard_BothShowTheCardsCurrentRemaining()
    {
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 6);
        int orderId = await AddOrder(f, CardCare(cardId), CardCare(cardId));
        var cares = await ListCares(f, orderId);
        Assert.Equal(new int?[] { 4, 4 }, cares.Select(c => c.cardRemaining).ToArray());
    }

    [Fact]
    public async Task SeasonCard_IsUnlimited_WithNoRemainingCount()
    {
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: null, punches: 3, name: "机打蜡季卡");
        int orderId = await AddOrder(f, CardCare(cardId, "机打蜡季卡"));
        var care = Assert.Single(await ListCares(f, orderId));
        Assert.Equal("机打蜡季卡", care.cardDisplayName);
        Assert.True(care.cardUnlimited);
        Assert.Null(care.cardRemaining);
    }

    [Fact]
    public async Task EquipmentWithoutACard_HasNoCardInfo()
    {
        await using var f = await CoachFixture.Create();
        await AddCard(f, total: 10, punches: 5);
        int orderId = await AddOrder(f, new Care { equipment = "双板", need_edge = 1, biz_type = "雪季养护", common_charge = 120 });
        var care = Assert.Single(await ListCares(f, orderId));
        Assert.Null(care.cardDisplayName);
        Assert.Null(care.cardRemaining);
        Assert.False(care.cardUnlimited);
    }

    [Fact]
    public async Task CardNotBoundYet_ShowsTheNameKeptOnTheCare_WithoutRemaining()
    {
        // 购卡处理中：装备已标用次卡，但 card_id 还空着
        await using var f = await CoachFixture.Create();
        int orderId = await AddOrder(f, CardCare(null, "新买的次卡"));
        var care = Assert.Single(await ListCares(f, orderId));
        Assert.Equal("新买的次卡", care.cardDisplayName);
        Assert.Null(care.cardRemaining);
        Assert.False(care.cardUnlimited);
    }

    [Fact]
    public async Task WhenTheCardsCannotBeRead_TheListStillWorks_AndTheEquipmentJustHasNoCardInfo()
    {
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 5);
        int orderId = await AddOrder(f, CardCare(cardId));
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE punch_card");
        var care = Assert.Single(await ListCares(f, orderId));
        Assert.Null(care.cardRemaining);
        Assert.False(care.cardUnlimited);
        Assert.Equal(0.0, care.displayCharge);       // 金额照常
    }

    [Fact]
    public async Task ListingDoesNotChangeTheCard()
    {
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 5);
        int orderId = await AddOrder(f, CardCare(cardId));
        await ListCares(f, orderId);
        var stored = await f.NewContext().punchCard.AsNoTracking().SingleAsync(c => c.id == cardId);
        Assert.Equal((10, 5), (stored.total, stored.punches));
    }
}

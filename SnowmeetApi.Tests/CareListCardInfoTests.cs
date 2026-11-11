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
///   · 剩余次数 = 这件装备「用完之后」卡里还剩几次 = 卡现在的余量 + 这件之后又用掉的次数（核销明细 id 更大的有效记录）：
///     同一张卡上先后用的两件装备，先用的比后用的多 1，最后一次使用显示卡现在的余量；自己没有有效核销记录就取卡现在的余量；不为负、不超过总次数；
///   · 季卡（总次数为空）不限次，标 cardUnlimited；
///   · 卡号 = 卡 id，沿用装备自带的 card_id（列表接口照常返回），小程序直接显示；
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

    // 次卡核销明细（punch_card_used）：每件装备核销一次写一条，id 递增 = 使用先后
    private static async Task AddUse(CoachFixture f, int cardId, int orderId, int bizId, bool valid = true, int punchCount = 1, string bizType = "养护")
    {
        f.Db.punchCardUsed.Add(new PunchCardUsed { card_id = cardId, order_id = orderId, biz_type = bizType, biz_id = bizId, punch_count = punchCount, valid = valid });
        await f.Db.SaveChangesAsync();
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
    public void Rule_UsedAfter_CountsOnlyValidUsesWithLargerIdThanTheCaresOwn_AndNullWithoutAnOwnValidUse()
    {
        PunchCardUsed U(int id, int bizId, int count = 1, bool valid = true, string type = "养护") =>
            new PunchCardUsed { id = id, card_id = 1, biz_id = bizId, punch_count = count, valid = valid, biz_type = type };
        var uses = new[] { U(1, 100), U(2, 200), U(3, 300, count: 2), U(4, 400, valid: false), U(5, 500) };
        Assert.Equal(3, CareCardInfoRules.UsedAfter(uses, 200));    // 之后的有效记录：id3 扣 2 次 + id5 扣 1 次；作废的 id4 不算
        Assert.Equal(0, CareCardInfoRules.UsedAfter(uses, 500));    // 最后一次使用
        Assert.Null(CareCardInfoRules.UsedAfter(uses, 400));        // 自己的记录作废
        Assert.Null(CareCardInfoRules.UsedAfter(uses, 999));        // 自己没有记录
        Assert.Null(CareCardInfoRules.UsedAfter(new[] { U(1, 100, type: "租赁") }, 100));   // 租赁核销的 biz_id 是 rental，不是 care
    }

    [Fact]
    public void Rule_RemainingAfterTheUse_IsCurrentPlusLaterUses_ClampedToZeroAndTotal()
    {
        var care = new Care { use_card = true };
        var card = new PunchCard { card_name = "卡", total = 10, punches = 5 };
        Assert.Equal(("卡", 5, false), CareCardInfoRules.Build(care, card, 0));
        Assert.Equal(("卡", 6, false), CareCardInfoRules.Build(care, card, 1));
        Assert.Equal(("卡", 5, false), CareCardInfoRules.Build(care, card, null));      // 没有自己的核销记录：卡现在的余量
        Assert.Equal(("卡", 10, false), CareCardInfoRules.Build(care, card, 99));       // 不超过总次数（已用累计和明细对不上的老数据）
        Assert.Equal(("卡", 0, false), CareCardInfoRules.Build(care, new PunchCard { card_name = "卡", total = 3, punches = 5 }, 1));   // 不为负
        Assert.Equal(("卡", null, true), CareCardInfoRules.Build(care, new PunchCard { card_name = "卡", total = null, punches = 5 }, 3));   // 季卡不限次
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
    public async Task TwoEquipmentOnTheSameCard_WithoutUseRecords_BothShowTheCardsCurrentRemaining()
    {
        // 没有核销明细（订单还没生效）：算不出各自用完之后的余量，都退回卡现在的余量
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 6);
        int orderId = await AddOrder(f, CardCare(cardId), CardCare(cardId));
        var cares = await ListCares(f, orderId);
        Assert.Equal(new int?[] { 4, 4 }, cares.Select(c => c.cardRemaining).ToArray());
    }

    [Fact]
    public async Task TwoEquipmentOnTheSameCard_EachShowsWhatWasLeftAfterItsOwnUse()
    {
        // 实录：总 10 次，前面已经用掉 3 次（别的订单），这一单两件装备先后各核销一次，卡现在剩 5。
        // 先用的显示 6，后用的显示 5（最后一次使用显示卡现在的余量）
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 5);
        await AddUse(f, cardId, orderId: 1, bizId: 900);
        await AddUse(f, cardId, orderId: 2, bizId: 901);
        await AddUse(f, cardId, orderId: 2, bizId: 902);
        Care first = CardCare(cardId), second = CardCare(cardId);
        int orderId = await AddOrder(f, first, second);
        await AddUse(f, cardId, orderId, first.id);
        await AddUse(f, cardId, orderId, second.id);
        var cares = await ListCares(f, orderId);
        Assert.Equal(new int?[] { 6, 5 }, cares.Select(c => c.cardRemaining).ToArray());
        Assert.Equal(new int?[] { cardId, cardId }, cares.Select(c => c.card_id).ToArray());   // 卡号（卡 id）随装备一起返回
    }

    [Fact]
    public async Task EquipmentOnDifferentCards_EachCountsOnlyItsOwnCard()
    {
        await using var f = await CoachFixture.Create();
        int cardA = await AddCard(f, total: 10, punches: 5, name: "A卡");
        int cardB = await AddCard(f, total: 5, punches: 1, name: "B卡");
        Care a = CardCare(cardA, "A卡"), b = CardCare(cardB, "B卡");
        int orderId = await AddOrder(f, a, b);
        await AddUse(f, cardA, orderId, a.id);
        await AddUse(f, cardB, orderId, b.id);
        var cares = await ListCares(f, orderId);
        Assert.Equal(new[] { ("A卡", (int?)5), ("B卡", 4) }, cares.Select(c => (c.cardDisplayName!, c.cardRemaining)).ToArray());
    }

    [Fact]
    public async Task AUseThatWasCancelled_ShowsTheCardsCurrentRemaining()
    {
        // 取消返次后这件装备自己的核销记录作废：不按「用完之后」算，显示卡现在的余量；作废的记录也不计入别的装备「之后又用掉」
        await using var f = await CoachFixture.Create();
        int cardId = await AddCard(f, total: 10, punches: 5);
        Care cancelled = CardCare(cardId), normal = CardCare(cardId);
        int orderId = await AddOrder(f, cancelled, normal);
        await AddUse(f, cardId, orderId, cancelled.id, valid: false);
        await AddUse(f, cardId, orderId, normal.id);
        var cares = await ListCares(f, orderId);
        Assert.Equal(new int?[] { 5, 5 }, cares.Select(c => c.cardRemaining).ToArray());
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

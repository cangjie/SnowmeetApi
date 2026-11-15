using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

// 机构自己制作会员卡：管理员设底价（下限），机构在底价之内自己设养护 / 租赁优惠，保存即生效，只影响之后领取的新卡。
public class OrgCardStandardTests
{
    private const string Wl = "万龙服务中心", Cl = "崇礼旗舰店";

    // 教练 1（会员 2）是「教练」类型，教练 2（会员 6）是「机构」类型；都没有设价格
    private static async Task<CoachFixture> NewFixture()
    {
        var f = await CoachFixture.Create(withPricing: false);
        foreach (var (member, coach, type) in new[] { (CoachFixture.CoachMember, f.CoachId, "教练"), (CoachFixture.CoachMember2, f.Coach2Id, "机构") })
        {
            var relation = new OrderShareRelation { name = type + member, type = "个人", valid = true };
            f.Db.orderShareRelation.Add(relation);
            await f.Db.SaveChangesAsync();
            f.Db.introducer.Add(new Introducer { member_id = member, coach_id = coach, type = type, name = type + member, relation_id = relation.id, valid = 1 });
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static DistributorController Org(CoachFixture f) => new(f.NewContext(), f.Config);
    private static CoachAdminController Admin(CoachFixture f) => new(f.NewContext(), f.Config);
    private static JsonElement J(object? data) => JsonSerializer.SerializeToElement(data);

    private static CarePriceInput Care(string shop, string tier, string mode, double value) => new(shop, tier, mode, value);
    private static CareFloorInput Floor(string shop, string tier, double? rate, double? fixedPrice) => new(shop, tier, rate, fixedPrice);

    private static async Task SetFloors(CoachFixture f, int coachId, List<CareFloorInput> care, double? rental)
    {
        var res = await Admin(f).SetCoachFloors("session-1", new CoachAdminController.SetFloorsInput(coachId, care, rental));
        Assert.Equal(0, res.code);
    }

    // ---------- 纯规则 ----------

    [Theory]
    [InlineData("none", 0, null)]                 // 不设优惠总是可以
    [InlineData("rate", 0.7, null)]               // 刚好等于底价
    [InlineData("rate", 0.8, null)]
    [InlineData("rate", 0.69, "折扣不能低于 7 折")]
    [InlineData("fixed", 60, null)]
    [InlineData("fixed", 59.99, "一口价不能低于 ¥60")]
    public void Care_cell_is_checked_against_both_floors(string mode, double value, string? expected)
    {
        var floor = new CoachPriceFloor { min_rate = 0.7, min_fixed = 60 };
        string? msg = CoachFloorRules.CheckCare(floor, mode, value, "万龙 单项");
        if (expected == null) Assert.Null(msg); else Assert.Contains(expected, msg);
    }

    [Fact]
    public void A_cell_the_admin_has_not_opened_cannot_have_a_discount_and_a_single_floor_opens_only_that_mode()
    {
        Assert.Contains("还没有开放", CoachFloorRules.CheckCare(null, "rate", 0.9, "x"));
        Assert.Contains("还没有开放", CoachFloorRules.CheckCare(new CoachPriceFloor(), "fixed", 100, "x"));
        Assert.Null(CoachFloorRules.CheckCare(null, "none", 0, "x"));
        Assert.Contains("只开放了一口价", CoachFloorRules.CheckCare(new CoachPriceFloor { min_fixed = 50 }, "rate", 0.9, "x"));
        Assert.Contains("只开放了折扣", CoachFloorRules.CheckCare(new CoachPriceFloor { min_rate = 0.8 }, "fixed", 100, "x"));
        Assert.Null(CoachFloorRules.CheckRental(null, null));
        Assert.Contains("还没有开放", CoachFloorRules.CheckRental(null, 0.9));
        Assert.Contains("不能低于 8 折", CoachFloorRules.CheckRental(new CoachPriceFloor { min_rate = 0.8 }, 0.75));
        Assert.Null(CoachFloorRules.CheckRental(new CoachPriceFloor { min_rate = 0.8 }, 0.8));
    }

    // ---------- 管理员设底价 ----------

    [Fact]
    public async Task Only_admins_can_read_or_set_floors_and_bad_values_are_rejected()
    {
        await using var f = await NewFixture();
        var input = new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { Floor(Wl, "单项", 0.7, null) }, 0.8);
        Assert.Equal(3, (await Admin(f).SetCoachFloors("session-7", input)).code);            // 店员
        Assert.Equal(3, (await Admin(f).GetCoachFloors("session-7", f.Coach2Id)).code);
        Assert.Equal(2, (await Admin(f).GetCoachFloors("nope", f.Coach2Id)).code);
        foreach (var bad in new[]
        {
            new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { Floor(Wl, "单项", 0, null) }, null),
            new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { Floor(Wl, "单项", 1.5, null) }, null),
            new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { Floor(Wl, "单项", null, -1) }, null),
            new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { Floor("南山", "单项", 0.7, null) }, null),
            new CoachAdminController.SetFloorsInput(f.Coach2Id, new() { Floor(Wl, "三项", 0.7, null) }, null),
            new CoachAdminController.SetFloorsInput(f.Coach2Id, new(), 2),
            new CoachAdminController.SetFloorsInput(9999, new(), null),
        })
            Assert.Equal(1, (await Admin(f).SetCoachFloors("session-1", bad)).code);
        Assert.Empty(await f.NewContext().coachPriceFloor.ToListAsync());

        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.7, 60), Floor(Cl, "双项", null, 100) }, 0.8);
        var view = J((await Admin(f).GetCoachFloors("session-1", f.Coach2Id)).data);
        Assert.Equal(4, view.GetProperty("care").GetArrayLength());
        var wlOne = view.GetProperty("care").EnumerateArray().Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.Equal(0.7, wlOne.GetProperty("minRate").GetDouble());
        Assert.Equal(60, wlOne.GetProperty("minFixed").GetDouble());
        Assert.Equal(0.8, view.GetProperty("rentalMinRate").GetDouble());
        // 再设一次是更新不是新增；去掉的格子回到未开放
        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.75, null) }, null);
        Assert.Equal(5, await f.NewContext().coachPriceFloor.CountAsync());
        var again = J((await Admin(f).GetCoachFloors("session-1", f.Coach2Id)).data);
        Assert.Equal(JsonValueKind.Null, again.GetProperty("rentalMinRate").ValueKind);
        // 两次成功的设置各写一条审计，带前后值
        var logs = await f.NewContext().coreDataModLog.Where(l => l.table_name == "coach_price_floor").OrderBy(l => l.id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => { Assert.Equal("管理员设置机构会员卡底价", l.scene); Assert.Equal(9, l.staff_id); Assert.NotNull(l.current_value); });
        Assert.Equal("", logs[0].prev_value);
    }

    // ---------- 机构自己制作会员卡 ----------

    [Fact]
    public async Task A_coach_cannot_edit_the_card_standard_and_a_non_distributor_gets_no_page()
    {
        await using var f = await NewFixture();
        var coachView = J((await Org(f).GetMyCardStandard("session-2")).data);
        Assert.False(coachView.GetProperty("canEdit").GetBoolean());
        Assert.Contains("只有机构", coachView.GetProperty("reason").GetString());
        var save = await Org(f).SaveMyCardStandard("session-2", new(new(), null));
        Assert.Equal(3, save.code);
        Assert.Equal(3, (await Org(f).GetMyCardStandard("session-3")).code);               // 学员
        Assert.Equal(2, (await Org(f).GetMyCardStandard("nope")).code);
    }

    [Fact]
    public async Task Without_floors_an_organization_can_save_no_discount_but_not_any_discount()
    {
        await using var f = await NewFixture();
        var view = J((await Org(f).GetMyCardStandard("session-6")).data);
        Assert.True(view.GetProperty("canEdit").GetBoolean());
        Assert.All(view.GetProperty("cells").EnumerateArray(), c => Assert.False(c.GetProperty("open").GetBoolean()));
        Assert.False(view.GetProperty("rental").GetProperty("open").GetBoolean());

        var rejected = await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.9) }, null));
        Assert.Equal(1, rejected.code);
        Assert.Contains("还没有开放", rejected.message);
        Assert.Equal(1, (await Org(f).SaveMyCardStandard("session-6", new(new(), 0.9))).code);
        Assert.Equal(0, (await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "none", 0) }, null))).code);
    }

    [Fact]
    public async Task Saving_is_checked_cell_by_cell_against_the_floors_and_takes_effect_for_new_cards_only()
    {
        await using var f = await NewFixture();
        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.7, null), Floor(Cl, "单项", null, 60) }, 0.8);

        // 低于底价、没开放的格子、方式不对：一次说清所有问题，且什么都不保存
        var bad = await Org(f).SaveMyCardStandard("session-6", new(new()
        {
            Care(Wl, "单项", "rate", 0.6), Care(Wl, "双项", "rate", 0.9), Care(Cl, "单项", "rate", 0.9), Care(Cl, "双项", "none", 0),
        }, 0.75));
        Assert.Equal(1, bad.code);
        Assert.Contains("万龙服务中心 单项：折扣不能低于 7 折", bad.message);
        Assert.Contains("万龙服务中心 双项：管理员还没有开放这一项", bad.message);
        Assert.Contains("崇礼旗舰店 单项：管理员这一项只开放了一口价", bad.message);
        Assert.Contains("租赁：折扣不能低于 8 折", bad.message);
        Assert.Empty(await f.NewContext().coachCarePrice.Where(p => p.coach_id == f.Coach2Id && p.mode != "none").ToListAsync());

        // 一张在改价之前领取的卡
        await f.Admin_().SetPricingAsync(f.Coach2Id, new PricingInput(new() { Care(Wl, "单项", "rate", 0.9) }, null));
        CoachCard oldCard = await f.IssueAndClaim(CoachFixture.StudentA, f.Coach2Id);

        // 刚好等于底价可以；一口价刚好等于底价可以
        var ok = await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.7), Care(Cl, "单项", "fixed", 60) }, 0.8));
        Assert.Equal(0, ok.code);
        var saved = J(ok.data);
        var wlOne = saved.GetProperty("cells").EnumerateArray().Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.Equal(("rate", 0.7), (wlOne.GetProperty("mode").GetString(), wlOne.GetProperty("value").GetDouble()));
        Assert.Equal(0.8, saved.GetProperty("rental").GetProperty("rate").GetDouble());
        Assert.Equal(0.8, (await f.NewContext().coach.AsNoTracking().SingleAsync(c => c.id == f.Coach2Id)).rental_rate);

        // 只影响之后领取的新卡
        CoachCard newCard = await f.IssueAndClaim(CoachFixture.StudentB, f.Coach2Id);
        Assert.Contains("0.9", (await f.NewContext().coachCard.AsNoTracking().SingleAsync(c => c.id == oldCard.id)).care_price_snapshot);
        var newSnapshot = (await f.NewContext().coachCard.AsNoTracking().SingleAsync(c => c.id == newCard.id)).care_price_snapshot;
        Assert.Contains("0.7", newSnapshot);
        Assert.Contains("60", newSnapshot);
        Assert.Equal(0.8, (await f.NewContext().coachCard.AsNoTracking().SingleAsync(c => c.id == newCard.id)).rental_rate_snapshot);

        // 审计：谁（会员 6）、改了什么
        var log = await f.NewContext().coreDataModLog.AsNoTracking().SingleAsync(l => l.scene == "机构自助设置会员卡");
        Assert.Equal((f.Coach2Id, CoachFixture.CoachMember2, "coach_care_price"), (log.key_value, log.member_id, log.table_name));
        Assert.NotEqual(log.prev_value, log.current_value);
        // 没有变化的再保存一次不再写审计
        await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.7), Care(Cl, "单项", "fixed", 60) }, 0.8));
        Assert.Equal(1, await f.NewContext().coreDataModLog.CountAsync(l => l.scene == "机构自助设置会员卡"));
    }

    [Fact]
    public async Task Raising_the_floor_later_flags_the_old_setting_and_saving_it_unchanged_is_refused()
    {
        await using var f = await NewFixture();
        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.7, null) }, null);
        Assert.Equal(0, (await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.72) }, null))).code);
        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.8, null) }, null);              // 管理员把底价调高
        var view = J((await Org(f).GetMyCardStandard("session-6")).data);
        var cell = view.GetProperty("cells").EnumerateArray().Single(c => c.GetProperty("shop").GetString() == Wl && c.GetProperty("tier").GetString() == "单项");
        Assert.True(cell.GetProperty("belowFloor").GetBoolean());
        Assert.Equal(1, (await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.72) }, null))).code);
        Assert.Equal(0, (await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.8) }, null))).code);
    }

    [Fact]
    public async Task A_stopped_organization_cannot_edit_and_the_admin_can_still_set_any_price()
    {
        await using var f = await NewFixture();
        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.7, null) }, null);
        // 管理员自己给机构设价不受底价限制（底价只约束机构自助）
        var admin = await Admin(f).SetCoachPricing("session-1", new CoachAdminController.SetPricingInput(f.Coach2Id, new() { Care(Wl, "单项", "rate", 0.5) }, 0.5));
        Assert.Equal(0, admin.code);
        Assert.Equal("机构", J(admin.data).GetProperty("introducerType").GetString());
        await f.NewContext().coach.Where(c => c.id == f.Coach2Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.valid, 0));
        await f.NewContext().introducer.Where(i => i.coach_id == f.Coach2Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.valid, 0));
        Assert.Equal(3, (await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.7) }, null))).code);   // 已停用：不再是分销商
    }

    [Fact]
    public async Task An_organization_without_a_card_is_told_to_make_one_rather_than_to_ask_the_admin()
    {
        await using var f = await NewFixture();
        var org = await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(f.Coach2Id, DateTime.Now));
        Assert.Contains("制作会员卡", org.Message);
        var coach = await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(f.CoachId, DateTime.Now));
        Assert.Contains("管理员还没有给你设置", coach.Message);
        // 做好之后就能发卡
        await SetFloors(f, f.Coach2Id, new() { Floor(Wl, "单项", 0.7, null) }, null);
        Assert.Equal(0, (await Org(f).SaveMyCardStandard("session-6", new(new() { Care(Wl, "单项", "rate", 0.8) }, null))).code);
        Assert.NotNull(await f.Cards().CreateCardAsync(f.Coach2Id, DateTime.Now));
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

public class CoachCardClaimTests
{
    [Fact]
    public async Task CreateCard_NeedsPricingAndCapsPendingCards()
    {
        await using var noPricing = await CoachFixture.Create(withPricing: false);
        var e = await Assert.ThrowsAsync<CoachException>(() => noPricing.Cards().CreateCardAsync(noPricing.CoachId, DateTime.Now));
        Assert.Contains("价格", e.Message);

        await using var f = await CoachFixture.Create();
        for (int i = 0; i < CoachCardRules.MaxUnclaimedCards; i++) await f.Cards().CreateCardAsync(f.CoachId, DateTime.Now);
        var capped = await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(f.CoachId, DateTime.Now));
        Assert.Contains("待领取", capped.Message);
        // 撤销一张后又能建
        var one = (await f.Query().ListCardsAsync(f.CoachId, null)).First();
        await f.Cards().RevokeAsync(one.id, f.CoachId, DateTime.Now);
        await f.Cards().CreateCardAsync(f.CoachId, DateTime.Now);
    }

    [Fact]
    public async Task CreateCard_DisabledCoachCannotIssue()
    {
        await using var f = await CoachFixture.Create();
        await f.Admin_().UpdateCoachAsync(f.CoachId, null, false);
        await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(f.CoachId, DateTime.Now));
    }

    [Fact]
    public async Task Claim_LocksPriceSnapshotAndSeasonEnd_LaterPriceChangeDoesNotAffectIt()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        Assert.Equal(CoachCard.StatusClaimed, card.status);
        Assert.Equal(CoachFixture.StudentA, card.member_id);
        Assert.Equal(CoachCardRules.SeasonEnd(now, f.Config), card.end_date);
        Assert.Equal(card.end_date!.Value.Year, card.season_end_year);
        Assert.Equal(0.9, card.rental_rate_snapshot);
        var cells = CoachCardRules.ParseCarePrices(card.care_price_snapshot);
        Assert.Equal(4, cells.Count);
        Assert.Equal(128, CoachCardRules.FindCell(cells, "崇礼旗舰店", "双项")!.value);

        // 教练改价：只影响新领的卡
        await f.Admin_().SetPricingAsync(f.CoachId, new PricingInput(new() { new("万龙服务中心", "双项", "rate", 0.5) }, 0.5));
        var old = await f.Db.coachCard.AsNoTracking().FirstAsync(c => c.id == card.id);
        Assert.Equal(0.9, old.rental_rate_snapshot);
        Assert.Equal(0.8, CoachCardRules.FindCell(CoachCardRules.ParseCarePrices(old.care_price_snapshot), "万龙服务中心", "双项")!.value);
        CoachCard fresh = await f.IssueAndClaim(CoachFixture.StudentB);
        Assert.Equal(0.5, fresh.rental_rate_snapshot);
        Assert.Single(CoachCardRules.ParseCarePrices(fresh.care_price_snapshot));
    }

    [Fact]
    public async Task Claim_ConcurrentOnlyOneStudentWins()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.Cards().CreateCardAsync(f.CoachId, DateTime.Now);
        var results = await Task.WhenAll(
            f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, DateTime.Now),
            f.Cards().ClaimAsync(card.token, CoachFixture.StudentB, DateTime.Now),
            f.Cards().ClaimAsync(card.token, CoachFixture.StudentC, DateTime.Now));
        Assert.Single(results, r => r.state == ShareCardState.ClaimedByMe);
        Assert.Equal(2, results.Count(r => r.state == ShareCardState.Taken));
        var stored = await f.Db.coachCard.AsNoTracking().SingleAsync(c => c.id == card.id);
        Assert.Equal(CoachCard.StatusClaimed, stored.status);
        Assert.Contains(stored.member_id!.Value, new[] { CoachFixture.StudentA, CoachFixture.StudentB, CoachFixture.StudentC });
    }

    [Fact]
    public async Task Claim_SameStudentTwiceIsIdempotent_OtherStudentSeesTaken()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.Cards().CreateCardAsync(f.CoachId, DateTime.Now);
        Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, DateTime.Now)).state);
        Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, DateTime.Now)).state);
        var other = await f.Cards().ClaimAsync(card.token, CoachFixture.StudentB, DateTime.Now);
        Assert.Equal(ShareCardState.Taken, other.state);
        Assert.Contains("已被领取", other.message);
    }

    [Fact]
    public async Task Claim_RejectedStates()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        // 教练本人
        CoachCard c1 = await f.Cards().CreateCardAsync(f.CoachId, now);
        Assert.Equal(ShareCardState.Self, (await f.Cards().ClaimAsync(c1.token, CoachFixture.CoachMember, now)).state);
        // 已持有该教练本雪季的卡：第二张不能领，且这张仍可被别人领
        await f.IssueAndClaim(CoachFixture.StudentA);
        CoachCard c2 = await f.Cards().CreateCardAsync(f.CoachId, now);
        Assert.Equal(ShareCardState.AlreadyHas, (await f.Cards().ClaimAsync(c2.token, CoachFixture.StudentA, now)).state);
        Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(c2.token, CoachFixture.StudentB, now)).state);
        // 另一位教练的卡不受影响
        await f.Admin_().SetPricingAsync(f.Coach2Id, CoachFixture.Standard());
        CoachCard other = await f.Cards().CreateCardAsync(f.Coach2Id, now);
        Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(other.token, CoachFixture.StudentA, now)).state);
        // 已撤销
        CoachCard c3 = await f.Cards().CreateCardAsync(f.CoachId, now);
        await f.Cards().RevokeAsync(c3.id, f.CoachId, now);
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(c3.token, CoachFixture.StudentC, now)).state);
        // 不存在
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync("nope", CoachFixture.StudentC, now)).state);
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(null, CoachFixture.StudentC, now)).state);
        // 教练被停用后，没领的卡不能再领
        CoachCard c4 = await f.Cards().CreateCardAsync(f.CoachId, now);
        await f.Admin_().UpdateCoachAsync(f.CoachId, null, false);
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(c4.token, CoachFixture.StudentC, now)).state);
        Assert.NotNull(other);
    }

    [Fact]
    public async Task Claim_PastDeadlineIsInvalid()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.Cards().CreateCardAsync(f.CoachId, DateTime.Now);
        await f.Db.coachCard.Where(c => c.id == card.id).ExecuteUpdateAsync(s => s.SetProperty(c => c.claim_deadline, (DateTime?)DateTime.Now.AddMinutes(-5)));
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, DateTime.Now)).state);
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, DateTime.Now)).state);
    }

    [Fact]
    public async Task GetShareCard_ShowsEachStateAndBenefits()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        CoachCard card = await f.Cards().CreateCardAsync(f.CoachId, now);
        var open = await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, now);
        Assert.Equal(ShareCardState.Claimable, open.state);
        Assert.Equal("张教练", open.coachName);
        Assert.Equal(4, open.carePrices.Count);
        Assert.Equal(0.9, open.rentalRate);
        Assert.Equal(CoachCardRules.SeasonEnd(now, f.Config), open.validUntil);
        // 还没有会员的人也能看到可领取
        Assert.Equal(ShareCardState.Claimable, (await f.Cards().GetShareCardAsync(card.token, null, now)).state);
        Assert.Equal(ShareCardState.Self, (await f.Cards().GetShareCardAsync(card.token, CoachFixture.CoachMember, now)).state);
        await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, now);
        Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, now)).state);
        var taken = await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentB, now);
        Assert.Equal(ShareCardState.Taken, taken.state);
        Assert.Contains("已被领取", taken.message);
        Assert.Equal(ShareCardState.Taken, (await f.Cards().GetShareCardAsync(card.token, null, now)).state);
        Assert.Equal(ShareCardState.Invalid, (await f.Cards().GetShareCardAsync("nope", CoachFixture.StudentA, now)).state);
        // 已有该教练的卡：另一张待领取卡，同一学员打开
        CoachCard second = await f.Cards().CreateCardAsync(f.CoachId, now);
        Assert.Equal(ShareCardState.AlreadyHas, (await f.Cards().GetShareCardAsync(second.token, CoachFixture.StudentA, now)).state);
    }

    [Fact]
    public async Task Revoke_CoachOnlyOwnUnclaimed_AdminAlsoClaimed()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        CoachCard claimed = await f.IssueAndClaim(CoachFixture.StudentA);
        var e = await Assert.ThrowsAsync<CoachException>(() => f.Cards().RevokeAsync(claimed.id, f.CoachId, now));
        Assert.Contains("管理员", e.Message);
        CoachCard otherCoachCard = await f.Cards().CreateCardAsync(f.CoachId, now);
        await Assert.ThrowsAsync<CoachException>(() => f.Cards().RevokeAsync(otherCoachCard.id, f.Coach2Id, now));   // 别人的卡
        Assert.Equal(CoachCard.StatusRevoked, (await f.Cards().RevokeAsync(claimed.id, null, now)).status);          // 管理员可撤销已领取
        Assert.Equal(CoachCard.StatusRevoked, (await f.Cards().RevokeAsync(claimed.id, null, now)).status);          // 幂等
        // 撤销后不再有效
        var stored = await f.Db.coachCard.AsNoTracking().SingleAsync(c => c.id == claimed.id);
        Assert.False(CoachCardRules.IsEffective(stored, CoachFixture.StudentA, now));
    }

    [Fact]
    public async Task Controller_ClaimNeedsMemberAndShowsTakenToTheSecondStudent()
    {
        await using var f = await CoachFixture.Create();
        CoachCard card = await f.Cards().CreateCardAsync(f.CoachId, DateTime.Now);
        var ctl = () => new CoachCardController(f.NewContext(), f.Config, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.Equal(2, (await ctl().ClaimShareCard(card.token, "bad-session")).code);
        var noMember = await ctl().ClaimShareCard(card.token, "session-new");
        Assert.Equal(5, noMember.code);
        var peek = await ctl().GetShareCard(card.token, "session-new");
        Assert.Equal(0, peek.code);
        var ok = await ctl().ClaimShareCard(card.token, "session-3");
        Assert.Equal(0, ok.code);
        var second = await ctl().ClaimShareCard(card.token, "session-4");
        Assert.Equal(1, second.code);
        Assert.Contains("已被领取", second.message);
        Assert.Equal("taken", System.Text.Json.JsonSerializer.SerializeToElement(second.data).GetProperty("state").GetString());
        var mine = await ctl().GetMyCards("session-3");
        Assert.Equal(1, System.Text.Json.JsonSerializer.SerializeToElement(mine.data).GetProperty("cards").GetArrayLength());
    }

    [Fact]
    public async Task Controller_CoachEndpointsRequireCoachIdentity()
    {
        await using var f = await CoachFixture.Create();
        var ctl = () => new CoachController(f.NewContext(), f.Config);
        Assert.Equal(2, (await ctl().CreateCard("bad")).code);
        Assert.Equal(3, (await ctl().CreateCard("session-3")).code);          // 学员不是教练
        Assert.Equal(5, (await ctl().CreateCard("session-new")).code);
        var mine = await ctl().GetMyCoach("session-3");
        Assert.Equal(0, mine.code);
        Assert.False(System.Text.Json.JsonSerializer.SerializeToElement(mine.data).GetProperty("isCoach").GetBoolean());
        var made = await ctl().CreateCard("session-2");
        Assert.Equal(0, made.code);
        string path = System.Text.Json.JsonSerializer.SerializeToElement(made.data).GetProperty("sharePath").GetString()!;
        Assert.StartsWith("/pages/coach/claim/claim?t=", path);
        var list = await ctl().ListMyCards("session-2");
        Assert.Equal(1, System.Text.Json.JsonSerializer.SerializeToElement(list.data).GetProperty("cards").GetArrayLength());
    }
}

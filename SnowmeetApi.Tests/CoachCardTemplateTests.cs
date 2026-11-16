using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

public class CoachCardTemplateTests
{
    private static async Task<(CoachFixture f, Coach coach, Introducer intro)> Setup()
    {
        var f = await CoachFixture.Create();
        var relation = new OrderShareRelation { name = "机构", type = "个人", valid = true };
        f.Db.orderShareRelation.Add(relation); await f.Db.SaveChangesAsync();
        var intro = new Introducer { member_id = CoachFixture.CoachMember, coach_id = f.CoachId, type = "机构", name = "机构", relation_id = relation.id, valid = 1 };
        f.Db.introducer.Add(intro);
        f.Db.coachPriceFloor.Add(new CoachPriceFloor { coach_id = f.CoachId, biz_type = "养护", shop = "万龙服务中心", tier = "单项", min_fixed = 50 });
        f.Db.coachPriceFloor.Add(new CoachPriceFloor { coach_id = f.CoachId, biz_type = "租赁", shop = "", tier = "", min_rate = 0.5 });
        await f.Db.SaveChangesAsync();
        return (f, await f.NewContext().coach.AsNoTracking().FirstAsync(c => c.id == f.CoachId), intro);
    }

    [Fact]
    public async Task Each_share_is_single_recipient_and_different_templates_remain_independent()
    {
        var (f, coach, intro) = await Setup(); await using (f)
        {
            var svc = new CoachCardTemplateService(f.NewContext(), f.Config);
            var a = await svc.SaveAsync(coach, intro, null, "学员卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, 0.8);
            var b = await svc.SaveAsync(coach, intro, null, "教练卡", new() { new("万龙服务中心", "单项", "fixed", 50) }, 0.5);
            var now = DateTime.Now;
            var first = await f.Cards().CreateCardAsync(coach.id, now, a.id);
            var second = await f.Cards().CreateCardAsync(coach.id, now, a.id);
            var third = await f.Cards().CreateCardAsync(coach.id, now, b.id);
            Assert.NotEqual(first.token, second.token);
            var won = await f.Cards().ClaimAsync(first.token, CoachFixture.StudentA, now);
            Assert.Equal(ShareCardState.ClaimedByMe, won.state);
            Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(first.token, CoachFixture.StudentA, now)).state);
            Assert.Equal(ShareCardState.Taken, (await f.Cards().ClaimAsync(first.token, CoachFixture.StudentB, now)).state);
            Assert.Equal(ShareCardState.Taken, (await f.Cards().GetShareCardAsync(first.token, null, now)).state);
            Assert.Equal(ShareCardState.AlreadyHas, (await f.Cards().ClaimAsync(second.token, CoachFixture.StudentA, now)).state);
            Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(second.token, CoachFixture.StudentB, now)).state);
            Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(third.token, CoachFixture.StudentA, now)).state);
            Assert.Equal("学员卡", won.card!.card_name_snapshot);
            Assert.Equal(60, CoachCardRules.ParseCarePrices(won.card.care_price_snapshot).Single().value);
            Assert.Equal(2, (await f.Pricing().ListUsableAsync(CoachFixture.StudentA, "租赁", null, null, 0, 0, 100, now)).Count);
        }
    }

    [Fact]
    public async Task Concurrent_recipients_only_one_wins_and_old_template_link_cannot_claim()
    {
        var (f, coach, intro) = await Setup(); await using (f)
        {
            var template = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            var now = DateTime.Now;
            var issued = await f.Cards().CreateCardAsync(coach.id, now, template.id);
            var results = await Task.WhenAll(f.Cards().ClaimAsync(issued.token, CoachFixture.StudentA, now), f.Cards().ClaimAsync(issued.token, CoachFixture.StudentB, now), f.Cards().ClaimAsync(issued.token, CoachFixture.StudentC, now));
            Assert.Single(results, r => r.state == ShareCardState.ClaimedByMe);
            Assert.Equal(2, results.Count(r => r.state == ShareCardState.Taken));
            var old = new CoachCardController(f.NewContext(), f.Config, ShareFixture.Http);
            Assert.Equal(1, (await old.ClaimShareTemplate(template.token, "session-4")).code);
            var view = System.Text.Json.JsonSerializer.SerializeToElement((await old.GetShareTemplate(template.token, "session-4")).data).GetProperty("view");
            Assert.Equal("invalid", view.GetProperty("state").GetString());
            Assert.Equal(1, await f.NewContext().coachCard.CountAsync());
        }
    }

    [Fact]
    public async Task Prices_are_validated_again_at_claim_and_snapshot_is_locked()
    {
        var (f, coach, intro) = await Setup(); await using (f)
        {
            var svc = new CoachCardTemplateService(f.NewContext(), f.Config);
            await Assert.ThrowsAsync<CoachException>(() => svc.SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 49) }, null));
            var template = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            var now = DateTime.Now;
            var first = await f.Cards().CreateCardAsync(coach.id, now, template.id);
            Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(first.token, CoachFixture.StudentA, now)).state);
            var second = await f.Cards().CreateCardAsync(coach.id, now, template.id);
            var floor = await f.Db.coachPriceFloor.AsTracking().SingleAsync(x => x.coach_id == coach.id && x.biz_type == "养护");
            floor.min_fixed = 70; await f.Db.SaveChangesAsync();
            Assert.Equal(ShareCardState.Invalid, (await f.Cards().GetShareCardAsync(second.token, CoachFixture.StudentB, now)).state);
            Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(second.token, CoachFixture.StudentB, now)).state);
            Assert.Equal(60, CoachCardRules.ParseCarePrices((await f.NewContext().coachCard.SingleAsync(c => c.id == first.id)).care_price_snapshot).Single().value);
            await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(coach.id, now, template.id));
        }
    }

    [Fact]
    public async Task Only_owner_can_issue_and_manage_templates()
    {
        var (f, coach, intro) = await Setup(); await using (f)
        {
            var t = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(f.Coach2Id, DateTime.Now, t.id));
            await Assert.ThrowsAsync<CoachException>(() => f.Cards().CreateCardAsync(coach.id, DateTime.Now));
            Assert.Equal(3, (await new DistributorController(f.NewContext(), f.Config).ListMyCardTemplates("session-3")).code);
        }
    }
}

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
    public async Task Templates_are_independent_shareable_and_lock_their_prices()
    {
        var (f, coach, intro) = await Setup();
        await using (f)
        {
            var svc = new CoachCardTemplateService(f.NewContext(), f.Config);
            List<CarePriceInput> p = new() { new("万龙服务中心", "单项", "fixed", 60) };
            var a = await svc.SaveAsync(coach, intro, null, "学员卡", p, 0.8);
            var b = await svc.SaveAsync(coach, intro, null, "教练卡", new() { new("万龙服务中心", "单项", "fixed", 50) }, 0.5);
            var now = DateTime.Now;
            var cardA = await new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(a.token, CoachFixture.StudentA, now);
            var duplicate = await new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(a.token, CoachFixture.StudentA, now);
            var cardB = await new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(b.token, CoachFixture.StudentA, now);
            var other = await new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(a.token, CoachFixture.StudentB, now);
            Assert.Equal(cardA.id, duplicate.id); Assert.NotEqual(cardA.id, cardB.id); Assert.NotEqual(cardA.id, other.id);
            var usable = await f.Pricing().ListUsableAsync(CoachFixture.StudentA, "租赁", null, null, 0, 0, 100, now);
            Assert.Contains(usable, c => c.cardId == cardA.id && c.cardName == "学员卡");
            Assert.Contains(usable, c => c.cardId == cardB.id && c.cardName == "教练卡");
            await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, a.id, "新学员卡", new() { new("万龙服务中心", "单项", "fixed", 80) }, 0.9);
            var old = await f.NewContext().coachCard.AsNoTracking().FirstAsync(c => c.id == cardA.id);
            Assert.Equal("学员卡", old.card_name_snapshot);
            Assert.Equal(60, CoachCardRules.ParseCarePrices(old.care_price_snapshot).Single().value);
            var newest = await new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(a.token, CoachFixture.StudentC, now);
            Assert.Equal("新学员卡", newest.card_name_snapshot);
            Assert.Equal(80, CoachCardRules.ParseCarePrices(newest.care_price_snapshot).Single().value);
        }
    }

    [Fact]
    public async Task Shared_template_controller_allows_many_recipients_and_requires_a_session()
    {
        var (f, coach, intro) = await Setup();
        await using (f)
        {
            var t = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "教练卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            CoachCardController Card() => new(f.NewContext(), f.Config, ShareFixture.Http);
            Assert.Equal(2, (await Card().GetShareTemplate(t.token, "bad")).code);
            Assert.Equal(5, (await Card().ClaimShareTemplate(t.token, "session-new")).code);
            Assert.Equal(0, (await Card().ClaimShareTemplate(t.token, "session-3")).code);
            Assert.Equal(0, (await Card().ClaimShareTemplate(t.token, "session-4")).code);
            var view = System.Text.Json.JsonSerializer.SerializeToElement((await Card().GetShareTemplate(t.token, "session-3")).data).GetProperty("view");
            Assert.Equal("claimed_by_me", view.GetProperty("state").GetString());
            Assert.Equal("教练卡", view.GetProperty("cardName").GetString());
            Assert.Equal(2, await f.NewContext().coachCard.CountAsync());
        }
    }

    [Fact]
    public async Task Concurrent_repeat_claims_create_only_one_card()
    {
        var (f, coach, intro) = await Setup();
        await using (f)
        {
            var t = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(t.token, CoachFixture.StudentA, DateTime.Now)));
            Assert.Single(results.Select(c => c.id).Distinct());
            Assert.Equal(1, await f.NewContext().coachCard.CountAsync());
        }
    }

    [Fact]
    public async Task Disabling_the_issuer_stops_new_claims_but_preserves_existing_cards()
    {
        var (f, coach, intro) = await Setup();
        await using (f)
        {
            var t = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            var now = DateTime.Now;
            var card = await new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(t.token, CoachFixture.StudentA, now);
            var tracked = await f.Db.coach.AsTracking().SingleAsync(c => c.id == f.CoachId);
            tracked.valid = 0; await f.Db.SaveChangesAsync();
            var svc = new CoachCardTemplateService(f.NewContext(), f.Config);
            Assert.Equal(ShareCardState.ClaimedByMe, (await svc.ViewAsync(t.token, CoachFixture.StudentA, now)).state);
            Assert.Equal(card.id, (await svc.ClaimAsync(t.token, CoachFixture.StudentA, now)).id);
            Assert.Equal(ShareCardState.Invalid, (await svc.ViewAsync(t.token, CoachFixture.StudentB, now)).state);
            await Assert.ThrowsAsync<CoachException>(() => svc.ClaimAsync(t.token, CoachFixture.StudentB, now));
        }
    }

    [Fact]
    public async Task Floors_are_enforced_on_save_and_again_on_claim()
    {
        var (f, coach, intro) = await Setup();
        await using (f)
        {
            var svc = new CoachCardTemplateService(f.NewContext(), f.Config);
            await Assert.ThrowsAsync<CoachException>(() => svc.SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 49) }, null));
            var t = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            var floor = await f.Db.coachPriceFloor.AsTracking().SingleAsync(x => x.coach_id == f.CoachId && x.biz_type == "养护");
            floor.min_fixed = 70; await f.Db.SaveChangesAsync();
            var view = await new CoachCardTemplateService(f.NewContext(), f.Config).ViewAsync(t.token, CoachFixture.StudentA, DateTime.Now);
            Assert.Equal(ShareCardState.Invalid, view.state);
            await Assert.ThrowsAsync<CoachException>(() => new CoachCardTemplateService(f.NewContext(), f.Config).ClaimAsync(t.token, CoachFixture.StudentA, DateTime.Now));
            Assert.Equal(0, await f.NewContext().coachCard.CountAsync());
        }
    }

    [Fact]
    public async Task Other_members_and_coaches_cannot_manage_organization_templates()
    {
        var (f, coach, intro) = await Setup();
        await using (f)
        {
            var controller = new DistributorController(f.NewContext(), f.Config);
            Assert.Equal(3, (await controller.ListMyCardTemplates("session-3")).code);
            var editor = await controller.GetMyCardTemplate("session-2");
            Assert.Equal(0, editor.code);
            Assert.Equal(4, System.Text.Json.JsonSerializer.SerializeToElement(editor.data).GetProperty("cells").GetArrayLength());
            intro.type = "教练";
            await Assert.ThrowsAsync<CoachException>(() => new CoachCardTemplateService(f.NewContext(), f.Config).ListAsync(coach, intro));
        }
    }
}

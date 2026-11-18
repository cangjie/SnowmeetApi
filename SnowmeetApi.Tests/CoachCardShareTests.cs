using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Tests;

// 机构会籍卡的分享：每次分享（分享二维码 / 分享小程序卡片）都记一条待领取的卡；对方扫码或点开后领取，
// 领取要求头像和昵称都已授权，领取成功得到和领取人绑定的会籍卡卡号；没领取的可以取消。（2026-11-17 用户要求）
public class CoachCardShareTests
{
    private static JsonElement J(object? data) => JsonSerializer.SerializeToElement(data);

    // 教练 1（会员 2）= 机构，有一个卡种；会话 session-2
    private static async Task<(CoachFixture f, Coach coach, CoachCardTemplate template)> Setup()
    {
        var f = await CoachFixture.Create(withPricing: false);
        var relation = new OrderShareRelation { name = "机构", type = "个人", valid = true };
        f.Db.orderShareRelation.Add(relation); await f.Db.SaveChangesAsync();
        f.Db.introducer.Add(new Introducer { member_id = CoachFixture.CoachMember, coach_id = f.CoachId, type = "机构", name = "机构", relation_id = relation.id, valid = 1 });
        f.Db.coachPriceFloor.Add(new CoachPriceFloor { coach_id = f.CoachId, biz_type = "养护", shop = "万龙服务中心", tier = "单项", min_fixed = 50 });
        await f.Db.SaveChangesAsync();
        var coach = await f.NewContext().coach.AsNoTracking().FirstAsync(c => c.id == f.CoachId);
        var intro = await f.NewContext().introducer.AsNoTracking().FirstAsync(i => i.coach_id == f.CoachId);
        var template = await new CoachCardTemplateService(f.NewContext(), f.Config).SaveAsync(coach, intro, null, "雪季会籍卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
        return (f, coach, template);
    }

    private static CoachController Ctl(CoachFixture f) => new(f.NewContext(), f.Config);

    private static Task ClearProfile(CoachFixture f, int memberId, bool nick, bool avatar) =>
        f.Db.Database.ExecuteSqlRawAsync("UPDATE member SET nick_name = " + (nick ? "nick_name" : "NULL") + ", avatar = " + (avatar ? "avatar" : "NULL") + " WHERE id = " + memberId);

    // ---------- 纯规则 ----------

    [Fact]
    public void Card_number_is_hj_plus_six_digit_id()
    {
        Assert.Equal("HJ000001", CoachCardRules.CardNo(1));
        Assert.Equal("HJ000123", CoachCardRules.CardNo(123));
        Assert.Equal("HJ1234567", CoachCardRules.CardNo(1234567));
    }

    [Theory]
    [InlineData("qr", "二维码", "qr")]
    [InlineData("card", "小程序卡片", "card")]
    [InlineData(" QR ", "二维码", "qr")]
    [InlineData("other", null, null)]
    [InlineData(null, null, null)]
    public void Share_channel_round_trips_through_remark(string? input, string? remark, string? back)
    {
        Assert.Equal(remark, CoachCardRules.ShareChannelRemark(input));
        Assert.Equal(back, CoachCardRules.ShareChannelOf(remark));
    }

    [Fact]
    public void Remark_that_is_not_a_share_channel_is_not_mistaken_for_one()
        => Assert.Null(CoachCardRules.ShareChannelOf("管理员备注"));

    [Theory]
    [InlineData("昵称", "/upload/a.jpg", true)]
    [InlineData("昵称", "", false)]
    [InlineData("昵称", null, false)]
    [InlineData("", "/upload/a.jpg", false)]
    [InlineData("  ", "/upload/a.jpg", false)]
    [InlineData(null, null, false)]
    public void Profile_is_complete_only_with_nickname_and_avatar(string? nick, string? avatar, bool expected)
        => Assert.Equal(expected, MemberProfileRules.IsComplete(nick, avatar));

    // ---------- 每次分享记一条 ----------

    [Fact]
    public async Task Every_share_tap_records_a_new_pending_card_with_its_channel()
    {
        var (f, _, template) = await Setup(); await using (f)
        {
            var qr = J((await Ctl(f).CreateCard("session-2", new CoachController.CreateCardInput(template.id, "qr"))).data);
            var card = J((await Ctl(f).CreateCard("session-2", new CoachController.CreateCardInput(template.id, "card"))).data);
            var legacy = J((await Ctl(f).CreateCard("session-2", new CoachController.CreateCardInput(template.id))).data);
            Assert.Equal("qr", qr.GetProperty("channel").GetString());
            Assert.Equal("card", card.GetProperty("channel").GetString());
            Assert.Equal(JsonValueKind.Null, legacy.GetProperty("channel").ValueKind);
            Assert.Equal(3, new[] { qr, card, legacy }.Select(x => x.GetProperty("cardId").GetInt32()).Distinct().Count());
            Assert.Equal(3, new[] { qr, card, legacy }.Select(x => x.GetProperty("sharePath").GetString()).Distinct().Count());

            var list = J((await Ctl(f).ListMyCards("session-2")).data).GetProperty("cards").EnumerateArray().ToList();
            Assert.Equal(3, list.Count);
            Assert.All(list, x => Assert.Equal("issued", x.GetProperty("card").GetProperty("status").GetString()));
            Assert.Equal(new[] { "二维码", "小程序卡片" }.Length, new[] { "qr", "card" }.Count(c => list.Any(x => x.GetProperty("card").GetProperty("channel").GetString() == c)));
        }
    }

    // ---------- 领取要求头像和昵称 ----------

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]    // 只有昵称
    [InlineData(false, true)]    // 只有头像
    public async Task Claim_is_refused_until_both_nickname_and_avatar_are_authorized(bool hasNick, bool hasAvatar)
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            await ClearProfile(f, CoachFixture.StudentA, hasNick, hasAvatar);
            var now = DateTime.Now;
            var card = await f.Cards().CreateCardAsync(coach.id, now, template.id, "card");

            var view = await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, now);
            Assert.Equal(ShareCardState.Claimable, view.state);
            Assert.True(view.needProfile);

            var refused = await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, now);
            Assert.Equal(ShareCardState.NeedProfile, refused.state);
            Assert.Contains("头像和昵称", refused.message);
            var row = await f.NewContext().coachCard.AsNoTracking().FirstAsync(c => c.id == card.id);
            Assert.Equal(CoachCard.StatusIssued, row.status);   // 卡没被占走，授权后还能领
            Assert.Null(row.member_id);

            // 授权完整后同一张卡能领
            await f.Db.Database.ExecuteSqlRawAsync("UPDATE member SET nick_name = '雪友', avatar = '/upload/20261117/a.jpg' WHERE id = " + CoachFixture.StudentA);
            Assert.False((await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, now)).needProfile);
            var ok = await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, now);
            Assert.Equal(ShareCardState.ClaimedByMe, ok.state);
            Assert.Equal(CoachFixture.StudentA, ok.card!.member_id);
        }
    }

    [Fact]
    public async Task Members_with_a_complete_profile_and_people_without_a_member_are_not_flagged()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var card = await f.Cards().CreateCardAsync(coach.id, DateTime.Now, template.id, "qr");
            Assert.False((await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, DateTime.Now)).needProfile);
            var noMember = await f.Cards().GetShareCardAsync(card.token, null, DateTime.Now);   // 还没授权手机号：先走手机号，不提资料
            Assert.Equal(ShareCardState.Claimable, noMember.state);
            Assert.False(noMember.needProfile);
        }
    }

    [Fact]
    public async Task A_taken_or_cancelled_card_never_asks_for_a_profile()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var now = DateTime.Now;
            var taken = await f.Cards().CreateCardAsync(coach.id, now, template.id);
            Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(taken.token, CoachFixture.StudentA, now)).state);
            await ClearProfile(f, CoachFixture.StudentB, false, false);
            Assert.Equal(ShareCardState.Taken, (await f.Cards().ClaimAsync(taken.token, CoachFixture.StudentB, now)).state);

            var cancelled = await f.Cards().CreateCardAsync(coach.id, now, template.id);
            await f.Cards().RevokeAsync(cancelled.id, coach.id, now);
            Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(cancelled.token, CoachFixture.StudentB, now)).state);

            // 本雪季已领过同一卡种：说「已领过」，不再催着授权资料
            var again = await f.Cards().CreateCardAsync(coach.id, now, template.id);
            Assert.Equal(ShareCardState.AlreadyHas, (await f.Cards().ClaimAsync(again.token, CoachFixture.StudentA, now)).state);
            await ClearProfile(f, CoachFixture.StudentA, false, false);
            Assert.Equal(ShareCardState.AlreadyHas, (await f.Cards().ClaimAsync(again.token, CoachFixture.StudentA, now)).state);
        }
    }

    // ---------- 卡号 ----------

    [Fact]
    public async Task The_claimer_gets_a_card_number_bound_to_them_and_it_is_shown_in_both_lists()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var now = DateTime.Now;
            var card = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            Assert.Null((await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentA, now)).cardNo);   // 领取前没有卡号
            var cardCtl = new CoachCardController(f.NewContext(), f.Config, new Microsoft.AspNetCore.Http.HttpContextAccessor());
            var claim = J((await cardCtl.ClaimShareCard(card.token, "session-3")).data);
            string cardNo = CoachCardRules.CardNo(card.id);
            Assert.Equal(cardNo, claim.GetProperty("view").GetProperty("cardNo").GetString());

            var mine = J((await cardCtl.GetMyCards("session-3")).data).GetProperty("cards").EnumerateArray().Single();
            Assert.Equal(cardNo, mine.GetProperty("cardNo").GetString());

            var shared = J((await Ctl(f).ListMyCards("session-2")).data).GetProperty("cards").EnumerateArray().Single().GetProperty("card");
            Assert.Equal(cardNo, shared.GetProperty("cardNo").GetString());
            Assert.Equal(CoachFixture.StudentA, shared.GetProperty("memberId").GetInt32());

            // 他人再点开：看到已被领取，没有卡号
            Assert.Null((await f.Cards().GetShareCardAsync(card.token, CoachFixture.StudentB, now)).cardNo);
        }
    }

    [Fact]
    public async Task Without_the_profile_the_claim_endpoint_answers_need_profile_and_issues_no_number()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            await ClearProfile(f, CoachFixture.StudentA, true, false);
            var card = await f.Cards().CreateCardAsync(coach.id, DateTime.Now, template.id, "card");
            var cardCtl = new CoachCardController(f.NewContext(), f.Config, new Microsoft.AspNetCore.Http.HttpContextAccessor());
            var res = await cardCtl.ClaimShareCard(card.token, "session-3");
            Assert.Equal(1, res.code);
            Assert.Equal("need_profile", J(res.data).GetProperty("state").GetString());
            Assert.Equal("issued", (await f.NewContext().coachCard.AsNoTracking().FirstAsync(c => c.id == card.id)).status);
        }
    }

    // ---------- 分享列表：领取人的头像昵称、状态、取消 ----------

    [Fact]
    public async Task Share_list_shows_who_claimed_with_avatar_and_nickname_and_what_is_still_waiting()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var now = DateTime.Now;
            var claimed = await f.Cards().CreateCardAsync(coach.id, now.AddMinutes(-5), template.id, "qr");
            var waiting = await f.Cards().CreateCardAsync(coach.id, now, template.id, "card");
            await f.Db.Database.ExecuteSqlRawAsync("UPDATE member SET nick_name = '雪友小A', avatar = '/upload/20261117/avatar_a.jpg' WHERE id = " + CoachFixture.StudentA);
            Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(claimed.token, CoachFixture.StudentA, now)).state);

            var cards = J((await Ctl(f).ListMyCards("session-2")).data).GetProperty("cards").EnumerateArray().Select(x => x.GetProperty("card")).ToList();
            Assert.Equal(2, cards.Count);
            var pending = cards.Single(x => x.GetProperty("status").GetString() == "issued");
            Assert.Equal(waiting.id, pending.GetProperty("id").GetInt32());
            Assert.Equal("card", pending.GetProperty("channel").GetString());
            Assert.Equal(JsonValueKind.Null, pending.GetProperty("nickName").ValueKind);
            Assert.Equal(JsonValueKind.Null, pending.GetProperty("avatarUrl").ValueKind);
            Assert.Equal(JsonValueKind.Null, pending.GetProperty("cardNo").ValueKind);

            var done = cards.Single(x => x.GetProperty("status").GetString() == "claimed");
            Assert.Equal("qr", done.GetProperty("channel").GetString());
            Assert.Equal("雪友小A", done.GetProperty("nickName").GetString());
            Assert.Equal("https://img.snowmeet.top/upload/20261117/avatar_a.jpg", done.GetProperty("avatarUrl").GetString());
            Assert.Equal(CoachCardRules.CardNo(claimed.id), done.GetProperty("cardNo").GetString());
            Assert.Equal("雪季会籍卡", done.GetProperty("cardName").GetString());
        }
    }

    [Fact]
    public async Task An_unclaimed_share_can_be_cancelled_and_then_cannot_be_claimed()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var created = J((await Ctl(f).CreateCard("session-2", new CoachController.CreateCardInput(template.id, "qr"))).data);
            int id = created.GetProperty("cardId").GetInt32();
            string token = created.GetProperty("sharePath").GetString()!.Split("t=")[1];

            var res = await Ctl(f).RevokeMyCard("session-2", new CoachController.CardIdInput(id));
            Assert.Equal(0, res.code);
            var row = await f.NewContext().coachCard.AsNoTracking().FirstAsync(c => c.id == id);
            Assert.Equal(CoachCard.StatusRevoked, row.status);
            Assert.NotNull(row.revoked_at);

            Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(token, CoachFixture.StudentA, DateTime.Now)).state);
            Assert.Equal(ShareCardState.Invalid, (await f.Cards().GetShareCardAsync(token, CoachFixture.StudentA, DateTime.Now)).state);
            // 删除的分享不再列出、不计入统计；库里的行还在（标已撤销，留审计）
            var listed = J((await Ctl(f).ListMyCards("session-2")).data);
            Assert.Empty(listed.GetProperty("cards").EnumerateArray());
            Assert.Equal(0, listed.GetProperty("counts").GetProperty("total").GetInt32());
            Assert.Equal(0, listed.GetProperty("counts").GetProperty("revoked").GetInt32());
            Assert.Equal(0, J((await Ctl(f).GetMyCoach("session-2")).data).GetProperty("cards").GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public async Task Deleting_hides_only_unclaimed_shares_a_claimed_card_the_admin_revoked_is_still_listed()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var now = DateTime.Now;
            var deleted = await f.Cards().CreateCardAsync(coach.id, now, template.id, "card");
            await f.Cards().RevokeAsync(deleted.id, coach.id, now);
            var waiting = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            var claimed = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            await f.Cards().ClaimAsync(claimed.token, CoachFixture.StudentA, now);
            var revokedLater = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            await f.Cards().ClaimAsync(revokedLater.token, CoachFixture.StudentB, now);
            await f.Cards().RevokeAsync(revokedLater.id, null, now);   // 管理员撤销已领取的卡

            var res = J((await Ctl(f).ListMyCards("session-2")).data);
            var rows = res.GetProperty("cards").EnumerateArray().Select(x => x.GetProperty("card")).ToList();
            Assert.Equal(new[] { waiting.id, claimed.id, revokedLater.id }.OrderBy(x => x), rows.Select(r => r.GetProperty("id").GetInt32()).OrderBy(x => x));
            Assert.DoesNotContain(rows, r => r.GetProperty("id").GetInt32() == deleted.id);
            Assert.Equal(template.id, rows[0].GetProperty("templateId").GetInt32());
            var counts = res.GetProperty("counts");
            Assert.Equal(3, counts.GetProperty("total").GetInt32());
            Assert.Equal(1, counts.GetProperty("issued").GetInt32());
            Assert.Equal(1, counts.GetProperty("claimed").GetInt32());
            Assert.Equal(1, counts.GetProperty("revoked").GetInt32());   // 只有「领取后被撤销」的那张
            // 管理员看某个教练的卡，仍然看得到被删除的分享
            Assert.Equal(4, (await f.Query().ListCardsAsync(coach.id, null)).Count);
            // 没有领取过的再次分享：待领取的卡带分享路径，同一张卡
            var waitingRow = res.GetProperty("cards").EnumerateArray().Single(x => x.GetProperty("card").GetProperty("id").GetInt32() == waiting.id);
            Assert.Equal("/pages/coach/claim/claim?t=" + waiting.token, waitingRow.GetProperty("sharePath").GetString());
        }
    }

    [Fact]
    public async Task A_share_that_was_already_claimed_cannot_be_cancelled_by_the_organization()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var card = await f.Cards().CreateCardAsync(coach.id, DateTime.Now, template.id, "qr");
            await f.Cards().ClaimAsync(card.token, CoachFixture.StudentA, DateTime.Now);
            var res = await Ctl(f).RevokeMyCard("session-2", new CoachController.CardIdInput(card.id));
            Assert.NotEqual(0, res.code);
            Assert.Equal("claimed", (await f.NewContext().coachCard.AsNoTracking().FirstAsync(c => c.id == card.id)).status);
        }
    }

    // ---------- 删除卡种：从来没人领取过才能删（2026-11-18）----------

    private static DistributorController Org(CoachFixture f) => new(f.NewContext(), f.Config);
    private static CoachCardTemplateService Templates(CoachFixture f) => new(f.NewContext(), f.Config);

    [Fact]
    public async Task Template_list_says_how_many_claimed_and_whether_it_can_be_deleted()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var intro = await f.NewContext().introducer.AsNoTracking().FirstAsync(i => i.coach_id == f.CoachId);
            var other = await Templates(f).SaveAsync(coach, intro, null, "另一种卡", new() { new("万龙服务中心", "单项", "fixed", 60) }, null);
            var now = DateTime.Now;
            await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");                       // 待领取
            var claimed = await f.Cards().CreateCardAsync(coach.id, now, template.id, "card");
            await f.Cards().ClaimAsync(claimed.token, CoachFixture.StudentA, now);                      // 已领取
            await f.Cards().CreateCardAsync(coach.id, now, other.id, "qr");                          // 另一种卡：只有待领取

            var list = J((await Org(f).ListMyCardTemplates("session-2")).data).GetProperty("templates").EnumerateArray().ToList();
            var a = list.Single(x => x.GetProperty("id").GetInt32() == template.id);
            var b = list.Single(x => x.GetProperty("id").GetInt32() == other.id);
            Assert.Equal((1, 1, false), (a.GetProperty("claimedCount").GetInt32(), a.GetProperty("pendingCount").GetInt32(), a.GetProperty("canDelete").GetBoolean()));
            Assert.Equal((0, 1, true), (b.GetProperty("claimedCount").GetInt32(), b.GetProperty("pendingCount").GetInt32(), b.GetProperty("canDelete").GetBoolean()));
        }
    }

    [Fact]
    public async Task A_template_nobody_claimed_can_be_deleted_and_its_waiting_shares_go_with_it()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var now = DateTime.Now;
            var qr = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            var card = await f.Cards().CreateCardAsync(coach.id, now, template.id, "card");
            var cancelledEarlier = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            await f.Cards().RevokeAsync(cancelledEarlier.id, coach.id, now);

            var res = await Org(f).DeleteMyCardTemplate("session-2", new DistributorController.DeleteCardTemplateInput(template.id));
            Assert.Equal(0, res.code);
            Assert.Equal(2, J(res.data).GetProperty("revokedShares").GetInt32());

            var row = await f.NewContext().coachCardTemplate.AsNoTracking().FirstAsync(t => t.id == template.id);
            Assert.Equal(0, row.valid);                                                              // 软删除：行还在
            Assert.Empty(J((await Org(f).ListMyCardTemplates("session-2")).data).GetProperty("templates").EnumerateArray());
            // 它还没人领取的分享一并删了：不再列出、统计里没有、二维码和卡片都领不了
            var listed = J((await Ctl(f).ListMyCards("session-2")).data);
            Assert.Empty(listed.GetProperty("cards").EnumerateArray());
            Assert.Equal(0, listed.GetProperty("counts").GetProperty("total").GetInt32());
            foreach (var c in new[] { qr, card })
            {
                Assert.Equal(CoachCard.StatusRevoked, (await f.NewContext().coachCard.AsNoTracking().FirstAsync(x => x.id == c.id)).status);
                Assert.Equal(ShareCardState.Invalid, (await f.Cards().ClaimAsync(c.token, CoachFixture.StudentA, now)).state);
            }
            Assert.Equal(1, await f.NewContext().coreDataModLog.AsNoTracking().CountAsync(l => l.table_name == "coach_card_template" && l.key_value == template.id && l.scene == "机构删除会员卡种"));
            // 再删一次：已经没有这个卡种
            Assert.NotEqual(0, (await Org(f).DeleteMyCardTemplate("session-2", new DistributorController.DeleteCardTemplateInput(template.id))).code);
        }
    }

    [Fact]
    public async Task A_template_somebody_claimed_cannot_be_deleted_even_if_that_card_was_revoked_later()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var now = DateTime.Now;
            var waiting = await f.Cards().CreateCardAsync(coach.id, now, template.id, "qr");
            var claimed = await f.Cards().CreateCardAsync(coach.id, now, template.id, "card");
            await f.Cards().ClaimAsync(claimed.token, CoachFixture.StudentA, now);

            var refused = await Org(f).DeleteMyCardTemplate("session-2", new DistributorController.DeleteCardTemplateInput(template.id));
            Assert.Equal(1, refused.code); Assert.Contains("领取过", refused.message);
            Assert.Equal(1, (await f.NewContext().coachCardTemplate.AsNoTracking().FirstAsync(t => t.id == template.id)).valid);
            Assert.Equal(CoachCard.StatusIssued, (await f.NewContext().coachCard.AsNoTracking().FirstAsync(c => c.id == waiting.id)).status);   // 什么都没动

            await f.Cards().RevokeAsync(claimed.id, null, now);                                          // 管理员撤销这张已领取的卡：仍然「领取过」
            Assert.Equal(1, (await Org(f).DeleteMyCardTemplate("session-2", new DistributorController.DeleteCardTemplateInput(template.id))).code);
            Assert.False(J((await Org(f).ListMyCardTemplates("session-2")).data).GetProperty("templates").EnumerateArray().Single().GetProperty("canDelete").GetBoolean());
        }
    }

    [Fact]
    public async Task Only_the_owning_organization_can_delete_a_template()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            // 会员 6 的教练（不是机构）、没有分销商身份的会员、没登录，都删不了
            Assert.NotEqual(0, (await Org(f).DeleteMyCardTemplate("session-6", new DistributorController.DeleteCardTemplateInput(template.id))).code);
            Assert.NotEqual(0, (await Org(f).DeleteMyCardTemplate("session-3", new DistributorController.DeleteCardTemplateInput(template.id))).code);
            Assert.NotEqual(0, (await Org(f).DeleteMyCardTemplate("nope", new DistributorController.DeleteCardTemplateInput(template.id))).code);
            Assert.NotEqual(0, (await Org(f).DeleteMyCardTemplate("session-2", new DistributorController.DeleteCardTemplateInput(template.id + 999))).code);
            Assert.Equal(1, (await f.NewContext().coachCardTemplate.AsNoTracking().FirstAsync(t => t.id == template.id)).valid);
        }
    }

    // ---------- 分享二维码 ----------

    private static MiniStaffBookingQrService Qr(MiniTicketQrTests.Handler handler) => new(new MiniTicketQrTests.Tokens(), new HttpClient(handler));

    [Fact]
    public async Task Share_qr_encodes_only_the_card_token_and_lands_on_the_claim_page()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var created = J((await Ctl(f).CreateCard("session-2", new CoachController.CreateCardInput(template.id, "qr"))).data);
            int id = created.GetProperty("cardId").GetInt32();
            string token = created.GetProperty("sharePath").GetString()!.Split("t=")[1];
            var handler = new MiniTicketQrTests.Handler();

            var response = Assert.IsType<FileContentResult>(await Ctl(f).GetCardQr("session-2", id, Qr(handler)));
            Assert.Equal("image/png", response.ContentType);
            var body = JsonDocument.Parse(handler.Requests.Single()).RootElement;
            Assert.Equal("pages/coach/claim/claim", body.GetProperty("page").GetString());
            Assert.Equal("t=" + token, body.GetProperty("scene").GetString());
            Assert.True(body.GetProperty("scene").GetString()!.Length <= 32);   // 微信 scene 上限 32 位
        }
    }

    [Fact]
    public async Task Share_qr_is_only_for_the_owner_and_only_while_the_card_is_waiting()
    {
        var (f, coach, template) = await Setup(); await using (f)
        {
            var card = await f.Cards().CreateCardAsync(coach.id, DateTime.Now, template.id, "qr");
            var handler = new MiniTicketQrTests.Handler();
            var qr = Qr(handler);

            Assert.IsType<BadRequestObjectResult>(await Ctl(f).GetCardQr("nope", card.id, qr));             // 没登录
            Assert.IsType<BadRequestObjectResult>(await Ctl(f).GetCardQr("session-3", card.id, qr));        // 不是教练 / 机构
            Assert.IsType<BadRequestObjectResult>(await Ctl(f).GetCardQr("session-6", card.id, qr));        // 别的教练不能取这张卡的码
            Assert.IsType<BadRequestObjectResult>(await Ctl(f).GetCardQr("session-2", card.id + 999, qr));  // 不存在
            Assert.Equal(0, handler.Calls);

            await f.Cards().RevokeAsync(card.id, coach.id, DateTime.Now);
            Assert.IsType<BadRequestObjectResult>(await Ctl(f).GetCardQr("session-2", card.id, qr));        // 已取消
            var claimed = await f.Cards().CreateCardAsync(coach.id, DateTime.Now, template.id, "qr");
            await f.Cards().ClaimAsync(claimed.token, CoachFixture.StudentA, DateTime.Now);
            Assert.IsType<BadRequestObjectResult>(await Ctl(f).GetCardQr("session-2", claimed.id, qr));     // 已领取
            Assert.Equal(0, handler.Calls);
        }
    }

    [Fact]
    public async Task Share_qr_service_rejects_a_malformed_token()
    {
        var handler = new MiniTicketQrTests.Handler();
        var qr = Qr(handler);
        foreach (string bad in new[] { "", "short", "has space in token 1234", "a/../b/../c/../d1234", new string('a', 31) })
            await Assert.ThrowsAsync<ArgumentException>(() => qr.GetForCoachCardAsync(bad));
        Assert.Equal(0, handler.Calls);
    }
}

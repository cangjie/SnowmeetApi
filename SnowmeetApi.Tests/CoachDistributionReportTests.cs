using System.Text.Json;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using Xunit;

namespace SnowmeetApi.Tests;

public sealed class CoachDistributionReportTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task SummaryAndRecipientsCoverCardsBatchesAndLegacyTransfersWithoutDoubleCounting()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now.Date.AddHours(12);
        f.Db.coachCardTemplate.AddRange(
            new CoachCardTemplate { id = 201, coach_id = f.CoachId, name = "学员卡", token = "card-template-201", valid = 1 },
            new CoachCardTemplate { id = 202, coach_id = f.CoachId, name = "教练卡", token = "card-template-202", valid = 1 });
        f.Db.coachCard.AddRange(
            new CoachCard { coach_id = f.CoachId, template_id = 201, token = "card-1", member_id = 3, claimed_at = now, status = CoachCard.StatusClaimed },
            new CoachCard { coach_id = f.CoachId, template_id = 201, token = "card-2", member_id = 4, claimed_at = now.AddDays(-1), status = CoachCard.StatusRevoked },
            new CoachCard { coach_id = f.CoachId, template_id = 201, token = "card-3", status = CoachCard.StatusIssued },
            new CoachCard { coach_id = f.CoachId, template_id = 202, token = "card-unknown-time", member_id = 5, claimed_at = null, status = CoachCard.StatusClaimed },
            new CoachCard { coach_id = f.CoachId, token = "card-old", member_id = 5, claimed_at = now, status = CoachCard.StatusClaimed },
            new CoachCard { coach_id = f.Coach2Id, template_id = 201, token = "other-card", member_id = 4, claimed_at = now, status = CoachCard.StatusClaimed });
        f.Db.ticketTemplate.AddRange(
            new TicketTemplate { id = 101, name = "体验券", memo = "券权益", type = "养护券", miniapp_recept_path = "", valid = 1 },
            new TicketTemplate { id = 102, name = "尚未领取的券", memo = "", type = "养护券", miniapp_recept_path = "", valid = 1 });
        f.Db.coachCouponBatch.AddRange(
            new CoachCouponBatch { id = 101, coach_id = f.CoachId, template_id = 101, token = "batch-101", total_count = 10, valid = 1, request_id = "request-101" },
            new CoachCouponBatch { id = 102, coach_id = f.CoachId, template_id = 102, token = "batch-102", total_count = 5, valid = 1, request_id = "request-102" });
        Ticket T(string code, int member, int used, int valid, DateTime? expires) => new() {
            code = code, name = "体验券", memo = "", open_id = "mini-" + member, member_id = member, template_id = 101,
            used = used, valid = valid, is_active = 1, channel = "机构自制券", expire_date = expires, create_date = now, miniapp_recept_path = "" };
        f.Db.ticket.AddRange(T("used", 3, 1, 1, now.AddDays(-1)), T("expired", 4, 0, 1, now.AddDays(-1)),
            T("today", 5, 0, 1, now.Date), T("invalid", 3, 0, 0, now.AddDays(5)), T("legacy", 4, 0, 1, now.AddDays(5)));
        var codes = new[] { "used", "expired", "today", "invalid", "missing" };
        for (int i = 0; i < codes.Length; i++)
        {
            f.Db.coachCouponClaim.Add(new CoachCouponClaim { coach_id = f.CoachId, batch_id = 101, member_id = 3 + i,
                open_id = "mini-" + (3 + i), ticket_code = codes[i], claim_date = now.Date.AddDays(-i), create_date = now.AddDays(-i) });
            f.Db.coachCouponGrant.Add(new CoachCouponGrant { coach_id = f.CoachId, template_id = 101, ticket_code = codes[i], grant_key = "batch-" + i, staff_id = 0 });
        }
        // 旧券先转给 3，再被转给 4：显示最初领取人 3，但统计券的当前使用状态。
        f.Db.coachCouponGrant.Add(new CoachCouponGrant { coach_id = f.CoachId, template_id = 101, ticket_code = "legacy", grant_key = "old", staff_id = 9 });
        f.Db.ticketLog.AddRange(
            new TicketLog { code = "legacy", sender_open_id = "mini-2", accepter_open_id = "mini-3", memo = "分享获得", transact_time = now.AddDays(-9) },
            new TicketLog { code = "legacy", sender_open_id = "mini-3", accepter_open_id = "mini-4", memo = "分享获得", transact_time = now.AddDays(-8) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var svc = new CoachDistributionReportService(f.NewContext());
        var summary = Json(await svc.SummaryAsync(f.CoachId, now));
        var cards = summary.GetProperty("cards").EnumerateArray().ToArray();
        Assert.Equal(2, cards.Single(x => x.GetProperty("id").GetInt32() == 201).GetProperty("claimed").GetInt32());
        Assert.Equal(1, cards.Single(x => x.GetProperty("id").GetInt32() == 202).GetProperty("claimed").GetInt32());
        Assert.Equal(1, cards.Single(x => x.GetProperty("id").GetInt32() == 0).GetProperty("claimed").GetInt32());
        var coupon = summary.GetProperty("coupons").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == 101);
        Assert.Equal(6, coupon.GetProperty("claimed").GetInt32());
        Assert.Equal(1, coupon.GetProperty("used").GetInt32());
        Assert.Equal(4, coupon.GetProperty("unused").GetInt32());
        Assert.Equal(1, coupon.GetProperty("expired").GetInt32());
        Assert.Equal(1, coupon.GetProperty("invalid").GetInt32());
        Assert.Equal(1, coupon.GetProperty("unknown").GetInt32());
        Assert.Contains("管理员发放", coupon.GetProperty("source").GetString());
        Assert.Equal(0, summary.GetProperty("coupons").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == 102).GetProperty("claimed").GetInt32());

        var first = await svc.RecipientsAsync(f.CoachId, "coupon", 101, 1, 2, now);
        Assert.Equal(6, first.total); Assert.Equal(2, first.items.Count);
        Assert.Equal("13800001003", first.items[0].phone);
        var all = await svc.RecipientsAsync(f.CoachId, "coupon", 101, 1, 50, now);
        Assert.Equal("已使用", all.items.Single(x => x.code == "used").status);
        Assert.Equal("未使用 · 已过期", all.items.Single(x => x.code == "expired").status);
        Assert.Equal("未使用", all.items.Single(x => x.code == "today").status);
        Assert.Equal("券记录缺失", all.items.Single(x => x.code == "missing").status);
        Assert.Equal(3, all.items.Single(x => x.code == "legacy").memberId);
        var cardPage = await svc.RecipientsAsync(f.CoachId, "card", 201, 1, 20, now);
        Assert.Equal(2, cardPage.total);
        Assert.Contains(cardPage.items, r => r.status == "已撤销");
        Assert.Null(Assert.Single((await svc.RecipientsAsync(f.CoachId, "card", 202, 1, 20, now)).items).claimedAt);
        Assert.Empty((await svc.RecipientsAsync(f.Coach2Id, "coupon", 101, 1, 20, now)).items);
    }

    [Fact]
    public async Task ReportEndpointsRequireSystemAdministrator()
    {
        await using var f = await CoachFixture.Create();
        var controller = new CoachAdminController(f.NewContext(), f.Config);
        Assert.Equal(2, (await controller.GetDistributionSummary("bad-session", f.CoachId)).code);
        Assert.Equal(3, (await controller.GetDistributionSummary("session-7", f.CoachId)).code);
        Assert.Equal(3, (await controller.GetDistributionRecipients("session-7", f.CoachId, "coupon", 12)).code);
        Assert.Equal(0, (await controller.GetDistributionSummary("session-1", f.CoachId)).code);
        Assert.Equal(0, (await controller.GetDistributionRecipients("session-1", f.CoachId, "card", 0)).code);
        Assert.NotEqual(0, (await controller.GetDistributionRecipients("session-1", f.CoachId, "bad", 0)).code);
    }
}

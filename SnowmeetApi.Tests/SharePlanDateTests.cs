using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 介绍人分账的「预计分账日期」：每条介绍人分账可以指定哪天分；没指定就是最早能分的那天（关单次日起的第一个夜间 23:00）。
public class SharePlanDateTests
{
    private static OrderShareService Svc(ApplicationDBContext db) => new(db, ShareFixture.Config, ShareFixture.Http);
    private static string Day(int offsetFromToday) => DateTime.Today.AddDays(offsetFromToday).ToString("yyyy-MM-dd");
    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    [Fact]
    public void Earliest_plan_date_is_the_next_nightly_run_but_never_before_the_day_after_closing()
    {
        var closed = new DateTime(2026, 11, 1, 15, 42, 0);
        // 关单早就过了：今天 23:00 之前设，就是今晚；23:00 以后设，就是明晚
        Assert.Equal(new DateTime(2026, 11, 3), ShareDateRules.EarliestPlanDate(closed, new DateTime(2026, 11, 3, 12, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 3), ShareDateRules.EarliestPlanDate(closed, new DateTime(2026, 11, 3, 22, 59, 59)));
        Assert.Equal(new DateTime(2026, 11, 4), ShareDateRules.EarliestPlanDate(closed, new DateTime(2026, 11, 3, 23, 0, 0)));
        // 当天关单当天设：夜间任务只提交关单日 <= 昨天的，所以最早是次日
        var closedToday = new DateTime(2026, 11, 3, 21, 50, 0);
        Assert.Equal(new DateTime(2026, 11, 4), ShareDateRules.EarliestPlanDate(closedToday, new DateTime(2026, 11, 3, 22, 10, 0)));
    }

    [Fact]
    public void A_share_is_due_when_its_plan_date_is_no_later_than_the_day_the_job_runs()
    {
        var shareDate = new DateTime(2026, 11, 3);       // 夜间任务的 shareDate 是「昨天」，任务在 11-04 23:00 跑
        Assert.True(ShareDateRules.IsDue(null, shareDate));
        Assert.True(ShareDateRules.IsDue(new DateTime(2026, 11, 4), shareDate));
        Assert.True(ShareDateRules.IsDue(new DateTime(2026, 11, 2), shareDate));
        Assert.False(ShareDateRules.IsDue(new DateTime(2026, 11, 5), shareDate));
        Assert.False(ShareDateRules.IsDue(new DateTime(2026, 11, 4), new DateTime(2026, 11, 2)));
    }

    [Fact]
    public void Plan_date_text_must_be_yyyy_MM_dd()
    {
        Assert.True(ShareDateRules.TryParse("2026-11-04", out var ok));
        Assert.Equal(new DateTime(2026, 11, 4), ok);
        Assert.True(ShareDateRules.TryParse(" 2026-11-04 ", out _));
        foreach (var bad in new[] { "2026/11/04", "11-04", "2026-13-01", "abc", "", "  ", null })
            Assert.False(ShareDateRules.TryParse(bad, out _), bad ?? "null");
    }

    [Fact]
    public async Task Set_keeps_the_plan_date_and_the_list_shows_it()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "13691457006", aliBound: true);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, DateTime.Today.AddDays(3));

        Assert.Equal(Day(3), view.planDate);
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid);
        Assert.Equal(DateTime.Today.AddDays(3), share.plan_date);
        Assert.Equal(Day(3), (await Svc(db).ListAsync(1)).Single().planDate);
    }

    [Fact]
    public async Task Set_without_a_plan_date_leaves_it_empty()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "13691457006", aliBound: true);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null);

        Assert.Null(view.planDate);
    }

    [Fact]
    public async Task Set_rejects_a_plan_date_before_today_but_lets_an_unchanged_old_one_stay()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "13691457006", aliBound: true);
        var svc = Svc(db);

        var early = await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, DateTime.Today.AddDays(-1)));
        Assert.Equal(1, early.Code);
        Assert.Equal("预计分账日期不能早于今天", early.Message);

        await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, DateTime.Today.AddDays(2));
        // 过了几天日期已经过去，只改备注再保存时，原来的日期不算「早于今天」
        var row = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid);
        row.plan_date = DateTime.Today.AddDays(-2);
        db.orderShare.Update(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var again = await svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, "改备注", DateTime.Today.AddDays(-2));
        Assert.Equal(Day(-2), again.planDate);
        await Assert.ThrowsAsync<ShareException>(() => svc.SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, DateTime.Today.AddDays(-1)));
    }

    [Fact]
    public async Task Nightly_job_skips_introducer_shares_whose_plan_date_has_not_come()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);   // 关单 11-01，付款 id 4
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "万龙租赁分账", valid = true });
        db.orderShareRelation.Add(new OrderShareRelation { id = 9, type = "个人", name = "示例", valid = true });
        db.orderShare.Add(new OrderShare { id = 10, order_id = 1, relation_id = 9, amount = 40, valid = true, create_date = DateTime.Now, plan_date = new DateTime(2026, 11, 3) });
        db.orderShare.Add(new OrderShare { id = 11, order_id = 1, relation_id = 4, amount = 200, valid = true, create_date = DateTime.Now });
        db.paymentShare.Add(new PaymentShare { id = 1, payment_id = 4, share_id = 10, amount = 1, out_trade_no = "A_01" });
        db.paymentShare.Add(new PaymentShare { id = 2, payment_id = 4, share_id = 11, amount = 1, out_trade_no = "A_02" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);

        // 夜间任务 11-02 跑（shareDate = 11-01）：介绍人预计 11-03，还没到，只有万龙那条
        Assert.Equal(new[] { 2 }, (await helper.GetDueWanlongShares(new DateTime(2026, 11, 1))).Select(s => s.id).ToArray());
        // 11-03 跑（shareDate = 11-02）：到了，万龙在前
        Assert.Equal(new[] { 2, 1 }, (await helper.GetDueWanlongShares(new DateTime(2026, 11, 2))).Select(s => s.id).ToArray());
    }

    [Fact]
    public async Task Api_suggests_a_plan_date_and_takes_the_chosen_one()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 0, 400);
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "13691457006", aliBound: true);
        var ctl = () => new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http);

        var view = await ctl().GetOrderShares("manager-s", 1);
        string suggested = D(view).GetProperty("suggestedPlanDate").GetString()!;
        Assert.Contains(suggested, new[] { Day(0), Day(1) });   // 今晚或明晚，看现在是不是过了 23:00

        var set = await ctl().SetShare("manager-s", new(1, introducer.id, 40, "备注", Day(5)));
        Assert.Equal(0, set.code);
        Assert.Equal(Day(5), D(set).GetProperty("shares")[0].GetProperty("planDate").GetString());

        var badFormat = await ctl().SetShare("manager-s", new(1, introducer.id, 40, null, "2026/11/04"));
        Assert.Equal(1, badFormat.code);
        Assert.Equal("预计分账日期格式不对", badFormat.message);
        var early = await ctl().SetShare("manager-s", new(1, introducer.id, 40, null, Day(-1)));
        Assert.Equal(1, early.code);

        // 老版本小程序不传日期：照样能保存
        var legacy = await ctl().SetShare("manager-s", new(1, introducer.id, 41, null));
        Assert.Equal(0, legacy.code);
        Assert.Equal(JsonValueKind.Null, D(legacy).GetProperty("shares")[0].GetProperty("planDate").ValueKind);
    }
}

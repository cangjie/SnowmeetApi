using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class IntroducerControllersTests
{
    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    [Fact]
    public async Task Introducer_api_permissions_and_add()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await ShareFixture.AddStaffSessionAsync(db, 301, 300, "admin-s");
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await ShareFixture.AddStaffSessionAsync(db, 303, 100, "clerk-s");
        db.member.Add(new SnowmeetApi.Models.Member { id = 400, real_name = "示例 KOL" });
        db.memberSocialAccount.Add(new SnowmeetApi.Models.MemberSocialAccount { member_id = 400, type = "wechat_mini_openid", num = "oKOL", valid = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ctl = () => new IntroducerController(db, ShareFixture.Config, ShareFixture.Http);

        Assert.Equal(2, (await ctl().ListIntroducers("bad-session")).code);
        Assert.Equal(3, (await ctl().ListIntroducers("manager-s")).code);   // 店长不是系统管理员
        Assert.Equal(3, (await ctl().ListIntroducers("clerk-s")).code);
        Assert.Equal(0, (await ctl().ListIntroducers("admin-s")).code);

        var denied = await ctl().AddIntroducer("manager-s", new(400, "KOL", "小王", null, null, null, null));
        Assert.Equal(3, denied.code);
        var added = await ctl().AddIntroducer("admin-s", new(400, "KOL", "小王", null, null, null, null));
        Assert.Equal(0, added.code);
        Assert.Equal("已填写", D(added).GetProperty("wechat").GetString());
        var dup = await ctl().AddIntroducer("admin-s", new(400, "KOL", "小王", null, null, null, null));
        Assert.Equal(1, dup.code);
        Assert.Equal("这位会员已经是介绍人", dup.message);
    }

    [Fact]
    public async Task Choices_are_for_managers_and_only_list_valid_introducers()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await ShareFixture.AddStaffSessionAsync(db, 303, 100, "clerk-s");
        var a = await ShareFixture.AddIntroducerAsync(db, 100, "KOL");
        var b = await ShareFixture.AddIntroducerAsync(db, 101, "教练");
        var stopped = await db.introducer.AsNoTracking().SingleAsync(i => i.id == b.id);
        stopped.valid = 0;
        db.introducer.Update(stopped);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ctl = new IntroducerController(db, ShareFixture.Config, ShareFixture.Http);

        Assert.Equal(3, (await ctl.ListChoices("clerk-s")).code);
        var ok = await ctl.ListChoices("manager-s");
        Assert.Equal(0, ok.code);
        var choices = D(ok).GetProperty("choices");
        Assert.Equal(1, choices.GetArrayLength());
        Assert.Equal(a.id, choices[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Order_share_api_shows_the_state_and_sets_and_removes()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 500, 430, 400);
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await ShareFixture.AddStaffSessionAsync(db, 303, 100, "clerk-s");
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "userId", aliNum: "2088000000000001", aliBound: true);
        var ctl = () => new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http);

        Assert.Equal(2, (await ctl().GetOrderShares("bad", 1)).code);
        Assert.Equal(3, (await ctl().GetOrderShares("clerk-s", 1)).code);
        var view = await ctl().GetOrderShares("manager-s", 1);
        Assert.Equal(0, view.code);
        Assert.True(D(view).GetProperty("canEdit").GetBoolean());
        Assert.Equal(40, D(view).GetProperty("suggested").GetDouble(), 2);
        Assert.Equal(0, D(view).GetProperty("shares").GetArrayLength());

        var set = await ctl().SetShare("manager-s", new(1, introducer.id, 40, "备注"));
        Assert.Equal(0, set.code);
        var shares = D(set).GetProperty("shares");
        Assert.Equal(1, shares.GetArrayLength());
        Assert.Equal(40, shares[0].GetProperty("amount").GetDouble(), 2);
        Assert.Equal("待审核", shares[0].GetProperty("state").GetString());

        var tooMuch = await ctl().SetShare("manager-s", new(1, introducer.id, 9999, null));
        Assert.Equal(1, tooMuch.code);

        int orderShareId = shares[0].GetProperty("orderShareId").GetInt32();
        var removed = await ctl().RemoveShare("manager-s", new(orderShareId));
        Assert.Equal(0, removed.code);
        Assert.Equal(0, D(removed).GetProperty("shares").GetArrayLength());
    }

    [Fact]
    public async Task Order_share_api_allows_an_open_order_with_a_shareable_payment()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync();
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        order.closed = 0;
        order.close_date = null;
        db.order.Update(order);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var view = await new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http).GetOrderShares("manager-s", 1);

        Assert.Equal(0, view.code);
        Assert.True(D(view).GetProperty("canEdit").GetBoolean());
        Assert.True(D(view).GetProperty("channelShareAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, D(view).GetProperty("reason").ValueKind);
    }
}

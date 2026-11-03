using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 教练也是介绍人：有效的教练自动出现在介绍人名单和订单上的候选里，不用再一个个添加。
public class IntroducerCoachSyncTests
{
    private sealed class NoBinder : IAlipayRoyaltyBinder
    {
        public Task<(bool ok, string message)> BindAsync(string accountType, string account, string? realName) => Task.FromResult((true, ""));
    }

    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    private static async Task AddCoachAsync(ApplicationDBContext db, int coachId, int memberId, string displayName, string? openid = "oCOACH", int valid = 1)
    {
        db.member.Add(new Member { id = memberId, real_name = "真名" + memberId });
        if (openid != null)
        {
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId, type = MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, num = openid, valid = 1 });
        }
        db.coach.Add(new Coach { id = coachId, member_id = memberId, display_name = displayName, valid = valid });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Every_valid_coach_becomes_an_introducer_with_the_members_openid()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await AddCoachAsync(db, 1, 201, "苍教练", "oCANG");
        await AddCoachAsync(db, 2, 202, "周教练", "oZHOU");

        int created = await new IntroducerService(db, new NoBinder()).SyncCoachesAsync();

        Assert.Equal(2, created);
        var introducers = await db.introducer.AsNoTracking().OrderBy(i => i.coach_id).ToListAsync();
        Assert.Equal(new[] { "苍教练", "周教练" }, introducers.Select(i => i.name).ToArray());
        Assert.All(introducers, i => { Assert.Equal("教练", i.type); Assert.Equal(1, i.valid); Assert.Null(i.real_name); });
        Assert.Equal(new int?[] { 1, 2 }, introducers.Select(i => i.coach_id).ToArray());
        Assert.Equal(new[] { 201, 202 }, introducers.Select(i => i.member_id).ToArray());

        var relation = await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.id == introducers[1].relation_id);
        Assert.Equal("个人", relation.type);
        Assert.Equal("周教练", relation.name);
        Assert.Equal("PERSONAL_OPENID", relation.wepay_account_type);
        Assert.Equal("oZHOU", relation.wepay_account_num);
        Assert.Null(relation.ali_account_num);   // 支付宝账号要管理员以后补
    }

    [Fact]
    public async Task Syncing_again_adds_nothing()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await AddCoachAsync(db, 1, 201, "苍教练");
        var svc = new IntroducerService(db, new NoBinder());

        Assert.Equal(1, await svc.SyncCoachesAsync());
        Assert.Equal(0, await svc.SyncCoachesAsync());
        Assert.Equal(1, await db.introducer.AsNoTracking().CountAsync());
        Assert.Equal(1, await db.orderShareRelation.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Stopped_coaches_and_members_who_already_have_an_introducer_record_are_left_alone()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await AddCoachAsync(db, 1, 201, "停用的教练", valid: 0);
        await AddCoachAsync(db, 2, 202, "已经是 KOL 的教练");
        await AddCoachAsync(db, 3, 203, "被管理员停用过的教练");
        // 这两位会员已经建好了（上面是教练的会员），这里只补收款账号和介绍人记录
        async Task<Introducer> IntroducerForExistingMemberAsync(int memberId, string type, int valid)
        {
            var relation = new OrderShareRelation { type = "个人", name = "手工添加" + memberId, valid = true };
            db.orderShareRelation.Add(relation);
            await db.SaveChangesAsync();
            var introducer = new Introducer { member_id = memberId, type = type, name = "手工添加" + memberId, relation_id = relation.id, valid = valid };
            db.introducer.Add(introducer);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return introducer;
        }
        var kol = await IntroducerForExistingMemberAsync(202, "KOL", 1);
        var stoppedByAdmin = await IntroducerForExistingMemberAsync(203, "教练", 0);

        int created = await new IntroducerService(db, new NoBinder()).SyncCoachesAsync();

        Assert.Equal(0, created);
        Assert.Equal(2, await db.introducer.AsNoTracking().CountAsync());   // 没多出记录
        Assert.Equal("KOL", (await db.introducer.AsNoTracking().SingleAsync(i => i.id == kol.id)).type);
        Assert.Equal(0, (await db.introducer.AsNoTracking().SingleAsync(i => i.id == stoppedByAdmin.id)).valid);   // 管理员停用的不会被自动启用
    }

    [Fact]
    public async Task A_coach_without_an_openid_is_still_listed_but_has_no_wechat_account()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await AddCoachAsync(db, 1, 201, "没登录过小程序的教练", openid: null);

        Assert.Equal(1, await new IntroducerService(db, new NoBinder()).SyncCoachesAsync());

        var view = (await new IntroducerService(db, new NoBinder()).ListAsync(true)).Single();
        Assert.Equal("未填写", view.wechat);
        Assert.Equal("未填写", view.alipay);
    }

    [Fact]
    public async Task A_blank_display_name_falls_back_to_the_members_name()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await AddCoachAsync(db, 1, 201, "  ");

        await new IntroducerService(db, new NoBinder()).SyncCoachesAsync();

        Assert.Equal("真名201", (await db.introducer.AsNoTracking().SingleAsync()).name);
    }

    [Fact]
    public async Task The_choices_and_the_admin_list_include_the_coaches_without_adding_them_first()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await ShareFixture.AddStaffSessionAsync(db, 301, 300, "admin-s");
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await AddCoachAsync(db, 1, 201, "周教练", "oZHOU");
        var ctl = () => new IntroducerController(db, ShareFixture.Config, ShareFixture.Http);

        var choices = D(await ctl().ListChoices("manager-s")).GetProperty("choices");
        Assert.Equal(1, choices.GetArrayLength());
        Assert.Equal("周教练", choices[0].GetProperty("name").GetString());
        Assert.Equal("教练", choices[0].GetProperty("type").GetString());

        var listed = D(await ctl().ListIntroducers("admin-s")).GetProperty("introducers");
        Assert.Equal(1, listed.GetArrayLength());   // 同一个人，不会因为两个接口各同步一次而重复
        Assert.Equal("已填写", listed[0].GetProperty("wechat").GetString());
    }

    [Fact]
    public async Task A_coach_can_be_given_a_share_that_goes_out_automatically_on_a_wechat_order()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 0, 400);
        await AddCoachAsync(db, 1, 201, "周教练", "oZHOU");
        var svc = new IntroducerService(db, new NoBinder());
        await svc.SyncCoachesAsync();
        var introducer = (await svc.ListAsync(true)).Single();

        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null);

        Assert.Equal("待自动分账", view.state);
    }

    [Fact]
    public async Task On_an_alipay_order_a_coach_without_an_alipay_account_is_marked_manual_with_the_reason()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 1000, 0, 400);
        await AddCoachAsync(db, 1, 201, "周教练", "oZHOU");
        var svc = new IntroducerService(db, new NoBinder());
        await svc.SyncCoachesAsync();
        var introducer = (await svc.ListAsync(true)).Single();

        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null);

        Assert.Equal("手动处理", view.state);
        Assert.Equal("介绍人没有支付宝收款账号", view.memo);
    }
}

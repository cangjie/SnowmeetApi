using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

public class IntroducerServiceTests
{
    private sealed class FakeBinder : IAlipayRoyaltyBinder
    {
        public readonly List<(string type, string account, string? name)> Calls = new();
        public bool Ok = true;
        public string Message = "账号与姓名不一致";

        public Task<(bool ok, string message)> BindAsync(string accountType, string account, string? realName)
        {
            Calls.Add((accountType, account, realName));
            return Task.FromResult((Ok, Ok ? "" : Message));
        }
    }

    private static readonly IntroducerService.AccountInput NoAccount = new(null, null, null, null);

    private static async Task SeedMemberAsync(ApplicationDBContext db, int memberId, string? openid)
    {
        db.member.Add(new Member { id = memberId, real_name = "示例" + memberId });
        if (openid != null)
        {
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId,
                type = MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, num = openid, valid = 1 });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Add_personal_introducer_takes_the_members_mini_program_openid()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        await SeedMemberAsync(db, 101, null);
        var svc = new IntroducerService(db, new FakeBinder());

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "老王", null, NoAccount);

        var introducer = await db.introducer.AsNoTracking().SingleAsync(i => i.id == view.id);
        var relation = await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.id == introducer.relation_id);
        Assert.Equal("个人", relation.type);
        Assert.Equal("PERSONAL_OPENID", relation.wepay_account_type);
        Assert.Equal("oABC", relation.wepay_account_num);
        Assert.Equal("已填写", view.wechat);
        Assert.Equal("未填写", view.alipay);

        var noOpenid = await svc.AddAsync(ShareFixture.Manager(300), 101, "KOL", "小赵", null, NoAccount);
        Assert.Equal("未填写", noOpenid.wechat);   // 没用过小程序取不到 openid，仍然创建
    }

    [Fact]
    public async Task Add_organization_requires_merchant_id_and_name()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var svc = new IntroducerService(db, new FakeBinder());

        var ex = await Assert.ThrowsAsync<ShareException>(() =>
            svc.AddAsync(ShareFixture.Manager(300), 100, "机构", "某俱乐部", null, new("1600000001", null, null, null)));
        Assert.Equal(1, ex.Code);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "机构", "某俱乐部", null, new("1600000001", "某某滑雪俱乐部有限公司", null, null));
        var relation = await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.id ==
            db.introducer.Single(i => i.id == view.id).relation_id);
        Assert.Equal("公司", relation.type);
        Assert.Equal("MERCHANT_ID", relation.wepay_account_type);
        Assert.Equal("1600000001", relation.wepay_account_num);
        Assert.Equal("某某滑雪俱乐部有限公司", relation.wepay_account_name);
    }

    [Fact]
    public async Task Add_rejects_a_member_who_is_already_an_introducer_until_it_is_stopped()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var svc = new IntroducerService(db, new FakeBinder());
        var first = await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "老王", null, NoAccount);

        var ex = await Assert.ThrowsAsync<ShareException>(() => svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "老王", null, NoAccount));
        Assert.Equal("这位会员已经是介绍人", ex.Message);

        await svc.UpdateAsync(first.id, null, null, false, null);
        await svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "老王", null, NoAccount);
        var reactivate = await Assert.ThrowsAsync<ShareException>(() => svc.UpdateAsync(first.id, null, null, true, null));
        Assert.Equal(1, reactivate.Code);
    }

    [Fact]
    public async Task Alipay_login_name_needs_a_real_name_and_is_bound_on_save()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var binder = new FakeBinder();
        var svc = new IntroducerService(db, binder);

        await Assert.ThrowsAsync<ShareException>(() =>
            svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", null, new(null, null, "loginName", "13691457006")));
        Assert.Empty(binder.Calls);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", "张三", new(null, null, "loginName", "13691457006"));

        Assert.Equal(("loginName", "13691457006", "张三"), Assert.Single(binder.Calls));
        Assert.Equal("已绑定", view.alipay);
        var relationId = (await db.introducer.AsNoTracking().SingleAsync(i => i.id == view.id)).relation_id;
        var bind = await db.shareRelationBind.AsNoTracking().SingleAsync(b => b.share_relation_id == relationId);
        Assert.Equal("支付宝", bind.pay_method);
        Assert.True(bind.valid);
    }

    [Fact]
    public async Task A_failed_bind_keeps_the_introducer_and_can_be_retried()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var binder = new FakeBinder { Ok = false };
        var svc = new IntroducerService(db, binder);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", "张三", new(null, null, "loginName", "13691457006"));

        Assert.Equal("未绑定", view.alipay);
        Assert.Equal("账号与姓名不一致", view.alipayMessage);
        Assert.Empty(await db.shareRelationBind.AsNoTracking().ToListAsync());

        binder.Ok = true;
        var retried = await svc.BindAlipayAsync(view.id);
        Assert.Equal("已绑定", retried.alipay);
        Assert.Single(await db.shareRelationBind.AsNoTracking().ToListAsync());

        await svc.BindAlipayAsync(view.id);   // 再绑一次不重复写行
        Assert.Single(await db.shareRelationBind.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task List_can_hide_stopped_introducers()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oA");
        await SeedMemberAsync(db, 101, "oB");
        var svc = new IntroducerService(db, new FakeBinder());
        await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "甲", null, NoAccount);
        var b = await svc.AddAsync(ShareFixture.Manager(300), 101, "KOL", "乙", null, NoAccount);
        await svc.UpdateAsync(b.id, null, null, false, null);

        Assert.Equal(2, (await svc.ListAsync(false)).Count);
        Assert.Equal(new[] { "甲" }, (await svc.ListAsync(true)).Select(v => v.name).ToArray());
    }
}

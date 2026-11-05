using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Controllers.CoachPlatform;

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

    private static readonly IntroducerService.AccountInput NoAccount = new(null, null, null);

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

    [Theory]
    [InlineData("KOL")]
    [InlineData("教练")]
    [InlineData("机构")]
    public async Task New_distributor_gets_linked_platform_identity(string type)
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var account = type == "机构" ? new IntroducerService.AccountInput("1600000001", "机构商户", null) : NoAccount;
        var result = await new IntroducerService(db, new FakeBinder()).AddAsync(ShareFixture.Manager(300), 100, type, "分销商甲", null, account);
        var coach = await db.coach.AsNoTracking().SingleAsync(c => c.member_id == 100);
        Assert.Equal(1, coach.valid);
        Assert.Equal("分销商甲", coach.display_name);
        Assert.Equal(coach.id, result.coachId);
        db.miniSession.Add(new MiniSession { session_key = "distributor-session", session_type = "wechat_mini_openid", member_id = 100,
            wechat_openid = "oABC", valid = 1, expire_date = DateTime.Now.AddHours(1) });
        await db.SaveChangesAsync();
        await new CoachAdminService(db, ShareFixture.Config).SetPricingAsync(coach.id, new PricingInput(null, 0.9));
        var self = new CoachController(db, ShareFixture.Config);
        Assert.Equal(0, (await self.GetMyCoach("distributor-session")).code);
        Assert.Equal(0, (await self.CreateCard("distributor-session")).code);
        Assert.Single(await db.coachCard.AsNoTracking().Where(c => c.coach_id == coach.id).ToListAsync());
    }

    [Fact]
    public async Task Editing_distributor_name_and_status_updates_linked_platform_identity()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var svc = new IntroducerService(db, new FakeBinder());
        var result = await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "甲", null, NoAccount);
        await svc.UpdateAsync(result.id, "新名字", null, false, null);
        var coach = await db.coach.AsNoTracking().SingleAsync(c => c.member_id == 100);
        Assert.Equal("新名字", coach.display_name);
        Assert.Equal(0, coach.valid);
    }

    [Fact]
    public async Task Distributor_list_exposes_only_the_phone_tail_for_search()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        db.memberSocialAccount.Add(new MemberSocialAccount { member_id = 100, type = MemberSocialAccount.TYPE_CELL, num = "13800001234", valid = 1 });
        await db.SaveChangesAsync();
        var svc = new IntroducerService(db, new FakeBinder());
        await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "甲", null, NoAccount);
        var json = System.Text.Json.JsonSerializer.Serialize((await svc.ListAsync(false)).Single());
        using var item = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("1234", item.RootElement.GetProperty("phoneTail").GetString());
        Assert.DoesNotContain("13800001234", json);
    }

    [Fact]
    public async Task Changing_ali_real_name_rebinds_the_existing_account()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var binder = new FakeBinder();
        var svc = new IntroducerService(db, binder);
        var added = await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "甲", "旧姓名", new(null, null, "13800001234"));
        var edited = await svc.UpdateAsync(added.id, null, "新姓名", null, NoAccount);
        Assert.Equal(2, binder.Calls.Count);
        Assert.Equal(("loginName", "13800001234", "新姓名"), binder.Calls.Last());
        Assert.Equal("已绑定", edited.alipay);
    }

    [Fact]
    public async Task Changing_account_preserves_existing_share_payee_and_history()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 1000, 0, 400);
        await SeedMemberAsync(db, 100, "oABC");
        var svc = new IntroducerService(db, new FakeBinder());
        var added = await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "甲", null, NoAccount);
        var old = await db.introducer.AsNoTracking().SingleAsync(i => i.id == added.id);
        var share = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, added.id, 40, null);
        await svc.UpdateAsync(added.id, null, "真实姓名", null, new(null, null, "13800001234"));
        var current = await db.introducer.AsNoTracking().SingleAsync(i => i.id == added.id);
        Assert.NotEqual(old.relation_id, current.relation_id);
        Assert.Null((await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.id == old.relation_id)).ali_account_num);
        Assert.Equal(old.relation_id, (await db.orderShare.AsNoTracking().SingleAsync(s => s.id == share.orderShareId)).relation_id);
        var query = ShareListServiceTests.Q() with { DistributorOnly = true };
        Assert.Contains((await new ShareListService(db).ListAsync(query)).items, i => i.orderShareId == share.orderShareId);
        Assert.Contains(await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http).ListAsync(1), i => i.orderShareId == share.orderShareId);
        await Assert.ThrowsAsync<ShareException>(() => new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, added.id, 50, null));
    }

    [Fact]
    public async Task Changing_ali_identity_keeps_old_real_name_in_history()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var svc = new IntroducerService(db, new FakeBinder());
        var added = await svc.AddAsync(ShareFixture.Manager(300), 100, "KOL", "甲", "原姓名", new(null, null, "13800001234"));
        await svc.UpdateAsync(added.id, null, "新姓名", null, NoAccount);
        var history = await db.introducerRelationHistory.AsNoTracking().SingleAsync(h => h.introducer_id == added.id);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(history));
        Assert.True(json.RootElement.TryGetProperty("real_name", out var oldName));
        Assert.Equal("原姓名", oldName.GetString());
    }

    [Fact]
    public async Task Changing_organization_wechat_keeps_old_bind_and_preserves_unchanged_alipay_bind()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var svc = new IntroducerService(db, new FakeBinder());
        var added = await svc.AddAsync(ShareFixture.Manager(300), 100, "机构", "机构", "法人", new("1600000001", "旧商户", "13800001234"));
        int oldId = (await db.introducer.AsNoTracking().SingleAsync(i => i.id == added.id)).relation_id;
        db.shareRelationBind.Add(new ShareRelationBind { share_relation_id = oldId, pay_method = "微信支付", wepay_key_id = 7, valid = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await svc.UpdateAsync(added.id, null, null, null, new("1600000002", "新商户", null));
        int newId = (await db.introducer.AsNoTracking().SingleAsync(i => i.id == added.id)).relation_id;
        Assert.NotEqual(oldId, newId);
        Assert.Equal("1600000001", (await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.id == oldId)).wepay_account_num);
        Assert.Equal("1600000002", (await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.id == newId)).wepay_account_num);
        Assert.True(await db.shareRelationBind.AsNoTracking().AnyAsync(b => b.share_relation_id == oldId && b.pay_method == "微信支付" && b.valid));
        Assert.False(await db.shareRelationBind.AsNoTracking().AnyAsync(b => b.share_relation_id == newId && b.pay_method == "微信支付" && b.valid));
        Assert.True(await db.shareRelationBind.AsNoTracking().AnyAsync(b => b.share_relation_id == newId && b.pay_method == "支付宝" && b.valid));
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
            svc.AddAsync(ShareFixture.Manager(300), 100, "机构", "某俱乐部", null, new("1600000001", null, null)));
        Assert.Equal(1, ex.Code);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "机构", "某俱乐部", null, new("1600000001", "某某滑雪俱乐部有限公司", null));
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
            svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", null, new(null, null, "13691457006")));
        Assert.Empty(binder.Calls);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", "张三", new(null, null, "13691457006"));

        Assert.Equal(("loginName", "13691457006", "张三"), Assert.Single(binder.Calls));
        Assert.Equal("已绑定", view.alipay);
        var relationId = (await db.introducer.AsNoTracking().SingleAsync(i => i.id == view.id)).relation_id;
        var bind = await db.shareRelationBind.AsNoTracking().SingleAsync(b => b.share_relation_id == relationId);
        Assert.Equal("支付宝", bind.pay_method);
        Assert.True(bind.valid);
    }

    [Fact]
    public async Task Alipay_login_name_must_be_a_phone_number_or_an_email()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var binder = new FakeBinder();
        var svc = new IntroducerService(db, binder);

        foreach (var bad in new[] { "2088000000000001", "1369145700", "abc", "a@b", "13691457006x" })
        {
            var ex = await Assert.ThrowsAsync<ShareException>(() =>
                svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", "张三", new(null, null, bad)));
            Assert.Equal("支付宝登录号要填手机号或邮箱", ex.Message);
        }
        Assert.Empty(binder.Calls);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", "张三", new(null, null, " zhang.san@example.com "));
        Assert.Equal(("loginName", "zhang.san@example.com", "张三"), Assert.Single(binder.Calls));
        Assert.Equal("已绑定", view.alipay);
        var relation = await db.orderShareRelation.AsNoTracking().SingleAsync(r => r.ali_account_num == "zhang.san@example.com");
        Assert.Equal("loginName", relation.ali_account_type);
    }

    [Fact]
    public async Task A_failed_bind_keeps_the_introducer_and_can_be_retried()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await SeedMemberAsync(db, 100, "oABC");
        var binder = new FakeBinder { Ok = false };
        var svc = new IntroducerService(db, binder);

        var view = await svc.AddAsync(ShareFixture.Manager(300), 100, "教练", "张三", "张三", new(null, null, "13691457006"));

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

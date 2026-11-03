using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 分账请求里的「是否自动分账」：店长可以把某条设成手动（只记账，夜间任务不提交，自己去转账）；
// 默认自动，但系统判断必须手动的（用券、没收款账号、合计超门槛）仍然是手动，原因写在备注里。
public class ShareAutoChoiceTests
{
    private const string ChosenMemo = "已设为手动分账，不自动分账";

    private static OrderShareService Svc(ApplicationDBContext db) => new(db, ShareFixture.Config, ShareFixture.Http);
    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    private static Task<List<PaymentShare>> RecordsAsync(ApplicationDBContext db, int introducerRelationId)
        => db.paymentShare.AsNoTracking().Where(r => r.valid && r.orderShare.relation_id == introducerRelationId).ToListAsync();

    [Fact]
    public async Task Choosing_manual_marks_the_share_manual_and_says_why()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, null, autoShare: false);

        Assert.Equal("手动处理", view.state);
        Assert.Equal(ChosenMemo, view.memo);
        var record = Assert.Single(await RecordsAsync(db, introducer.relation_id));
        Assert.True(record.can_not_share);
        Assert.False((await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid)).auto_share);
    }

    [Fact]
    public async Task The_manual_choice_keeps_the_managers_own_note()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, "月底统一转", null, autoShare: false);

        Assert.Equal("月底统一转；" + ChosenMemo, view.memo);
    }

    [Fact]
    public async Task Saving_again_as_automatic_turns_it_back_into_an_automatic_share()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, null, autoShare: false);

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, null, autoShare: true);

        Assert.Equal("待自动分账", view.state);
        Assert.Null(view.memo);
        Assert.False(Assert.Single(await RecordsAsync(db, introducer.relation_id)).can_not_share);
    }

    [Fact]
    public async Task The_manual_choice_survives_when_another_share_is_added_later()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 0, 400);
        var manual = await ShareFixture.AddIntroducerAsync(db, 100);
        var auto = await ShareFixture.AddIntroducerAsync(db, 101);
        await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, manual.id, 40, null, null, autoShare: false);

        await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, auto.id, 30, null);   // 触发整单重算

        var shares = await Svc(db).ListAsync(1);
        Assert.Equal("手动处理", shares.Single(s => s.introducerId == manual.id).state);
        Assert.Equal("待自动分账", shares.Single(s => s.introducerId == auto.id).state);
    }

    [Fact]
    public async Task Automatic_is_still_manual_when_the_system_says_it_must_be()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 1000, 0, 400);
        var noAlipay = await ShareFixture.AddIntroducerAsync(db);   // 没有支付宝账号

        var view = await Svc(db).SetIntroducerShareAsync(ShareFixture.Manager(), 1, noAlipay.id, 40, null, null, autoShare: true);

        Assert.Equal("手动处理", view.state);
        Assert.Equal("介绍人没有支付宝收款账号", view.memo);
    }

    [Fact]
    public async Task Api_takes_the_choice_and_tells_the_client_it_is_supported()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 0, 400);
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var ctl = () => new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http);

        Assert.True(D(await ctl().GetOrderShares("manager-s", 1)).GetProperty("autoShareSupported").GetBoolean());

        var manual = await ctl().SetShare("manager-s", new(1, introducer.id, 40, null, null, false));
        Assert.Equal("手动处理", D(manual).GetProperty("shares")[0].GetProperty("state").GetString());
        var auto = await ctl().SetShare("manager-s", new(1, introducer.id, 40, null));   // 老版本小程序不传：按自动
        Assert.Equal("待自动分账", D(auto).GetProperty("shares")[0].GetProperty("state").GetString());
    }
}

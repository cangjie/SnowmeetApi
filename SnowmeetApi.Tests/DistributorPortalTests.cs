using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Tests;

// 小程序「我的」里的分销商菜单区域（教练 / 机构）：身份判定、收款资料只读（脱敏）、订票二维码只认登录态。
public class DistributorPortalTests
{
    private static async Task AddSessionAsync(ApplicationDBContext db, int memberId, string sessionKey)
    {
        db.miniSession.Add(new MiniSession { session_key = sessionKey, session_type = "wechat_mini_openid", member_id = memberId,
            valid = 1, expire_date = DateTime.Now.AddDays(1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static JsonElement Json(object? data) => JsonSerializer.SerializeToElement(data);

    [Theory]
    [InlineData("教练")]
    [InlineData("机构")]
    public async Task Coach_and_organization_get_the_menu_with_their_entries(string type)
    {
        await using var db = await ShareFixture.NewDbAsync();
        var intro = await ShareFixture.AddIntroducerAsync(db, 301, type);
        await AddSessionAsync(db, 301, "sk-301");
        var result = await new DistributorController(db, ShareFixture.Config).GetMyDistributor("sk-301");
        Assert.Equal(0, result.code);
        var data = Json(result.data);
        Assert.True(data.GetProperty("isDistributor").GetBoolean());
        Assert.Equal(type, data.GetProperty("distributor").GetProperty("type").GetString());
        Assert.Equal(intro.id, data.GetProperty("distributor").GetProperty("id").GetInt32());
        // 机构多一个「制作会员卡」；教练的会员卡价格由管理员设置
        string[] expected = type == "机构"
            ? new[] { "qrcode", "customers", "makeCard", "makeCoupon", "cards", "coupons", "payee" }
            : new[] { "qrcode", "customers", "cards", "coupons", "payee" };
        Assert.Equal(expected, data.GetProperty("entries").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task Kol_stopped_and_unrelated_members_do_not_get_the_menu()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await ShareFixture.AddIntroducerAsync(db, 302, "KOL");
        var stopped = await ShareFixture.AddIntroducerAsync(db, 303, "教练");
        var row = await db.introducer.AsNoTracking().SingleAsync(i => i.id == stopped.id);
        row.valid = 0;
        db.introducer.Update(row);
        db.member.Add(new Member { id = 304 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        foreach (int id in new[] { 302, 303, 304 }) await AddSessionAsync(db, id, "sk-" + id);
        var ctl = new DistributorController(db, ShareFixture.Config);
        foreach (int id in new[] { 302, 303, 304 })
        {
            var result = await ctl.GetMyDistributor("sk-" + id);
            Assert.Equal(0, result.code);
            Assert.False(Json(result.data).GetProperty("isDistributor").GetBoolean());
        }
        Assert.Equal(2, (await ctl.GetMyDistributor("nope")).code);
    }

    [Fact]
    public async Task Payee_is_read_only_and_masks_the_alipay_login_and_real_name()
    {
        await using var db = await ShareFixture.NewDbAsync();
        var intro = await ShareFixture.AddIntroducerAsync(db, 305, "教练", aliType: "loginName", aliNum: "13691457006", aliBound: true);
        var row = await db.introducer.AsNoTracking().SingleAsync(i => i.id == intro.id);
        row.real_name = "周小明";
        db.introducer.Update(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await AddSessionAsync(db, 305, "sk-305");
        var result = await new DistributorController(db, ShareFixture.Config).GetPayee("sk-305");
        Assert.Equal(0, result.code);
        string text = JsonSerializer.Serialize(result.data);
        Assert.DoesNotContain("13691457006", text);
        Assert.DoesNotContain("oINTRO", text);
        var alipay = Json(result.data).GetProperty("alipay");
        Assert.True(alipay.GetProperty("filled").GetBoolean());
        Assert.True(alipay.GetProperty("bound").GetBoolean());
        Assert.Equal("136****7006", alipay.GetProperty("loginName").GetString());
        Assert.Equal("周**", alipay.GetProperty("realName").GetString());
        Assert.True(Json(result.data).GetProperty("wechat").GetProperty("filled").GetBoolean());
    }

    [Fact]
    public async Task Payee_without_an_alipay_account_says_not_filled()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await ShareFixture.AddIntroducerAsync(db, 306, "机构");
        await AddSessionAsync(db, 306, "sk-306");
        var alipay = Json((await new DistributorController(db, ShareFixture.Config).GetPayee("sk-306")).data).GetProperty("alipay");
        Assert.False(alipay.GetProperty("filled").GetBoolean());
        Assert.False(alipay.GetProperty("bound").GetBoolean());
    }

    [Fact]
    public async Task Qr_code_encodes_the_logged_in_distributor_and_refuses_everyone_else()
    {
        await using var db = await ShareFixture.NewDbAsync();
        var intro = await ShareFixture.AddIntroducerAsync(db, 307, "机构");
        await ShareFixture.AddIntroducerAsync(db, 308, "KOL");
        await AddSessionAsync(db, 307, "sk-307");
        await AddSessionAsync(db, 308, "sk-308");
        var handler = new MiniTicketQrTests.Handler();
        var qr = new MiniStaffBookingQrService(new MiniTicketQrTests.Tokens(), new HttpClient(handler));
        var ctl = new DistributorController(db, ShareFixture.Config);

        Assert.IsType<BadRequestObjectResult>(await ctl.GetQrCode("sk-308", qr));   // KOL 暂无入口
        Assert.IsType<BadRequestObjectResult>(await ctl.GetQrCode("nope", qr));
        Assert.Equal(0, handler.Calls);

        var response = Assert.IsType<FileContentResult>(await ctl.GetQrCode("sk-307", qr));
        Assert.Equal("image/png", response.ContentType);
        var body = JsonDocument.Parse(handler.Requests.Single()).RootElement;
        Assert.Equal("d=" + intro.id, body.GetProperty("scene").GetString());
        Assert.Equal("pages/ski_pass/ski_pass_selector", body.GetProperty("page").GetString());
    }

    [Theory]
    [InlineData("13691457006", "136****7006")]
    [InlineData("abc@example.com", "a***@example.com")]
    [InlineData("loginname01", "***me01")]
    [InlineData("", "")]
    public void Login_names_are_masked(string input, string expected)
        => Assert.Equal(expected, DistributorMenuRules.MaskLoginName(input));
}

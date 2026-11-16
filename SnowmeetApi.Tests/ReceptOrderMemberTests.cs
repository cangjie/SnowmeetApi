using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

public class ReceptOrderMemberTests
{
    private static HttpContextAccessor Http() => new() { HttpContext = new DefaultHttpContext() };

    private static async Task<int> Scan(CoachFixture f, int staffId = 9, string openId = "mini-new", string? unionId = "union-new")
    {
        var q = new ScanQrCode { staff_id = staffId, code = ReceptCheckinRules.BuildCode("abcdefghij", 77),
            platform = "wechat_mini", scene = "店铺接待", purpose = "顾客扫码进小程序",
            scaned = 1, scaner_openid = openId, scaner_unionid = unionId, scan_time = DateTime.Now.AddMinutes(-1) };
        f.Db.scanQrCode.Add(q);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return q.id;
    }

    private static ApiResult<Order?> Result(ActionResult<ApiResult<Order?>> r) =>
        Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task LegacyMiniPostsWithoutCheckinParameter_StillCreateOrdersNormally()
    {
        await using var f = await CoachFixture.Create();
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<SnowmeetApi.Data.ApplicationDBContext>(o => o.UseSqlite(f.Connection.ConnectionString)
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(f.Config);
                services.AddHttpContextAccessor();
                services.AddControllers().AddApplicationPart(typeof(CareController).Assembly)
                    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
            })
            .Configure(app => { app.UseRouting(); app.UseEndpoints(e => e.MapControllers()); }));
        using var client = server.CreateClient();
        var posts = new (string path, string body)[] {
            ("/api/Care/SaveCareRecept?sessionKey=session-1", "{\"shop\":\"万龙服务中心\",\"type\":\"养护\",\"cares\":[{\"equipment\":\"双板\"}]}"),
            ("/api/Rent/SaveRentRecept?sessionKey=session-1", "{\"shop\":\"万龙服务中心\",\"type\":\"租赁\",\"rentals\":[{\"name\":\"租赁\"}]}"),
            ("/api/Order/PlaceOrder?sessionKey=session-1", "{\"shop\":\"万龙服务中心\",\"type\":\"零售\",\"total_amount\":100,\"paying_amount\":100,\"retails\":[{\"order_type\":\"普通\",\"deal_price\":100}]}")
        };
        foreach (var (path, body) in posts)
        {
            using var response = await client.PostAsync(path, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, path + " HTTP " + response.StatusCode + ": " + text);
            using var json = JsonDocument.Parse(text);
            Assert.Equal(0, json.RootElement.GetProperty("code").GetInt32());
            var order = json.RootElement.GetProperty("data");
            Assert.True(order.GetProperty("id").GetInt32() > 0);
            Assert.Equal(JsonValueKind.Null, order.GetProperty("member_id").ValueKind);
            Assert.False(order.GetProperty("wechat_unverified").GetBoolean());
        }
    }

    [Fact]
    public async Task CareDraft_CreatesMemberAndLinksScanAndSession_WithoutVerifyingPhone_AndPreservesOnResave()
    {
        await using var f = await CoachFixture.Create();
        int scan = await Scan(f);
        var input = new Order { shop = "万龙服务中心", cares = new List<Care> { new() { equipment = "双板" } } };
        var created = Result(await new CareController(f.NewContext(), f.Config, Http()).SaveCareRecept(input, "session-1", receptCheckinId: scan));
        Assert.Equal(0, created.code);
        var order = created.data!;
        Assert.NotNull(order.member_id);
        int id = order.member_id!.Value;
        Assert.False(order.wechat_unverified);
        var storedMember = await f.NewContext().member.AsNoTracking().SingleAsync(m => m.id == id);
        Assert.Equal(0, storedMember.is_staff);
        Assert.Equal(0, storedMember.is_manager);
        Assert.Equal(0, storedMember.is_admin);
        Assert.Equal(id, (await f.NewContext().scanQrCode.SingleAsync(q => q.id == scan)).scaner_member_id);
        Assert.Equal(id, (await f.NewContext().miniSession.SingleAsync(s => s.session_key == "session-new")).member_id);
        var accounts = await f.NewContext().memberSocialAccount.Where(a => a.member_id == id && a.valid == 1).ToListAsync();
        Assert.Equal(2, accounts.Count);
        Assert.DoesNotContain(accounts, a => a.type == "cell");
        Assert.Single(await f.NewContext().coreDataModLog.Where(l => l.key_value == order.id && l.field_name == "member_id").ToListAsync());

        order.member_id = null; // 请求在创建前就已排队，不能冲掉已关联的会员。
        var again = Result(await new CareController(f.NewContext(), f.Config, Http()).SaveCareRecept(order, "session-1", receptCheckinId: scan));
        Assert.Equal(0, again.code);
        Assert.Equal(id, (await f.NewContext().order.SingleAsync(o => o.id == order.id)).member_id);
        Assert.Single(await f.NewContext().memberSocialAccount.Where(a => a.type == "wechat_mini_openid" && a.num == "mini-new" && a.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task RentDraft_ReusesUnionIdMemberAndAddsOpenId()
    {
        await using var f = await CoachFixture.Create();
        f.Db.memberSocialAccount.Add(new MemberSocialAccount { member_id = 3, type = "wechat_unionid", num = "union-new", valid = 1 });
        await f.Db.SaveChangesAsync();
        int scan = await Scan(f);
        var input = new Order { shop = "万龙服务中心", type = "租赁", rentals = new List<Rental> { new() { name = "租赁" } } };
        var saved = Result(await new RentController(f.NewContext(), f.Config, Http()).SaveRentRecept(input, "session-1", receptCheckinId: scan));
        Assert.Equal(0, saved.code);
        Assert.Equal(3, saved.data!.member_id);
        Assert.False(saved.data.wechat_unverified);
        Assert.Single(await f.NewContext().memberSocialAccount.Where(a => a.member_id == 3 && a.num == "mini-new" && a.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task RetailOrder_AssociatesTheScannedCustomerOnInsertion()
    {
        await using var f = await CoachFixture.Create();
        int scan = await Scan(f);
        var input = new Order { shop = "万龙服务中心", type = "零售", total_amount = 100, paying_amount = 100,
            retails = new List<Retail> { new() { deal_price = 100 } } };
        var result = Result(await new OrderController(f.NewContext(), f.Config, Http()).PlaceOrder(input, "session-1", receptCheckinId: scan));
        Assert.Equal(0, result.code);
        Assert.NotNull(result.data!.member_id);
        Assert.Equal(result.data.member_id, (await f.NewContext().scanQrCode.SingleAsync(q => q.id == scan)).scaner_member_id);
        Assert.False(result.data.wechat_unverified);
        Assert.Single(await f.NewContext().coreDataModLog.Where(l => l.key_value == result.data.id && l.field_name == "member_id").ToListAsync());
        var payment = new OrderPayment { order_id = result.data.id, amount = 100, pay_method = "微信支付", status = "待支付", valid = 1 };
        f.Db.orderPayment.Add(payment);
        await f.Db.SaveChangesAsync();
        var identityResponse = await new PaymentIdentityController(f.NewContext(), f.Config)
            .CheckPayerIdentity(payment.id, "wechat", "", "session-new");
        var identity = Assert.IsType<ApiResult<PaymentIdentityController.CheckPayerIdentityResult>>(
            Assert.IsType<OkObjectResult>(identityResponse.Result).Value);
        Assert.Equal("direct", identity.data!.status);
        Assert.False(identity.data.scannerHasCell);
    }

    [Fact]
    public async Task RollingBackTheOrderTransactionAlsoRollsBackMemberAndBindings()
    {
        await using var f = await CoachFixture.Create();
        int scan = await Scan(f);
        await using (var db = f.NewContext())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var order = new Order { shop = "万龙服务中心" };
            Assert.True(await new ReceptOrderMemberService(db).PrepareAsync(order, 9, scan));
            await tx.RollbackAsync();
        }
        Assert.Null((await f.NewContext().scanQrCode.SingleAsync(q => q.id == scan)).scaner_member_id);
        Assert.Null((await f.NewContext().miniSession.SingleAsync(s => s.session_key == "session-new")).member_id);
        Assert.Empty(await f.NewContext().memberSocialAccount.Where(a => a.num == "mini-new").ToListAsync());
    }

    [Fact]
    public async Task RejectsAnotherStaffsScan_AndDoesNotCreateOrderOrMember()
    {
        await using var f = await CoachFixture.Create();
        int scan = await Scan(f, staffId: 10);
        var input = new Order { shop = "万龙服务中心", cares = new List<Care> { new() { equipment = "双板" } } };
        var response = Result(await new CareController(f.NewContext(), f.Config, Http()).SaveCareRecept(input, "session-1", receptCheckinId: scan));
        Assert.Equal(1, response.code);
        Assert.Empty(await f.NewContext().order.ToListAsync());
        Assert.Empty(await f.NewContext().memberSocialAccount.Where(a => a.num == "mini-new").ToListAsync());
    }

    [Fact]
    public async Task DifferentExistingOpenAndUnionMembers_AreNotMergedOrReassigned()
    {
        await using var f = await CoachFixture.Create();
        f.Db.memberSocialAccount.Add(new MemberSocialAccount { member_id = 3, type = "wechat_mini_openid", num = "mini-new", valid = 1 });
        f.Db.memberSocialAccount.Add(new MemberSocialAccount { member_id = 4, type = "wechat_unionid", num = "union-new", valid = 1 });
        await f.Db.SaveChangesAsync();
        int scan = await Scan(f);
        await using var db = f.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = new Order { shop = "万龙服务中心" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReceptOrderMemberService(db).PrepareAsync(order, 9, scan));
        Assert.Null(order.member_id);
    }
}

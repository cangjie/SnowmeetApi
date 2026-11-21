using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Tests;

public class MemberWechatBindTests
{
    private sealed class Tokens : IMiniTicketTokenProvider
    {
        public Task<string> GetTokenAsync(bool forceRefresh = false) => Task.FromResult("token");
    }

    private sealed class Handler(string phone, string country = "86") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/wxa/business/getuserphonenumber", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"errcode\":0,\"phone_info\":{\"countryCode\":\"" + country + "\",\"purePhoneNumber\":\"" + phone + "\"}}",
                    Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class Clients(string phone, string country = "86") : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(phone, country));
    }
    private sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"errcode\":40029,\"errmsg\":\"invalid code\"}")
            });
    }
    private sealed class ErrorClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ErrorHandler());
    }

    private static ApiResult<object> Result(ActionResult<ApiResult<object>> action) =>
        (ApiResult<object>)((OkObjectResult)action.Result!).Value!;

    private static async Task<(CoachFixture fixture, int id, string token)> Setup()
    {
        var f = await CoachFixture.Create(withPricing: false);
        var account = await f.Db.memberSocialAccount.AsTracking().SingleAsync(a => a.member_id == CoachFixture.StudentA && a.type == "wechat_mini_openid");
        account.valid = 0;
        await f.Db.SaveChangesAsync();
        var create = Result(await new MemberWechatBindController(f.NewContext()).Create(CoachFixture.StudentA, "session-7"));
        Assert.Equal(0, create.code);
        var qr = await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.scene == "会员绑定微信");
        return (f, qr.id, qr.code);
    }

    private static Task<ActionResult<ApiResult<object>>> Bind(CoachFixture f, string token, string phone, string session = "session-new") =>
        new MemberWechatBindController(f.NewContext()).Bind(new() { token = token, phoneCode = "phone-code" },
            session, new Tokens(), new Clients(phone));

    [Fact]
    public async Task VerifiedPhoneBindsOnceAndUpdatesSession()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            Assert.Equal(0, Result(await Bind(f, token, "13800001003")).code);
            Assert.Equal(CoachFixture.StudentA, (await f.Db.miniSession.AsNoTracking().SingleAsync(s => s.session_key == "session-new")).member_id);
            Assert.True(await f.Db.memberSocialAccount.AsNoTracking().AnyAsync(a =>
                a.member_id == CoachFixture.StudentA && a.type == "wechat_mini_openid" && a.num == "mini-new" && a.valid == 1));
            Assert.Equal(1, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);
            Assert.Equal(1, Result(await Bind(f, token, "13800001003")).code);
            Assert.True(await f.Db.coreDataModLog.AsNoTracking().AnyAsync(l => l.key_value == CoachFixture.StudentA && l.scene == "会员扫码绑定微信"));
        }
    }

    [Fact]
    public async Task WrongPhoneAndExistingMemberAreRejectedWithoutBinding()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            Assert.Contains("手机号", Result(await Bind(f, token, "13800001004")).message);
            Assert.Contains("其他会员", Result(await Bind(f, token, "13800001003", "session-4")).message);
            Assert.Equal(0, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);
            Assert.False(await f.Db.memberSocialAccount.AsNoTracking().AnyAsync(a =>
                a.member_id == CoachFixture.StudentA && a.type == "wechat_mini_openid" && a.valid == 1));
        }
    }

    [Fact]
    public async Task ExpiredCodeAndUnauthorizedStaffAreRejected()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            var qr = await f.Db.scanQrCode.AsTracking().SingleAsync(q => q.id == id);
            qr.expire_time = DateTime.Now.AddSeconds(-1);
            await f.Db.SaveChangesAsync();
            Assert.Contains("失效", Result(await Bind(f, token, "13800001003")).message);
            Assert.Equal(1, Result(await new MemberWechatBindController(f.NewContext()).Create(CoachFixture.StudentA, "session-3")).code);
        }
    }

    [Fact]
    public async Task ReplacingAndCancellingCodePreventsUse()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            Assert.Equal(0, Result(await new MemberWechatBindController(f.NewContext()).Create(CoachFixture.StudentA, "session-7")).code);
            Assert.Equal(1, Result(await Bind(f, token, "13800001003")).code);
            var replacement = await f.Db.scanQrCode.AsNoTracking().Where(q => q.id != id).SingleAsync();
            Assert.Equal(1, Result(await new MemberWechatBindController(f.NewContext()).Cancel(replacement.id, "session-1")).code);
            Assert.Equal(0, Result(await new MemberWechatBindController(f.NewContext()).Cancel(replacement.id, "session-7")).code);
            Assert.Equal(1, Result(await Bind(f, replacement.code, "13800001003")).code);
        }
    }

    [Fact]
    public async Task DifferentUnionIdentityOnTargetIsRejected()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            f.Db.memberSocialAccount.Add(new MemberSocialAccount { member_id = CoachFixture.StudentA,
                type = "wechat_unionid", num = "other-union", valid = 1 });
            await f.Db.SaveChangesAsync();
            Assert.Contains("其他微信", Result(await Bind(f, token, "13800001003")).message);
            Assert.Equal(0, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);
        }
    }

    [Fact]
    public async Task BindingQrUsesDedicatedPageAndOneTimeToken()
    {
        var handler = new MiniTicketQrTests.Handler();
        var service = new MiniStaffBookingQrService(new Tokens(), new HttpClient(handler));
        await service.GetForMemberWechatBindAsync("1234567890abcdef12345678", "trial");
        using var request = JsonDocument.Parse(handler.Requests.Single());
        Assert.Equal("pages/member/wechat_bind", request.RootElement.GetProperty("page").GetString());
        Assert.Equal("1234567890abcdef12345678", request.RootElement.GetProperty("scene").GetString());
        Assert.False(request.RootElement.GetProperty("check_path").GetBoolean());
    }

    [Fact]
    public async Task ForeignCountryCannotMatchAnUnqualifiedChinesePhone()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            var result = Result(await new MemberWechatBindController(f.NewContext()).Bind(
                new() { token = token, phoneCode = "phone-code" }, "session-new",
                new Tokens(), new Clients("13800001003", "1")));
            Assert.Contains("手机号", result.message);
            Assert.Equal(0, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);
        }
    }

    [Fact]
    public async Task RejectedWechatPhoneCodeDoesNotBind()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            var result = Result(await new MemberWechatBindController(f.NewContext()).Bind(
                new() { token = token, phoneCode = "expired" }, "session-new", new Tokens(), new ErrorClients()));
            Assert.Contains("授权验证失败", result.message);
            Assert.Equal(0, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);
        }
    }
}

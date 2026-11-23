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

    // 2026-11-23：顾客授权手机号后绑定接口 500。根因：向微信 getuserphonenumber 发请求时用了 PostAsJsonAsync，
    // 它的请求体没有 Content-Length、按 chunked 发送，微信一律回 412（用 curl 加 Transfer-Encoding: chunked 复现），
    // 代码里 EnsureSuccessStatusCode 抛异常 → 500。改用带长度的 StringContent，并且微信回非 2xx、网络出错、回的不是 JSON 都返回
    // 「手机号授权验证失败」而不是 500。
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public long? ContentLength; public bool? Chunked; public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 在读 Content 之前看：.NET 发请求时，Content-Length 算不出来的内容（JsonContent）会改用 chunked
            ContentLength = request.Content!.Headers.ContentLength;
            Chunked = request.Headers.TransferEncodingChunked;
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"errcode\":0,\"phone_info\":{\"countryCode\":\"86\",\"purePhoneNumber\":\"13800001003\"}}", Encoding.UTF8, "application/json")
            };
        }
    }
    private sealed class FixedClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
    private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw error;
    }

    [Fact]
    public async Task The_request_to_wechat_has_a_known_length_not_chunked_because_wechat_answers_412_to_chunked_bodies()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            var capture = new CapturingHandler();
            var result = Result(await new MemberWechatBindController(f.NewContext()).Bind(
                new() { token = token, phoneCode = "phone-code" }, "session-new", new Tokens(), new FixedClients(capture)));
            Assert.Equal(0, result.code);
            Assert.NotNull(capture.ContentLength);                  // 带长度，不是 chunked
            Assert.NotEqual(true, capture.Chunked);
            Assert.Equal("{\"code\":\"phone-code\"}", capture.Body);
        }
    }

    [Theory]
    [InlineData(412)]
    [InlineData(500)]
    [InlineData(502)]
    public async Task A_non_success_answer_from_wechat_is_reported_not_thrown_as_a_500(int status)
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            var clients = new FixedClients(new FuncHandler(_ => new HttpResponseMessage((HttpStatusCode)status)));
            var result = Result(await new MemberWechatBindController(f.NewContext()).Bind(
                new() { token = token, phoneCode = "phone-code" }, "session-new", new Tokens(), clients));
            Assert.Equal(1, result.code);
            Assert.Contains("手机号", result.message);
            Assert.Equal(0, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);   // 二维码还能再用
        }
    }

    [Fact]
    public async Task A_network_error_or_a_non_json_answer_is_reported_not_thrown()
    {
        var (f, id, token) = await Setup();
        await using (f)
        {
            var network = Result(await new MemberWechatBindController(f.NewContext()).Bind(
                new() { token = token, phoneCode = "phone-code" }, "session-new", new Tokens(),
                new FixedClients(new ThrowingHandler(new HttpRequestException("network down")))));
            Assert.Equal(1, network.code);
            Assert.Contains("手机号", network.message);
            var html = Result(await new MemberWechatBindController(f.NewContext()).Bind(
                new() { token = token, phoneCode = "phone-code" }, "session-new", new Tokens(),
                new FixedClients(new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>bad gateway</html>") }))));
            Assert.Equal(1, html.code);
            Assert.Contains("手机号", html.message);
            Assert.Equal(0, (await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id)).authed);
        }
    }

    // 2026-11-23 用户：会员绑定微信的二维码有效期由 2 分钟改成 30 分钟（2 分钟来不及：扫码、等小程序加载、授权手机号）。
    [Fact]
    public async Task The_bind_qr_code_is_valid_for_thirty_minutes()
    {
        Assert.Equal(30, MemberWechatBindController.QrValidMinutes);
        var (f, id, token) = await Setup();
        await using (f)
        {
            var qr = await f.Db.scanQrCode.AsNoTracking().SingleAsync(q => q.id == id);
            var left = qr.expire_time!.Value - DateTime.Now;
            Assert.InRange(left.TotalMinutes, 29.5, 30.1);            // 刚生成：还剩约 30 分钟
            // 25 分钟后（原来 2 分钟的规则下早已失效）仍然能绑定
            qr.expire_time = DateTime.Now.AddMinutes(5);
            f.Db.scanQrCode.Update(qr);
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            Assert.Equal(0, Result(await Bind(f, token, "13800001003")).code);
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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Tests;

/// <summary>
/// 走真实 HTTP 模型绑定（不是直接调控制器方法）：小程序用 wx.uploadFile 发 multipart（头像 + 昵称表单字段），
/// 只改昵称时发 application/x-www-form-urlencoded，sessionKey 在 query 里。
/// </summary>
public sealed class MemberProfileHttpTests
{
    private static readonly byte[] Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 2, 3, 4, 5 };

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"HTTP {(int)response.StatusCode}: {text}");
        return Body(text);
    }

    [Fact]
    public async Task MultipartAndUrlEncodedRequestsBindAndSave()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var storage = new MiniTicketQrTests.Storage();
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<ApplicationDBContext>(o => o.UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                services.AddSingleton<IFileStorage>(storage);
                services.AddControllers().AddApplicationPart(typeof(MemberProfileController).Assembly);
            })
            .Configure(app => { app.UseRouting(); app.UseEndpoints(e => e.MapControllers()); }));
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            await db.Database.EnsureCreatedAsync();
            db.member.Add(new Member { id = 3, real_name = "学员" });
            db.miniSession.Add(new MiniSession { session_key = "customer-key", session_type = "wechat_mini_openid", member_id = 3,
                valid = 1, expire_date = DateTime.Now.AddHours(2) });
            db.miniSession.Add(new MiniSession { session_key = "unbound-key", session_type = "wechat_mini_openid", member_id = null,
                valid = 1, expire_date = DateTime.Now.AddHours(2) });
            await db.SaveChangesAsync();
        }
        using var client = server.CreateClient();

        // 1. 还没有资料：要弹
        var get1 = Body(await client.GetStringAsync("/api/MemberProfile/GetMyProfile?sessionKey=customer-key"));
        Assert.Equal(0, get1.GetProperty("code").GetInt32());
        Assert.True(get1.GetProperty("data").GetProperty("needProfile").GetBoolean());

        // 点击昵称输入框只记行为，绑定会员和页面，不会写入昵称。
        var click = await Json(await client.PostAsJsonAsync("/api/MemberProfile/LogNicknameClick",
            new { sessionKey = "customer-key", sessionType = "wechat_mini_openid", sourcePage = "pages/order/recept_checkin" }));
        Assert.Equal(0, click.GetProperty("code").GetInt32());
        var refusedClick = await Json(await client.PostAsJsonAsync("/api/MemberProfile/LogNicknameClick",
            new { sessionKey = "unbound-key", sessionType = "wechat_mini_openid", sourcePage = "pages/order/recept_checkin" }));
        Assert.Equal(1, refusedClick.GetProperty("code").GetInt32());
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            var log = Assert.Single(await db.coreDataModLog.AsNoTracking().ToListAsync());
            Assert.Equal("点击获取昵称", log.scene);
            Assert.Equal("member", log.table_name);
            Assert.Equal(3, log.member_id);
            Assert.Equal(3, log.key_value);
            Assert.Equal("pages/order/recept_checkin", log.manual_memo);
            Assert.Null(log.current_value);
            Assert.Null(log.prev_value);
            Assert.False(await db.memberProfile.AsNoTracking().AnyAsync());
        }

        // 2. wx.uploadFile：multipart，文件字段名 avatar，昵称是表单字段
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(file, "avatar", "tmp_abc.jpg");
        multipart.Add(new StringContent("雪友小明"), "nickName");
        var saved1 = await Json(await client.PostAsync("/api/MemberProfile/SaveMyProfile?sessionKey=customer-key&sessionType=wechat_mini_openid", multipart));
        Assert.Equal(0, saved1.GetProperty("code").GetInt32());
        Assert.Equal("雪友小明", saved1.GetProperty("data").GetProperty("nickName").GetString());
        Assert.False(saved1.GetProperty("data").GetProperty("needProfile").GetBoolean());
        Assert.Equal("https://images.test/upload/1.png", saved1.GetProperty("data").GetProperty("avatarUrl").GetString());
        Assert.Equal(1, storage.Saves);

        // 3. wx.request：只改昵称，表单编码
        var saved2 = await Json(await client.PostAsync("/api/MemberProfile/SaveMyProfile?sessionKey=customer-key",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("nickName", "新昵称") })));
        Assert.Equal(0, saved2.GetProperty("code").GetInt32());
        Assert.Equal("新昵称", saved2.GetProperty("data").GetProperty("nickName").GetString());
        Assert.Equal("https://images.test/upload/1.png", saved2.GetProperty("data").GetProperty("avatarUrl").GetString());   // 头像没动
        Assert.Equal(1, storage.Saves);

        // 4. 还没验证手机号的会话 / 不存在的会话：保存被拒（HTTP 200 + code 1，小程序按 code 提示）
        using var unbound = new MultipartFormDataContent { { new StringContent("谁"), "nickName" } };
        var refused = await Json(await client.PostAsync("/api/MemberProfile/SaveMyProfile?sessionKey=unbound-key", unbound));
        Assert.Equal(1, refused.GetProperty("code").GetInt32());
        var noSession = await Json(await client.PostAsync("/api/MemberProfile/SaveMyProfile?sessionKey=nope",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("nickName", "谁") })));
        Assert.Equal(1, noSession.GetProperty("code").GetInt32());

        // 5. 已经有资料了：GetMyProfile 不再要求弹，带回昵称
        var get2 = Body(await client.GetStringAsync("/api/MemberProfile/GetMyProfile?sessionKey=customer-key"));
        Assert.False(get2.GetProperty("data").GetProperty("needProfile").GetBoolean());
        Assert.Equal("新昵称", get2.GetProperty("data").GetProperty("nickName").GetString());
        // 没验证手机号的会话：不弹也不报错
        var get3 = Body(await client.GetStringAsync("/api/MemberProfile/GetMyProfile?sessionKey=unbound-key"));
        Assert.Equal(0, get3.GetProperty("code").GetInt32());
        Assert.False(get3.GetProperty("data").GetProperty("needProfile").GetBoolean());
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 万龙自我游（供应商）订单回调 POST core/WanlongZiwoyouHelper/CallBack。
    /// 以前只返回 200，收到什么都不留；现在把原文和时间记进 web_api_log，事后能对上「哪一条通知是哪张票什么状态」。
    /// 按供应商协议返回 HTTP 200 和 state=1 的 JSON，记录失败时仍保持原有的成功应答行为。
    /// </summary>
    public class ZiwoyouCallbackTests
    {
        const string PATH = "/core/WanlongZiwoyouHelper/CallBack";

        [Fact]
        public async Task 收到回调_原文存进web_api_log_仍返回200()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            string body = "{\"orderId\":1223005729,\"orderState\":4,\"other\":\"x\"}";
            var result = await f.Controller(db, body).CallBack();
            AssertSuccessResponse(result);

            var rows = await db.webApiLog.ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal("万龙自我游", row.source);
            Assert.Equal("回调通知", row.purpose);
            Assert.Equal("POST", row.method);
            Assert.Equal(PATH, row.request_url);
            Assert.Equal(body, row.payload);
            Assert.Equal("", row.response);
            Assert.True((DateTime.Now - row.create_date).TotalMinutes < 1);
        }

        [Fact]
        public async Task 请求头只留_ContentType_和_UserAgent_不存授权信息()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var c = f.Controller(db, "{}");
            c.HttpContext.Request.Headers["Authorization"] = "Bearer super-secret-token";
            c.HttpContext.Request.Headers["Cookie"] = "session=abc";
            c.HttpContext.Request.Headers["User-Agent"] = "zowoyoo-callback/1.0";
            await c.CallBack();

            var row = await db.webApiLog.SingleAsync();
            Assert.Contains("application/json", row.header);
            Assert.Contains("zowoyoo-callback/1.0", row.header);
            Assert.DoesNotContain("super-secret-token", row.header);
            Assert.DoesNotContain("session=abc", row.header);
        }

        [Fact]
        public async Task 备注里带订单号和订单状态_根级JSON()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            await f.Controller(db, "{\"orderId\":1223005729,\"orderState\":4}").CallBack();
            var row = await db.webApiLog.SingleAsync();
            Assert.Equal("orderId=1223005729 orderState=4", row.memo);
        }

        [Fact]
        public async Task 备注也认data里的订单号和状态()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            await f.Controller(db, "{\"state\":1,\"data\":{\"orderId\":1223005729,\"orderState\":2}}").CallBack();
            var row = await db.webApiLog.SingleAsync();
            Assert.Contains("orderId=1223005729", row.memo);
            Assert.Contains("orderState=2", row.memo);
        }

        [Fact]
        public async Task 不是JSON的正文_照原文存_备注为空()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            await f.Controller(db, "orderId=1223005729&orderState=4").CallBack();
            var row = await db.webApiLog.SingleAsync();
            Assert.Equal("orderId=1223005729&orderState=4", row.payload);
            Assert.Equal("", row.memo);
        }

        [Fact]
        public async Task 空正文也记一行()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db, "").CallBack();
            AssertSuccessResponse(result);
            var row = await db.webApiLog.SingleAsync();
            Assert.Equal("", row.payload);
        }

        [Fact]
        public async Task 超长正文截断到上限并标记_防止公开接口被灌满库()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            string body = new string('x', 20000);
            await f.Controller(db, body).CallBack();
            var row = await db.webApiLog.SingleAsync();
            Assert.StartsWith(new string('x', 8000), row.payload);
            Assert.EndsWith("（已截断）", row.payload);
            Assert.True(row.payload.Length < 8100);
        }

        [Fact]
        public async Task 连续两次回调记两行()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            await f.Controller(db, "{\"orderId\":1}").CallBack();
            await f.Controller(db, "{\"orderId\":2}").CallBack();
            Assert.Equal(2, await db.webApiLog.CountAsync());
        }

        [Fact]
        public async Task 过长的地址_UserAgent_订单号会截断到库里列宽以内_否则插入失败记录会丢()
        {
            // web_api_log 的列宽：request_url varchar(500)、header varchar(1000)、memo varchar(500)
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var c = f.Controller(db, "{\"orderId\":\"" + new string('9', 2000) + "\"}");
            c.HttpContext.Request.QueryString = new QueryString("?a=" + new string('q', 2000));
            c.HttpContext.Request.Headers["User-Agent"] = new string('u', 3000);
            await c.CallBack();

            var row = await db.webApiLog.SingleAsync();
            Assert.True(row.request_url.Length <= 500, "request_url " + row.request_url.Length);
            Assert.True(row.header.Length <= 1000, "header " + row.header.Length);
            Assert.True(row.memo.Length <= 500, "memo " + row.memo.Length);
            Assert.StartsWith(PATH, row.request_url);
        }

        [Fact]
        public async Task 记录失败_仍返回200_不抛异常()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            await f.CloseConnection();            // 库连接断了，SaveChanges 会抛异常
            var result = await f.Controller(db, "{\"orderId\":1}").CallBack();
            AssertSuccessResponse(result);
        }

        private static void AssertSuccessResponse(ActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
            Assert.Equal(1, json.RootElement.GetProperty("state").GetInt32());
            Assert.Equal("", json.RootElement.GetProperty("msg").GetString());
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly SqliteConnection connection;
            public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();

            private Fixture(SqliteConnection connection) { this.connection = connection; }

            public ApplicationDBContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

            public WanlongZiwoyouHelper Controller(ApplicationDBContext db, string body)
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Method = "POST";
                ctx.Request.Path = PATH;
                ctx.Request.ContentType = "application/json";
                ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
                return new WanlongZiwoyouHelper(db, Config) { ControllerContext = new ControllerContext { HttpContext = ctx } };
            }

            public Task CloseConnection() => connection.CloseAsync();

            public static async Task<Fixture> Create()
            {
                var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
                await connection.OpenAsync();
                var f = new Fixture(connection);
                await using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
                await db.Database.EnsureCreatedAsync();
                return f;
            }

            public async ValueTask DisposeAsync() { await connection.DisposeAsync(); }
        }
    }
}

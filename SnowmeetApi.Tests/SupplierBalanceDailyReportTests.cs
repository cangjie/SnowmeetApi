using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Ops;
using Xunit;

namespace SnowmeetApi.Tests;

// 2026-11-28 用户要求：每天晚上 8 点查大好河山账户的资金余额，通过企业微信发给苍杰和布乖（渠道用应用 1000008「报表」）。
// 这里不联网：查余额、发送、查「今天发过没有」都是注入的假实现；生产接线只测到不需要联网的部分。
public sealed class SupplierBalanceDailyReportTests
{
    private const string Both = "Snowmeet-CangJie_1|Snowmeet-Buguai";
    private const string Accepted = "{\"errcode\":0,\"errmsg\":\"ok\",\"msgid\":\"m1\"}";

    private sealed class Fake
    {
        public double? Balance = 8600;
        public Func<double?>? Query;                 // 要抛异常之类的特殊行为时用
        public Func<bool> SendResult = () => true;
        public Func<bool> Already = () => false;
        public string? Receivers = Both;
        public int Queries, LogChecks;
        public readonly List<(string text, string to)> Sent = new();

        public SupplierBalanceDailyReport Report() => new(
            () => { Queries++; return Task.FromResult(Query != null ? Query() : Balance); },
            (text, to) => { Sent.Add((text, to)); return Task.FromResult(SendResult()); },
            _ => { LogChecks++; return Task.FromResult(Already()); },
            () => Receivers);
    }

    private static DateTime At(int hour, int minute, int second = 0, int day = 28) => new(2026, 11, day, hour, minute, second);

    // ---------- 什么时候发、发什么 ----------

    [Fact]
    public async Task Nothing_happens_before_eight()
    {
        var f = new Fake();
        var report = f.Report();
        Assert.Equal("not_yet", await report.TickAsync(At(0, 0)));
        Assert.Equal("not_yet", await report.TickAsync(At(19, 59, 59)));
        Assert.Equal(0, f.Queries);
        Assert.Equal(0, f.LogChecks);
        Assert.Empty(f.Sent);
    }

    [Fact]
    public async Task At_eight_the_balance_goes_to_both_people_once()
    {
        var f = new Fake();
        var report = f.Report();
        Assert.Equal("sent", await report.TickAsync(At(20, 0)));
        var sent = Assert.Single(f.Sent);
        Assert.Equal(Both, sent.to);
        Assert.Equal("【大好河山余额】\n账户余额：¥8600.00\n11-28 20:00", sent.text);
        Assert.Equal("done", await report.TickAsync(At(20, 1)));
        Assert.Equal("done", await report.TickAsync(At(23, 59)));
        Assert.Single(f.Sent);
        Assert.Equal(1, f.Queries);
        Assert.Equal(1, f.LogChecks);
    }

    [Theory]
    [InlineData(1999.99, "【大好河山余额】\n账户余额：¥1999.99\n低于 ¥2000，请尽快充值。\n11-28 20:00")]
    [InlineData(0, "【大好河山余额】\n账户余额：¥0.00\n低于 ¥2000，请尽快充值。\n11-28 20:00")]
    [InlineData(2000, "【大好河山余额】\n账户余额：¥2000.00\n11-28 20:00")]
    [InlineData(11631.16, "【大好河山余额】\n账户余额：¥11631.16\n11-28 20:00")]
    public async Task The_top_up_line_appears_only_below_2000(double balance, string expected)
    {
        var f = new Fake { Balance = balance };
        await f.Report().TickAsync(At(20, 0));
        Assert.Equal(expected, Assert.Single(f.Sent).text);
    }

    // ---------- 查不到余额 ----------

    [Fact]
    public async Task After_three_failed_queries_a_please_check_message_is_sent()
    {
        var f = new Fake { Balance = null };
        var report = f.Report();
        Assert.Equal("query_retry", await report.TickAsync(At(20, 0)));
        Assert.Equal("waiting", await report.TickAsync(At(20, 0, 30)));
        Assert.Equal("query_retry", await report.TickAsync(At(20, 1)));
        Assert.Empty(f.Sent);
        Assert.Equal("sent", await report.TickAsync(At(20, 2)));
        var sent = Assert.Single(f.Sent);
        Assert.Equal("【大好河山余额】\n今天没查到余额，请手动看一下。\n11-28 20:02", sent.text);
        Assert.Equal(Both, sent.to);
        Assert.Equal("done", await report.TickAsync(At(20, 3)));
        Assert.Equal(3, f.Queries);
    }

    [Fact]
    public async Task A_query_that_recovers_sends_the_balance()
    {
        var f = new Fake { Balance = null };
        var report = f.Report();
        Assert.Equal("query_retry", await report.TickAsync(At(20, 0)));
        f.Balance = 5000;
        Assert.Equal("sent", await report.TickAsync(At(20, 1)));
        Assert.Equal("【大好河山余额】\n账户余额：¥5000.00\n11-28 20:01", Assert.Single(f.Sent).text);
    }

    [Fact]
    public async Task A_query_that_throws_counts_as_not_found()
    {
        var f = new Fake { Query = () => throw new InvalidOperationException("供应商接口不可用") };
        var report = f.Report();
        Assert.Equal("query_retry", await report.TickAsync(At(20, 0)));
        Assert.Equal("query_retry", await report.TickAsync(At(20, 1)));
        Assert.Equal("sent", await report.TickAsync(At(20, 2)));
        Assert.Contains("今天没查到余额", Assert.Single(f.Sent).text);
    }

    // ---------- 发不出去 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_send_is_retried_five_minutes_later_with_a_fresh_balance(bool throws)
    {
        var f = new Fake { SendResult = throws ? () => throw new InvalidOperationException("企业微信接口不可用") : () => false };
        var report = f.Report();
        Assert.Equal("send_failed", await report.TickAsync(At(20, 0)));
        Assert.Equal("waiting", await report.TickAsync(At(20, 4)));
        f.Balance = 8000;
        f.SendResult = () => true;
        Assert.Equal("sent", await report.TickAsync(At(20, 5)));
        Assert.Equal(2, f.Sent.Count);
        Assert.Equal("【大好河山余额】\n账户余额：¥8000.00\n11-28 20:05", f.Sent[1].text);
        Assert.Equal(2, f.Queries);
        Assert.Equal("done", await report.TickAsync(At(20, 6)));
    }

    [Fact]
    public async Task The_please_check_message_is_retried_too()
    {
        var f = new Fake { Balance = null, SendResult = () => false };
        var report = f.Report();
        await report.TickAsync(At(20, 0));
        await report.TickAsync(At(20, 1));
        Assert.Equal("send_failed", await report.TickAsync(At(20, 2)));
        f.SendResult = () => true;
        Assert.Equal("sent", await report.TickAsync(At(20, 7)));
        Assert.Equal("【大好河山余额】\n今天没查到余额，请手动看一下。\n11-28 20:07", f.Sent[^1].text);
    }

    // ---------- 跨天、重启 ----------

    [Fact]
    public async Task The_next_day_it_sends_again()
    {
        var f = new Fake();
        var report = f.Report();
        Assert.Equal("sent", await report.TickAsync(At(20, 0)));
        Assert.Equal("not_yet", await report.TickAsync(At(19, 59, 0, day: 29)));
        Assert.Equal("sent", await report.TickAsync(At(20, 0, 0, day: 29)));
        Assert.Equal(2, f.Sent.Count);
        Assert.Contains("11-29 20:00", f.Sent[1].text);
    }

    [Fact]
    public async Task Failures_and_waiting_do_not_carry_over_to_the_next_day()
    {
        var f = new Fake { Balance = null };
        var report = f.Report();
        await report.TickAsync(At(23, 58));
        await report.TickAsync(At(23, 59));
        // 第二天从零开始数：头两次还是只重试，第三次才发提示
        Assert.Equal("query_retry", await report.TickAsync(At(20, 0, 0, day: 29)));
        Assert.Equal("query_retry", await report.TickAsync(At(20, 1, 0, day: 29)));
        Assert.Empty(f.Sent);
        Assert.Equal("sent", await report.TickAsync(At(20, 2, 0, day: 29)));
    }

    [Fact]
    public async Task A_service_that_comes_back_later_the_same_day_still_sends()
    {
        var f = new Fake();
        Assert.Equal("sent", await f.Report().TickAsync(At(23, 30)));
        Assert.Contains("11-28 23:30", Assert.Single(f.Sent).text);
    }

    [Fact]
    public async Task Yesterdays_missed_report_is_not_sent_after_midnight()
    {
        var f = new Fake();
        Assert.Equal("not_yet", await f.Report().TickAsync(At(0, 10, 0, day: 29)));
        Assert.Empty(f.Sent);
    }

    [Fact]
    public async Task After_a_restart_it_does_not_send_twice()
    {
        var f = new Fake { Already = () => true };
        var report = f.Report();
        Assert.Equal("done", await report.TickAsync(At(20, 30)));
        Assert.Equal("done", await report.TickAsync(At(20, 31)));
        Assert.Equal(0, f.Queries);
        Assert.Empty(f.Sent);
        Assert.Equal(1, f.LogChecks);          // 确认发过之后当天不再反复查库
    }

    [Fact]
    public async Task When_the_send_record_cannot_be_read_nothing_is_sent_and_it_tries_again()
    {
        var f = new Fake { Already = () => throw new InvalidOperationException("数据库连不上") };
        var report = f.Report();
        await Assert.ThrowsAsync<InvalidOperationException>(() => report.TickAsync(At(20, 0)));
        Assert.Empty(f.Sent);
        f.Already = () => false;
        Assert.Equal("sent", await report.TickAsync(At(20, 1)));
    }

    [Fact]
    public async Task Without_receivers_nothing_is_queried()
    {
        var f = new Fake { Receivers = "  " };
        Assert.Equal("no_receivers", await f.Report().TickAsync(At(20, 0)));
        Assert.Equal(0, f.Queries);
        Assert.Empty(f.Sent);
    }

    // ---------- 「今天发过没有」看接口日志 ----------

    private static WebApiLog Log(DateTime at, string reply, string memo = "下发文本消息", string purpose = "大好河山余额日报") => new()
    {
        source = "企业微信", purpose = purpose, memo = memo, method = "POST",
        request_url = "https://example.invalid/send", header = "", payload = "", response = reply, deal = 1, create_date = at
    };

    private static async Task<bool> AlreadySentWith(WebApiLog row, DateTime now)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.webApiLog.Add(row);
        await db.SaveChangesAsync();
        return await SupplierBalanceDailyReport.AlreadySentAsync(db, now);
    }

    [Fact]
    public async Task Todays_accepted_report_counts_as_already_sent()
    {
        Assert.True(await AlreadySentWith(Log(At(20, 0, 5), Accepted), At(20, 30)));
    }

    [Theory]
    [InlineData("token")]
    [InlineData("rejected")]
    [InlineData("yesterday")]
    [InlineData("other_purpose")]
    [InlineData("no_reply")]
    public async Task Other_log_rows_do_not_count_as_already_sent(string kind)
    {
        WebApiLog row = kind switch
        {
            "token" => Log(At(20, 0), Accepted, memo: "获取Token"),      // 取令牌成功的返回也是 errcode 0
            "rejected" => Log(At(20, 0), "{\"errcode\":81013,\"errmsg\":\"user & party & tag all invalid\"}"),
            "yesterday" => Log(At(20, 0, 0, day: 27), Accepted),
            "other_purpose" => Log(At(20, 0), Accepted, purpose: "美团采集提醒"),
            _ => Log(At(20, 0), "")
        };
        Assert.False(await AlreadySentWith(row, At(20, 30)));
    }

    [Theory]
    [InlineData("{\"errcode\":0,\"errmsg\":\"ok\"}", true)]
    [InlineData("{\"errcode\":0,\"errmsg\":\"ok\",\"invaliduser\":\"Snowmeet-Buguai\"}", true)]
    [InlineData("{\"errcode\":40014,\"errmsg\":\"invalid access_token\"}", false)]
    [InlineData("{}", false)]
    [InlineData("<html>502 Bad Gateway</html>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_reply_with_errcode_zero_is_accepted(string? reply, bool expected)
    {
        Assert.Equal(expected, SupplierBalanceDailyReport.IsAccepted(reply));
    }

    // ---------- 生产接线与「报表」应用 ----------

    // 控制器构造时 Settings.GetSettings 要读齐这几个键（线上配置都有）
    private static IConfiguration TestConfig() => new ConfigurationBuilder().AddInMemoryCollection(
        new[] { "AppId", "AppSecret", "OriginalId", "token", "TiktokDomain", "TiktokAppId", "TiktokAppSecret" }
            .ToDictionary(k => "Settings:" + k, k => (string?)("test-" + k))).Build();

    [Fact]
    public async Task The_production_wiring_reads_todays_record_from_the_database()
    {
        string file = Path.Combine(Path.GetTempPath(), "balance-report-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite($"Data Source={file};Pooling=False").Options))
            {
                await db.Database.EnsureCreatedAsync();
                db.webApiLog.Add(Log(At(20, 0, 5), Accepted));
                await db.SaveChangesAsync();
            }
            var services = new ServiceCollection();
            // 故意用只读库：任何对外请求之前都要先写接口日志，写不进去就到不了网络。
            // 这样即使「发过没有」的判断被改坏，这条测试也不会真的去查供应商或发企业微信。
            services.AddDbContext<ApplicationDBContext>(o => o.UseSqlite($"Data Source={file};Mode=ReadOnly;Pooling=False"));
            services.AddSingleton(TestConfig());
            await using var provider = services.BuildServiceProvider();
            var report = SupplierBalanceDailyReport.Create(provider.GetRequiredService<IServiceScopeFactory>());
            Assert.Equal("done", await report.TickAsync(At(20, 30)));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Both_helpers_can_be_built_outside_a_web_request()
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite("Data Source=:memory:").Options);
        _ = new WanlongZiwoyouHelper(db, TestConfig(), SkipassSaleRules.SOURCE_DHHS);
        _ = new WeComController(db, TestConfig(), null!);
    }

    [Fact]
    public void The_message_goes_through_the_report_app()
    {
        Assert.Equal(1000008, WeComController.REPORT_AGENT_ID);
        var payload = JObject.Parse(WeComController.TextMessagePayload("  第一行\n第二行  ", Both));
        Assert.Equal(1000008, (int)payload["agentid"]!);
        Assert.Equal("text", (string?)payload["msgtype"]);
        Assert.Equal(Both, (string?)payload["touser"]);
        Assert.Equal("第一行\n第二行", (string?)payload["text"]!["content"]);
    }

    [Fact]
    public void The_send_method_cannot_be_called_over_the_network()
    {
        MethodInfo send = typeof(WeComController).GetMethod("SendText")!;
        Assert.NotNull(send.GetCustomAttribute<NonActionAttribute>());
    }

    [Fact]
    public void The_job_is_registered_only_when_background_jobs_are_on()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Startup.cs"))) folder = folder.Parent;
        Assert.NotNull(folder);
        string line = Assert.Single(File.ReadAllLines(Path.Combine(folder!.FullName, "Startup.cs")),
            l => l.Contains("SupplierBalanceDailyReportJob"));
        Assert.StartsWith("if (!backgroundJobsOff) services.AddHostedService<", line.Trim());
    }
}

using SnowmeetApi.Services.Ops;
using Xunit;

namespace SnowmeetApi.Tests;

// 2026-11-18 用户要求：预定雪票时，如果发现大好河山账号的预存款低于 2000 元，给苍杰和布乖的企业微信发预警。
// 这里测预警服务本身（阈值、收件人、重复发送的节流、发送失败不影响调用方）；下单时在哪里调用它见 SkipassCartTests 里的「下单预警」用例。
public sealed class SupplierBalanceAlertTests
{
    private sealed class Recorder
    {
        public readonly List<(string text, string to)> Sent = new();
        public bool Succeed = true;
        public bool Throw;

        public Task<bool> Send(string text, string to)
        {
            Sent.Add((text, to));
            if (Throw) throw new InvalidOperationException("企业微信接口不可用");
            return Task.FromResult(Succeed);
        }
    }

    private static readonly DateTime T0 = new(2026, 11, 18, 14, 20, 0);

    private static (SupplierBalanceAlertService svc, Recorder rec, Func<DateTime> clock, Action<TimeSpan> advance) Make(string? receivers = "Snowmeet-CangJie_1|Snowmeet-Buguai")
    {
        var rec = new Recorder();
        DateTime now = T0;
        var svc = new SupplierBalanceAlertService(rec.Send, () => now, () => receivers);
        return (svc, rec, () => now, d => now += d);
    }

    // ---------- 阈值和来源 ----------

    [Theory]
    [InlineData("大好河山", 1999.99, true)]
    [InlineData("大好河山", 0, true)]
    [InlineData("大好河山", 2000, false)]          // 「低于 2000」：正好 2000 不预警
    [InlineData("大好河山", 2000.01, false)]
    [InlineData("大好河山", 100000, false)]
    [InlineData(" 大好河山 ", 500, true)]           // 来源名两端的空格不影响
    [InlineData("万龙自我游", 500, false)]          // 只盯大好河山
    [InlineData("别的渠道", 0, false)]
    [InlineData("", 0, false)]
    [InlineData(null, 0, false)]
    public void Only_the_dahaoheshan_account_below_2000_needs_an_alert(string? source, double balance, bool expected)
    {
        Assert.Equal(expected, SupplierBalanceAlertService.NeedsAlert(source, balance));
    }

    [Fact]
    public void The_threshold_is_2000_and_the_default_receivers_are_cangjie_and_buguai()
    {
        Assert.Equal(2000, SupplierBalanceAlertService.Threshold);
        Assert.Equal("Snowmeet-CangJie_1|Snowmeet-Buguai", SupplierBalanceAlertService.DefaultReceivers);
    }

    // ---------- 发送 ----------

    [Fact]
    public async Task A_low_balance_sends_one_message_to_both_people()
    {
        var (svc, rec, _, _) = Make();
        Assert.Equal("sending", svc.Check("大好河山", 1234.5));
        await svc.WhenIdleAsync();
        var sent = Assert.Single(rec.Sent);
        Assert.Equal("Snowmeet-CangJie_1|Snowmeet-Buguai", sent.to);
        Assert.Contains("大好河山", sent.text);
        Assert.Contains("1234.50", sent.text);          // 当前余额
        Assert.Contains("2000", sent.text);             // 阈值
        Assert.Contains("11-18 14:20", sent.text);      // 发出的时间
    }

    [Fact]
    public async Task Without_a_receivers_config_the_default_two_people_get_it()
    {
        var rec = new Recorder();
        var svc = new SupplierBalanceAlertService(rec.Send, () => T0);   // 不传收件人配置：先读服务器上的配置文件，没有就用默认
        svc.Check("大好河山", 100);
        await svc.WhenIdleAsync();
        Assert.Equal(SupplierBalanceAlertService.DefaultReceivers, Assert.Single(rec.Sent).to);
    }

    [Theory]
    [InlineData("万龙自我游", 100)]
    [InlineData("大好河山", 2000)]
    [InlineData("大好河山", 50000)]
    public async Task Nothing_is_sent_when_the_account_is_not_watched_or_the_balance_is_fine(string source, double balance)
    {
        var (svc, rec, _, _) = Make();
        svc.Check(source, balance);
        await svc.WhenIdleAsync();
        Assert.Empty(rec.Sent);
    }

    [Fact]
    public async Task An_unknown_balance_never_alerts_the_query_may_simply_have_failed()
    {
        var (svc, rec, _, _) = Make();
        Assert.Equal("unknown", svc.Check("大好河山", null));
        await svc.WhenIdleAsync();
        Assert.Empty(rec.Sent);
    }

    // ---------- 节流：订票一多不能刷屏 ----------

    [Fact]
    public async Task The_same_low_balance_is_only_reported_once_an_hour()
    {
        var (svc, rec, _, advance) = Make();
        svc.Check("大好河山", 1800);
        await svc.WhenIdleAsync();
        advance(TimeSpan.FromMinutes(1));
        Assert.Equal("throttled", svc.Check("大好河山", 1400));
        advance(TimeSpan.FromMinutes(58));                // 距上一条 59 分钟
        Assert.Equal("throttled", svc.Check("大好河山", 1000));
        await svc.WhenIdleAsync();
        Assert.Single(rec.Sent);

        advance(TimeSpan.FromMinutes(2));                 // 距上一条 61 分钟：再提醒一次，余额写最新的
        Assert.Equal("sending", svc.Check("大好河山", 900));
        await svc.WhenIdleAsync();
        Assert.Equal(2, rec.Sent.Count);
        Assert.Contains("900.00", rec.Sent[1].text);
    }

    [Fact]
    public async Task Once_the_balance_is_topped_up_the_next_dip_alerts_again_right_away()
    {
        var (svc, rec, _, advance) = Make();
        svc.Check("大好河山", 1500);
        await svc.WhenIdleAsync();
        advance(TimeSpan.FromMinutes(10));
        Assert.Equal("fine", svc.Check("大好河山", 30000));     // 充值了
        advance(TimeSpan.FromMinutes(10));
        Assert.Equal("sending", svc.Check("大好河山", 1900));         // 又掉到 2000 以下，不用等满一小时
        await svc.WhenIdleAsync();
        Assert.Equal(2, rec.Sent.Count);
    }

    // ---------- 发送失败 ----------

    [Fact]
    public async Task A_failed_send_is_retried_after_five_minutes_not_on_every_order()
    {
        var (svc, rec, _, advance) = Make();
        rec.Succeed = false;
        svc.Check("大好河山", 1500);
        await svc.WhenIdleAsync();
        Assert.Single(rec.Sent);

        advance(TimeSpan.FromMinutes(4));
        Assert.Equal("throttled", svc.Check("大好河山", 1500));
        advance(TimeSpan.FromMinutes(2));                 // 距失败 6 分钟：可以再试
        rec.Succeed = true;
        Assert.Equal("sending", svc.Check("大好河山", 1500));
        await svc.WhenIdleAsync();
        Assert.Equal(2, rec.Sent.Count);

        advance(TimeSpan.FromMinutes(10));                // 这次成功了，回到一小时一条
        Assert.Equal("throttled", svc.Check("大好河山", 1500));
    }

    [Fact]
    public async Task A_send_that_throws_never_reaches_the_caller()
    {
        var (svc, rec, _, _) = Make();
        rec.Throw = true;
        var ex = Record.Exception(() => svc.Check("大好河山", 1500));
        Assert.Null(ex);
        await svc.WhenIdleAsync();                        // 等发送任务结束也不抛
        Assert.Single(rec.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_receivers_means_nothing_is_sent_and_the_next_order_can_try_again(string? receivers)
    {
        var (svc, rec, _, _) = Make(receivers);
        Assert.Equal("no_receivers", svc.Check("大好河山", 1500));
        await svc.WhenIdleAsync();
        Assert.Empty(rec.Sent);
    }

    [Fact]
    public void The_message_names_the_account_the_balance_and_what_goes_wrong_if_it_runs_out()
    {
        string text = SupplierBalanceAlertService.Format("大好河山", 1234.5, T0);
        Assert.StartsWith("【雪票预警】", text);
        Assert.Contains("大好河山预存款只剩 ¥1234.50", text);
        Assert.Contains("低于 ¥2000", text);
        Assert.Contains("充值", text);
        Assert.Contains("11-18 14:20", text);
    }
}

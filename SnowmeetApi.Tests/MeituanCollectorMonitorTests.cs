using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Services.Fnb;
using Xunit;

namespace SnowmeetApi.Tests;

public class MeituanCollectorMonitorTests
{
    DateTime _now = new(2026, 10, 3, 2, 0, 0, DateTimeKind.Utc);  // 北京时间 10:00
    readonly List<string> _sent = new();
    bool _sendOk = true;

    MeituanCollectorMonitor Create(string? statePath = null) =>
        new(text => { if (_sendOk) _sent.Add(text); return Task.FromResult(_sendOk); }, () => _now, statePath);

    static MeituanCollectorMonitor.Heartbeat Beat(string status = "ok", int sinceRound = 60) =>
        new() { status = status, machine = "门店电脑", seconds_since_last_round = sinceRound, last_round_ok = true };

    [Fact]
    public async Task NeverReported_NoOfflineAlert()
    {
        var m = Create();
        _now = _now.AddDays(3);
        Assert.Empty(await m.Check());
    }

    [Fact]
    public async Task NeedLogin_AlertsOnce_RepeatsAfterTwoHours_ThenRecovers()
    {
        var m = Create();
        await m.OnHeartbeat(Beat());
        var first = await m.OnHeartbeat(Beat("need_login"));
        Assert.Single(first);
        Assert.Contains("登录已失效", first[0]);
        Assert.Contains("门店电脑", first[0]);

        _now = _now.AddMinutes(30);
        Assert.Empty(await m.OnHeartbeat(Beat("need_login")));   // 2 小时内不重复
        _now = _now.AddMinutes(95);
        var again = await m.OnHeartbeat(Beat("need_login"));
        Assert.Single(again);
        Assert.Contains("再次提醒", again[0]);

        _now = _now.AddMinutes(10);
        var back = await m.OnHeartbeat(Beat("ok"));
        Assert.Single(back);
        Assert.Contains("已重新登录", back[0]);
        Assert.Empty(await m.OnHeartbeat(Beat("ok")));
    }

    [Fact]
    public async Task NeedLogin_DoesNotCountAsStalled()
    {
        var m = Create();
        await m.OnHeartbeat(Beat("need_login", sinceRound: 60));
        _sent.Clear();
        for (int i = 0; i < 40; i++)   // 3 个多小时一直等登录，心跳照常
        {
            _now = _now.AddMinutes(5);
            await m.OnHeartbeat(Beat("need_login", sinceRound: 60 + (i + 1) * 300));
            await m.Check();
        }
        Assert.All(_sent, t => Assert.DoesNotContain("没有完成一轮", t));
    }

    [Fact]
    public async Task Offline_AlertAfter20Minutes_RepeatEvery6Hours_ThenRecovery()
    {
        var m = Create();
        await m.OnHeartbeat(Beat());
        _now = _now.AddMinutes(19);
        Assert.Empty(await m.Check());
        _now = _now.AddMinutes(2);
        var off = await m.Check();
        Assert.Single(off);
        Assert.Contains("没有上报", off[0]);
        Assert.Contains("最后一次上报：10-03 10:00", off[0]);   // 按北京时间显示

        _now = _now.AddHours(5);
        Assert.Empty(await m.Check());
        _now = _now.AddHours(1);
        Assert.Single(await m.Check());

        _now = _now.AddDays(2);   // 停了两天多，重新开机
        var back = await m.OnHeartbeat(Beat());
        Assert.Single(back);
        Assert.Contains("已恢复上报", back[0]);
        Assert.Contains("天", back[0]);
        Assert.Empty(await m.Check());
    }

    [Fact]
    public async Task Stalled_WhenHeartbeatsContinueButNoRoundCompletes()
    {
        var m = Create();
        await m.OnHeartbeat(Beat(sinceRound: 0));
        for (int i = 1; i <= 24; i++)   // 2 小时，心跳一直有，但距上一轮越来越久
        {
            _now = _now.AddMinutes(5);
            await m.OnHeartbeat(Beat("running", sinceRound: i * 300));
        }
        var stalled = await m.Check();
        Assert.Single(stalled);
        Assert.Contains("没有完成一轮", stalled[0]);

        _now = _now.AddMinutes(5);
        var back = await m.OnHeartbeat(Beat("ok", sinceRound: 10));
        Assert.Single(back);
        Assert.Contains("已恢复", back[0]);
    }

    [Fact]
    public async Task FailedSend_IsRetriedOnNextCheck()
    {
        var m = Create();
        await m.OnHeartbeat(Beat());
        _now = _now.AddMinutes(25);
        _sendOk = false;
        Assert.Empty(await m.Check());
        _sendOk = true;
        _now = _now.AddMinutes(5);
        Assert.Single(await m.Check());
    }

    [Fact]
    public async Task StateSurvivesRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mtc_state_{Guid.NewGuid():N}.json");
        try
        {
            var m = Create(path);
            await m.OnHeartbeat(Beat());
            _now = _now.AddMinutes(30);
            Assert.Single(await m.Check());

            var restarted = Create(path);   // 服务重启（发布）后状态还在，不会重复报「停机」
            _now = _now.AddMinutes(5);
            Assert.Empty(await restarted.Check());
            Assert.Contains("已恢复上报", Assert.Single(await restarted.OnHeartbeat(Beat())));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "", false)]
    [InlineData(null, "abc", false)]
    [InlineData("", "", false)]
    public void TokenCheck(string? expected, string? given, bool ok)
    {
        Assert.Equal(ok, MeituanCollectorController.TokenMatches(expected, given));
    }
}

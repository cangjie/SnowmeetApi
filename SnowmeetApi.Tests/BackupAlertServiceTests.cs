using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers.Ops;
using SnowmeetApi.Services.Ops;
using Xunit;

namespace SnowmeetApi.Tests;

public class BackupAlertServiceTests
{
    DateTime _now = new(2026, 11, 16, 2, 40, 0, DateTimeKind.Utc);   // 北京时间 10:40
    readonly List<(string text, string to)> _sent = new();
    bool _sendOk = true;

    BackupAlertService Create() =>
        new((text, to) => { if (_sendOk) _sent.Add((text, to)); return Task.FromResult(_sendOk); }, () => _now);

    [Fact]
    public async Task Sends_WithLevelLabel_Message_AndBeijingTime()
    {
        var s = Create();
        Assert.Equal("sent", await s.Notify("error", "full:snowmeet_new 失败：磁盘不够", " u1|u2 "));
        var (text, to) = Assert.Single(_sent);
        Assert.Equal("u1|u2", to);
        Assert.StartsWith("【数据库备份】出问题了\n", text);
        Assert.Contains("full:snowmeet_new 失败：磁盘不够", text);
        Assert.EndsWith("11-16 10:40", text);
    }

    [Theory]
    [InlineData("warning", "请留意")]
    [InlineData("info", "通知")]
    [InlineData(null, "通知")]
    public void Format_LevelLabels(string? level, string label)
    {
        Assert.StartsWith("【数据库备份】" + label + "\n", BackupAlertService.Format(level, "x", _now));
    }

    [Fact]
    public async Task SameMessage_IsSuppressedFor30Minutes_ThenSentAgain()
    {
        var s = Create();
        Assert.Equal("sent", await s.Notify("error", "diff 失败", "u"));
        _now = _now.AddMinutes(29);
        Assert.Equal("duplicate", await s.Notify("error", "diff 失败", "u"));
        Assert.Equal("sent", await s.Notify("error", "另一条", "u"));
        _now = _now.AddMinutes(2);
        Assert.Equal("sent", await s.Notify("error", "diff 失败", "u"));
        Assert.Equal(3, _sent.Count);
    }

    [Fact]
    public async Task FailedSend_IsNotRemembered_SoItCanBeRetried()
    {
        var s = Create();
        _sendOk = false;
        Assert.Equal("send_failed", await s.Notify("error", "x", "u"));
        _sendOk = true;
        Assert.Equal("sent", await s.Notify("error", "x", "u"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoReceivers_NothingIsSent(string? receivers)
    {
        Assert.Equal("no_receivers", await Create().Notify("error", "x", receivers));
        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public async Task EmptyMessage_IsRejected(string? message)
    {
        Assert.Equal("empty", await Create().Notify("error", message, "u"));
        Assert.Empty(_sent);
    }

    [Fact]
    public void Clean_StripsControlCharacters_KeepsNewlines_AndTruncates()
    {
        Assert.Equal("a\nb", BackupAlertService.Clean("a\u0001\nb\u0007"));
        string longText = new string('字', BackupAlertService.MaxMessageLength + 50);
        string cleaned = BackupAlertService.Clean(longText);
        Assert.Equal(BackupAlertService.MaxMessageLength + 1, cleaned.Length);
        Assert.EndsWith("…", cleaned);
    }

    [Fact]
    public async Task Controller_RejectsRequestsWithoutAMatchingToken()
    {
        var controller = new BackupAlertController(Create())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var res = await controller.Notify(new BackupAlertService.Alert { level = "error", message = "x" });
        Assert.IsType<UnauthorizedResult>(res.Result);
        Assert.Empty(_sent);

        controller.ControllerContext.HttpContext.Request.Headers["X-Backup-Token"] = "wrong-token";
        res = await controller.Notify(new BackupAlertService.Alert { level = "error", message = "x" });
        Assert.IsType<UnauthorizedResult>(res.Result);
        Assert.Empty(_sent);
    }
}

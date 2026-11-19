using SnowmeetApi.Helpers;
using Xunit;

namespace SnowmeetApi.Tests;

// 本地 dotnet run 起的后端连的是生产库，它里面的后台定时任务（雪票取消轮询、退款状态同步、日养护快照、美团采集看护）
// 会和服务器上那份同时跑，可能重复退款、重复发提醒。所以本地调试用环境变量 SNOWMEET_DISABLE_BACKGROUND_JOBS=1 把它们关掉（2026-11-19）。
public sealed class BackgroundJobSwitchTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public void The_switch_turns_the_background_jobs_off(string value)
    {
        Assert.True(BackgroundJobSwitch.Disabled(name => name == BackgroundJobSwitch.EnvName ? value : null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    public void Without_the_switch_the_background_jobs_run_as_before(string? value)
    {
        Assert.False(BackgroundJobSwitch.Disabled(name => name == BackgroundJobSwitch.EnvName ? value : null));
        Assert.False(BackgroundJobSwitch.Disabled(_ => null));
    }

    [Fact]
    public void The_switch_name_is_stable()
    {
        Assert.Equal("SNOWMEET_DISABLE_BACKGROUND_JOBS", BackgroundJobSwitch.EnvName);
    }

    [Fact]
    public void The_default_reads_the_real_environment()
    {
        string? before = Environment.GetEnvironmentVariable(BackgroundJobSwitch.EnvName);
        try
        {
            Environment.SetEnvironmentVariable(BackgroundJobSwitch.EnvName, "1");
            Assert.True(BackgroundJobSwitch.Disabled());
            Environment.SetEnvironmentVariable(BackgroundJobSwitch.EnvName, null);
            Assert.False(BackgroundJobSwitch.Disabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(BackgroundJobSwitch.EnvName, before);
        }
    }
}

using System;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 本地调试开关：环境变量 SNOWMEET_DISABLE_BACKGROUND_JOBS=1（或 true）时，Startup 不注册后台定时任务
    /// （雪票取消轮询、退款状态同步、日养护快照、美团采集看护）。
    /// 原因：本地 dotnet run 起的后端默认连生产库，里面的定时任务会和服务器上那份同时跑——
    /// 重复退款、重复向供应商取消、重复发企业微信提醒。服务器上不设这个变量，行为不变。
    /// </summary>
    public static class BackgroundJobSwitch
    {
        public const string EnvName = "SNOWMEET_DISABLE_BACKGROUND_JOBS";

        public static bool Disabled(Func<string, string?>? readEnv = null)
        {
            string value = ((readEnv ?? Environment.GetEnvironmentVariable)(EnvName) ?? "").Trim();
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}

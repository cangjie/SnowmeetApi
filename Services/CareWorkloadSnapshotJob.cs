using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services
{
    /// <summary>
    /// 日养护工作列表的每日快照：每 10 分钟看一眼，今天过了 22:10 就把当天（asOf = 22:00）的统计存下来；
    /// 服务重启后的第一轮会把最近 14 天缺的日子补上（服务停了几天也不会漏）。
    /// 直接在应用里用数据库，不走 crontab 和公网域名——对外网络断了 crontab 任务会一起失效、而且没有告警。
    /// 表还没建 / 查询失败只记一条日志，下一轮再试，不影响别的功能。
    /// </summary>
    public sealed class CareWorkloadSnapshotJob : BackgroundService
    {
        /// <summary>启动时往回补几天。</summary>
        public const int BackDays = 14;
        static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

        private readonly IServiceScopeFactory _scopeFactory;
        // 这个进程已经处理过的日子（存过或那天本来就没有数据），免得每 10 分钟把 15 天重查一遍
        private readonly HashSet<DateTime> _done = new();

        public CareWorkloadSnapshotJob(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 刚启动时先等一会儿，让应用先把请求服务起来
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int saved = await RunOnceAsync(DateTime.Now);
                    if (saved > 0) Console.WriteLine("养护工作量快照：新存 " + saved + " 行");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("养护工作量快照生成失败：" + ex.Message);
                }
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>跑一轮（测试直接调这个）：返回这轮新存的行数。</summary>
        public async Task<int> RunOnceAsync(DateTime now)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            return await new CareWorkloadService(db).EnsureSnapshotsAsync(now, BackDays, _done);
        }
    }
}

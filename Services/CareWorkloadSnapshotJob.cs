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
    /// 日养护工作列表的每日快照：每天晚上 22:00 把当天（asOf = 22:00）的统计存下来，一天一次，只存当天。
    /// 今天的数据不存、直接现算；过去的日子没有快照（服务那天没在 22:00 跑）时也会按当天 22:00 现算，所以不往回补。
    /// 每分钟看一眼时间，到了 22:00 且今天还没存过就存（22:00 以后才启动服务，启动后一分钟左右也会补存今天的）。
    /// 直接在应用里用数据库，不走 crontab 和公网域名——对外网络断了 crontab 任务会一起失效、而且没有告警。
    /// 表还没建 / 查询失败只记一条日志，下一轮再试，不影响别的功能。
    /// </summary>
    public sealed class CareWorkloadSnapshotJob : BackgroundService
    {
        static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopeFactory;
        // 这个进程已经处理过的日子（存过或那天本来就没有数据），免得 22:00 以后每分钟都查库
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
            return await new CareWorkloadService(db).EnsureSnapshotsAsync(now, _done);
        }
    }
}

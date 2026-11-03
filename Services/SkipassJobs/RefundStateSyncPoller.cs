using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SnowmeetApi.Services.SkipassJobs
{
    /// <summary>
    /// 每 5 分钟调一次 Tenpay/SyncPendingRefunds，向微信查还是「处理中」的退款并落成成功。
    /// 微信退款成功回调这几个月没有生效，退款状态不能只靠回调；和 SkipassCancelPoller 一样请求本机接口，
    /// 因为控制器构造函数要读 HttpContext。地址可用配置 RefundSyncUrl 覆盖。
    /// ⚠️ TenpayController 的路由是 api/[controller]/[action]，不是 core/（2026-11-03 之前这里写成 core/，每 5 分钟都 404，任务从没起过作用）。
    /// </summary>
    public sealed class RefundStateSyncPoller : BackgroundService
    {
        /// <summary>默认请求的本机地址。PollerRouteTests 会把它和 TenpayController 的真实路由对上。</summary>
        public const string DefaultUrl = "http://127.0.0.1:5000/api/Tenpay/SyncPendingRefunds?days=3";
        readonly string _url;
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        public RefundStateSyncPoller(IConfiguration config)
        {
            string? configured = config["RefundSyncUrl"];
            _url = string.IsNullOrWhiteSpace(configured) ? DefaultUrl : configured.Trim();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var response = await Client.GetAsync(_url, stoppingToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("[退款状态同步] 返回 " + (int)response.StatusCode);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[退款状态同步] 出错：" + e.Message);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}

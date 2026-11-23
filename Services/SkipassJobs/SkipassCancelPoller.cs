using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SnowmeetApi.Services.SkipassJobs
{
    /// <summary>
    /// 每分钟调两个本机接口：
    ///   SkiPass/RefreshCancel?poll=1 —— 自我游确认取消后自动退款，等太久的标成取消失败；
    ///   SkiPass/RefreshAutoReserve?hours=48 —— 补上刚下单的票的取票码（以前付款回调里睡 25 秒等，现在改为后台补）；
    ///   SkiPass/RefreshAutoReserve?minutes=10 —— 每 10 秒一次（独立循环）：最近 10 分钟内付款、还没取到码的票，供应商一出码就尽快取到；
    ///   SkiPass/RefreshIssuing —— 付款后还没出成的票：30 分钟内重试，超过就确认出票失败并自动退款；
    ///   SkiPass/RefreshUsedAll?recentDays=14 —— 已取票检查：每 5 分钟一次，只在 7:00–21:00，查游玩日期是今天或最近 14 天内的票。
    /// 退款链路里的控制器构造函数要读 HttpContext，后台服务里没有，所以不在这里直接 new 控制器，
    /// 而是请求本机自己的接口；基础地址可用配置 SkipassPollBaseUrl 覆盖。
    /// </summary>
    public sealed class SkipassCancelPoller : BackgroundService
    {
        /// <summary>默认请求的本机基础地址。PollerRouteTests 会把它和 SkiPassController 的真实路由对上。</summary>
        public const string DefaultBase = "http://127.0.0.1:5000/core/SkiPass/";
        static readonly string[] Calls = { "RefreshCancel?poll=1", "RefreshAutoReserve?hours=48", "RefreshIssuing" };
        /// <summary>刚付款的票每 10 秒取一次二维码（见 SkipassSaleRules.FreshCodePollInterval）；PollerRouteTests 会把它和控制器对上。</summary>
        public static readonly string FreshCodeCall = "RefreshAutoReserve?minutes=" + SnowmeetApi.Helpers.SkipassSaleRules.FreshCodeWindowMinutes;
        readonly string _base;
        const string PICKUP_CALL = "RefreshUsedAll?recentDays=14";
        DateTime? _lastPickupCheck;
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        public SkipassCancelPoller(IConfiguration config)
        {
            string? configured = config["SkipassPollBaseUrl"];
            _base = string.IsNullOrWhiteSpace(configured) ? DefaultBase : configured.Trim().TrimEnd('/') + "/";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 服务刚启动时先等一会儿，等 Kestrel 开始监听
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                // 刚付款的票每 10 秒取一次二维码，和下面每分钟的一轮同时跑；两路在控制器里互斥，不会同时处理同一张票
                _ = FreshCodeLoopAsync(stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    foreach (string call in Calls)
                    {
                        try
                        {
                            using var response = await Client.GetAsync(_base + call, stoppingToken);
                            if (!response.IsSuccessStatusCode)
                            {
                                Console.WriteLine("[雪票轮询] " + call + " 返回 " + (int)response.StatusCode);
                            }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("[雪票轮询] " + call + " 出错：" + e.Message);
                        }
                    }
                    DateTime now = DateTime.Now;
                    if (SnowmeetApi.Helpers.SkipassSaleRules.ShouldCheckPickup(now, _lastPickupCheck))
                    {
                        _lastPickupCheck = now;
                        try
                        {
                            using var response = await Client.GetAsync(_base + PICKUP_CALL, stoppingToken);
                            if (!response.IsSuccessStatusCode)
                            {
                                Console.WriteLine("[雪票轮询] " + PICKUP_CALL + " 返回 " + (int)response.StatusCode);
                            }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("[雪票轮询] " + PICKUP_CALL + " 出错：" + e.Message);
                        }
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        // 供应商出码通常在付款后 15～25 秒：对最近 10 分钟内付款、还没取到码的票每 10 秒查一次，顾客不用等到下一个整分钟。
        // 没有这样的票时接口只是一次很轻的库查询；上一轮还没跑完（慢的一路正在处理）时接口直接让位。
        private async Task FreshCodeLoopAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var response = await Client.GetAsync(_base + FreshCodeCall, stoppingToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("[雪票轮询] " + FreshCodeCall + " 返回 " + (int)response.StatusCode);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[雪票轮询] " + FreshCodeCall + " 出错：" + e.Message);
                    }
                    await Task.Delay(SnowmeetApi.Helpers.SkipassSaleRules.FreshCodePollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}

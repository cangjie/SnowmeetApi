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
    ///   SkiPass/RefreshIssuing —— 付款后还没出成的票：30 分钟内重试，超过就确认出票失败并自动退款；
    ///   SkiPass/RefreshUsedAll?recentDays=14 —— 已取票检查：每 5 分钟一次，只在 7:00–21:00，查游玩日期是今天或最近 14 天内的票。
    /// 退款链路里的控制器构造函数要读 HttpContext，后台服务里没有，所以不在这里直接 new 控制器，
    /// 而是请求本机自己的接口；基础地址可用配置 SkipassPollBaseUrl 覆盖。
    /// </summary>
    public sealed class SkipassCancelPoller : BackgroundService
    {
        const string DEFAULT_BASE = "http://127.0.0.1:5000/core/SkiPass/";
        static readonly string[] Calls = { "RefreshCancel?poll=1", "RefreshAutoReserve?hours=48", "RefreshIssuing" };
        readonly string _base;
        const string PICKUP_CALL = "RefreshUsedAll?recentDays=14";
        DateTime? _lastPickupCheck;
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        public SkipassCancelPoller(IConfiguration config)
        {
            string? configured = config["SkipassPollBaseUrl"];
            _base = string.IsNullOrWhiteSpace(configured) ? DEFAULT_BASE : configured.Trim().TrimEnd('/') + "/";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 服务刚启动时先等一会儿，等 Kestrel 开始监听
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
    }
}

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SnowmeetApi.Services.SkipassJobs
{
    /// <summary>
    /// 每分钟调一次 SkiPass/RefreshCancel?poll=1：自我游确认取消后自动退款，等太久的标成取消失败。
    /// 退款链路里的控制器构造函数要读 HttpContext，后台服务里没有，所以不在这里直接 new 控制器，
    /// 而是请求本机自己的接口；地址可用配置 SkipassCancelPollUrl 覆盖。
    /// </summary>
    public sealed class SkipassCancelPoller : BackgroundService
    {
        const string DEFAULT_URL = "http://127.0.0.1:5000/core/SkiPass/RefreshCancel?poll=1";
        readonly string _url;
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        public SkipassCancelPoller(IConfiguration config)
        {
            string? configured = config["SkipassCancelPollUrl"];
            _url = string.IsNullOrWhiteSpace(configured) ? DEFAULT_URL : configured.Trim();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 服务刚启动时先等一会儿，等 Kestrel 开始监听
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var response = await Client.GetAsync(_url, stoppingToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("[雪票取消轮询] 返回 " + (int)response.StatusCode);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[雪票取消轮询] 出错：" + e.Message);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}

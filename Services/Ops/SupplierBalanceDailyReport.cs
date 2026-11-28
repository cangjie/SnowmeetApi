using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;

namespace SnowmeetApi.Services.Ops
{
    /// <summary>
    /// 大好河山账户余额每日提醒（2026-11-28 用户要求）：每天晚上 8 点查一次余额，用企业微信应用「报表」发给苍杰和布乖，
    /// 不论余额多少都发。低于 2000 的即时预警是另一件事，见 SupplierBalanceAlertService。
    ///
    /// 常驻任务每分钟调一次 TickAsync：
    ///   - 当天过了 20:00 且还没发过才动手；服务 20:00 时没在运行，起来后当天补发，过了零点不补。
    ///   - 查不到余额隔 1 分钟再试，第 3 次还查不到就发一条「请手动看一下」，免得到点了却没动静。
    ///   - 发送失败 5 分钟后重试（重新查余额）。
    ///   - 发过没有看接口日志 web_api_log，服务重启不会重复发，不另建表。
    /// 收件人和低余额预警共用一份配置（SupplierBalanceAlertService.ReadReceivers）。
    /// </summary>
    public sealed class SupplierBalanceDailyReport
    {
        public const string Purpose = "大好河山余额日报";

        /// <summary>几点开始发（服务器是北京时间）。</summary>
        static readonly TimeSpan SendAt = TimeSpan.FromHours(20);
        static readonly TimeSpan QueryRetryDelay = TimeSpan.FromMinutes(1);
        static readonly TimeSpan SendRetryDelay = TimeSpan.FromMinutes(5);
        const int QueryFailuresBeforeNotice = 3;

        readonly Func<Task<double?>> _queryBalance;
        readonly Func<string, string, Task<bool>> _send;    // (内容, 接收人)
        readonly Func<DateTime, Task<bool>> _alreadySent;
        readonly Func<string?> _receivers;

        // 下面几项只管当天，换了一天就清零
        DateTime _day;
        bool _done;
        bool _checkedLog;
        int _queryFailures;
        DateTime _notBefore;

        public SupplierBalanceDailyReport(Func<Task<double?>> queryBalance, Func<string, string, Task<bool>> send,
            Func<DateTime, Task<bool>> alreadySent, Func<string?>? receivers = null)
        {
            _queryBalance = queryBalance;
            _send = send;
            _alreadySent = alreadySent;
            _receivers = receivers ?? SupplierBalanceAlertService.ReadReceivers;
        }

        public static string Format(double balance, DateTime now)
        {
            string text = "【大好河山余额】\n账户余额：¥" + balance.ToString("0.00") + "\n";
            if (balance < SupplierBalanceAlertService.Threshold)
            {
                text += "低于 ¥" + SupplierBalanceAlertService.Threshold.ToString("0") + "，请尽快充值。\n";
            }
            return text + now.ToString("MM-dd HH:mm");
        }

        public static string FormatQueryFailed(DateTime now)
        {
            return "【大好河山余额】\n今天没查到余额，请手动看一下。\n" + now.ToString("MM-dd HH:mm");
        }

        /// <summary>
        /// 跑一轮：not_yet（还没到点）/ done（今天发过了）/ waiting（等下次重试）/ no_receivers（没有收件人）/
        /// query_retry（没查到余额，稍后再试）/ sent（发出去了）/ send_failed（没发出去，稍后再试）。
        /// 发送记录读不了时直接抛出：不知道发没发过就不发，下一轮再试。
        /// </summary>
        public async Task<string> TickAsync(DateTime now)
        {
            if (now.Date != _day)
            {
                _day = now.Date;
                _done = false;
                _checkedLog = false;
                _queryFailures = 0;
                _notBefore = default;
            }
            if (now.TimeOfDay < SendAt) return "not_yet";
            if (_done) return "done";
            if (now < _notBefore) return "waiting";
            if (!_checkedLog)
            {
                // 只在今天第一次要发之前查一次：之后发没发成功本进程自己知道
                bool sentBefore = await _alreadySent(now);
                _checkedLog = true;
                if (sentBefore)
                {
                    _done = true;
                    return "done";
                }
            }
            string? to = _receivers()?.Trim();
            if (string.IsNullOrEmpty(to)) return "no_receivers";
            double? balance = await TryQueryAsync();
            string text;
            if (balance != null)
            {
                text = Format(balance.Value, now);
            }
            else
            {
                _queryFailures++;
                if (_queryFailures < QueryFailuresBeforeNotice)
                {
                    _notBefore = now + QueryRetryDelay;
                    return "query_retry";
                }
                text = FormatQueryFailed(now);
            }
            if (await TrySendAsync(text, to))
            {
                _done = true;
                return "sent";
            }
            _notBefore = now + SendRetryDelay;
            return "send_failed";
        }

        async Task<double?> TryQueryAsync()
        {
            try
            {
                return await _queryBalance();
            }
            catch (Exception err)
            {
                Console.WriteLine("[余额日报] 查余额异常：" + err.GetType().Name + " " + err.Message);
                return null;
            }
        }

        async Task<bool> TrySendAsync(string text, string to)
        {
            try
            {
                return await _send(text, to);
            }
            catch (Exception err)
            {
                Console.WriteLine("[余额日报] 发送异常：" + err.GetType().Name + " " + err.Message);
                return false;
            }
        }

        // ---------- 「今天发过没有」看接口日志 ----------

        public static async Task<bool> AlreadySentAsync(ApplicationDBContext db, DateTime now)
        {
            DateTime today = now.Date;
            DateTime tomorrow = today.AddDays(1);
            // memo 条件不能少：同一个 purpose 下取令牌的那条日志，返回里也是 errcode 0
            var replies = await db.webApiLog.AsNoTracking()
                .Where(l => l.create_date >= today && l.create_date < tomorrow
                    && l.purpose == Purpose && l.memo == "下发文本消息")
                .Select(l => l.response)
                .ToListAsync();
            return replies.Any(IsAccepted);
        }

        /// <summary>企业微信收下了没有：返回里带 errcode 且等于 0 才算。</summary>
        public static bool IsAccepted(string? reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return false;
            try
            {
                JToken? code = JObject.Parse(reply)["errcode"];
                return code != null && code.Type == JTokenType.Integer && (long)code == 0;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

        // ---------- 生产接线 ----------

        public static SupplierBalanceDailyReport Create(IServiceScopeFactory scopes)
        {
            async Task<double?> QueryBalance()
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                return await new WanlongZiwoyouHelper(db, config, SkipassSaleRules.SOURCE_DHHS).QueryBalanceOrNull();
            }

            async Task<bool> Send(string text, string to)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var http = scope.ServiceProvider.GetService<IHttpContextAccessor>();
                var res = await new WeComController(db, config, http!).SendText(text, to, Purpose);
                bool accepted = res != null && res.errmsg != null && res.errcode == 0;
                if (accepted && !string.IsNullOrWhiteSpace(res!.invaliduser))
                {
                    // 另一个人已经收到了，仍算发过，免得他一直收到重复消息
                    Console.WriteLine("[余额日报] 有收件人无效：" + res.invaliduser);
                }
                return accepted;
            }

            async Task<bool> AlreadySent(DateTime now)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
                return await AlreadySentAsync(db, now);
            }

            return new SupplierBalanceDailyReport(QueryBalance, Send, AlreadySent);
        }
    }

    /// <summary>
    /// 每分钟看一次要不要发大好河山余额日报。直接在应用里跑，不走 crontab 和公网域名——对外网络断了 crontab 任务会一起失效、而且没有告警。
    /// 任何异常只记一条日志，下一轮再试，不影响别的功能。
    /// </summary>
    public sealed class SupplierBalanceDailyReportJob : BackgroundService
    {
        static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly SupplierBalanceDailyReport _report;

        public SupplierBalanceDailyReportJob(IServiceScopeFactory scopeFactory)
        {
            _report = SupplierBalanceDailyReport.Create(scopeFactory);
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
                    string result = await _report.TickAsync(DateTime.Now);
                    if (result != "not_yet" && result != "done" && result != "waiting")
                    {
                        Console.WriteLine("[余额日报] " + result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[余额日报] 本轮失败：" + ex.Message);
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
    }
}

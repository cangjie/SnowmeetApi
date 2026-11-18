using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;

namespace SnowmeetApi.Services.Ops
{
    /// <summary>
    /// 雪票供应商（自我游）账号预存款偏低时，给企业微信发预警（2026-11-18 用户要求）：
    /// 预定雪票时，如果发现大好河山账号的预存款低于 2000 元，给苍杰和布乖发一条。
    ///
    /// 调用方（SkiPassController 下单、付款后出票）每次查到余额就调一次 Check：
    ///   - 只盯大好河山；万龙自我游和别的来源不管。余额没查到（null）不算，查询失败不等于没钱。
    ///   - 同一个账号 1 小时内只发一条（订票一多不刷屏）；余额回到 2000 以上就清掉计时，下次再掉下来马上提醒。
    ///   - 发送放在后台：不让企业微信的网络慢拖住顾客下单；发送失败只记日志、绝不抛给调用方，5 分钟后下次检查时再试。
    /// 收件人：服务器工作目录 config.skipassBalanceAlertReceivers（一行企业微信 UserId，多个用 | 分隔）；没有这个文件就用默认的两位。
    /// 企业微信接口有 IP 白名单，只能从服务器上发，所以复用 FnbWeComController.SendText（与数据库备份提醒、美团采集提醒同一条通道）。
    /// </summary>
    public sealed class SupplierBalanceAlertService
    {
        /// <summary>低于这个余额（元）就预警；正好等于不预警。</summary>
        public const double Threshold = 2000;

        /// <summary>同一个账号两条预警之间至少隔多久（发送成功之后）。</summary>
        public static readonly TimeSpan RepeatWindow = TimeSpan.FromHours(1);

        /// <summary>发送失败后多久可以再试。</summary>
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

        /// <summary>默认收件人：苍杰、布乖（企业微信 UserId，用 | 分隔）。</summary>
        public const string DefaultReceivers = "Snowmeet-CangJie_1|Snowmeet-Buguai";

        /// <summary>只盯这些账号。</summary>
        static readonly string[] Watched = { SkipassSaleRules.SOURCE_DHHS };

        readonly Func<string, string, Task<bool>> _send;    // (内容, 接收人)
        readonly Func<DateTime> _now;
        readonly Func<string?> _receivers;
        readonly Dictionary<string, DateTime> _nextAllowed = new();
        readonly object _lock = new();
        Task _last = Task.CompletedTask;

        public SupplierBalanceAlertService(Func<string, string, Task<bool>> send, Func<DateTime>? now = null, Func<string?>? receivers = null)
        {
            _send = send;
            _now = now ?? (() => DateTime.Now);
            _receivers = receivers ?? ReadReceivers;
        }

        public static bool IsWatched(string? source) => Watched.Contains((source ?? "").Trim());

        /// <summary>这个账号、这个余额要不要预警：只看大好河山，且低于 2000。</summary>
        public static bool NeedsAlert(string? source, double balance) => IsWatched(source) && balance < Threshold;

        public static string Format(string source, double balance, DateTime now)
        {
            return "【雪票预警】" + source.Trim() + "预存款只剩 ¥" + balance.ToString("0.00") + "，低于 ¥" + Threshold.ToString("0") + "。\n"
                + "请尽快充值：余额不够时顾客订不了票，已付款的票会出票失败并自动退款。\n"
                + now.ToString("MM-dd HH:mm");
        }

        /// <summary>
        /// 查到某个账号的余额之后调用。立即返回，发送在后台：
        /// ignored（不是要盯的账号）/ unknown（没查到余额）/ fine（余额够，已清掉计时）/ throttled（刚发过）/
        /// no_receivers（没有收件人）/ sending（已安排发送）。
        /// </summary>
        public string Check(string? source, double? balance)
        {
            string key = (source ?? "").Trim();
            if (!IsWatched(key)) return "ignored";
            if (balance == null) return "unknown";
            DateTime now = _now();
            lock (_lock)
            {
                if (!NeedsAlert(key, balance.Value))
                {
                    _nextAllowed.Remove(key);
                    return "fine";
                }
                string? to = _receivers();
                if (string.IsNullOrWhiteSpace(to)) return "no_receivers";
                if (_nextAllowed.TryGetValue(key, out DateTime next) && now < next) return "throttled";
                // 先占住这个时段，后台发送失败再改成 5 分钟后重试
                _nextAllowed[key] = now + RepeatWindow;
                _last = Task.Run(() => SendAsync(key, Format(key, balance.Value, now), to!.Trim(), now));
            }
            return "sending";
        }

        async Task SendAsync(string key, string text, string to, DateTime now)
        {
            bool ok = false;
            try
            {
                ok = await _send(text, to);
            }
            catch (Exception err)
            {
                Console.WriteLine("[雪票预警] 发送异常 " + key + "：" + err.GetType().Name + " " + err.Message);
            }
            if (ok) return;
            Console.WriteLine("[雪票预警] 没发出去，5 分钟后下次检查时再试：" + key);
            lock (_lock) _nextAllowed[key] = now + RetryAfterFailure;
        }

        /// <summary>等后台发送结束（测试用）。</summary>
        public Task WhenIdleAsync()
        {
            lock (_lock) return _last;
        }

        // ---------- 服务器上的配置文件（与 config.sqlServer 同一模式：工作目录下、不进 git） ----------

        public static string? ReadReceivers()
        {
            try
            {
                string path = Path.Combine(Util.workingPath, "config.skipassBalanceAlertReceivers");
                if (File.Exists(path))
                {
                    string v = File.ReadAllText(path).Trim();
                    if (v.Length > 0) return v;
                }
            }
            catch
            {
            }
            return DefaultReceivers;
        }

        // ---------- 全局共用的一个实例：SkiPassController 在好几个地方是 new 出来的，拿不到依赖注入，所以用静态入口 ----------

        static SupplierBalanceAlertService? _shared;

        /// <summary>服务启动后由 Startup 设置；没设置（比如单元测试里）就是 null，调用方什么都不发。</summary>
        public static SupplierBalanceAlertService? Shared => _shared;

        public static void UseShared(SupplierBalanceAlertService? service) => _shared = service;

        public static SupplierBalanceAlertService Create(IServiceScopeFactory scopes)
        {
            async Task<bool> Send(string text, string to)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var res = await new FnbWeComController(db, config).SendText(text, to, "雪票预存款预警");
                return res != null && res.errcode == 0;
            }
            return new SupplierBalanceAlertService(Send);
        }
    }
}

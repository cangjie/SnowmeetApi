using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;
using Microsoft.Extensions.Configuration;

namespace SnowmeetApi.Services.Fnb
{
    /// <summary>
    /// 美团管家采集程序（SnowmeetApi/Tools/meituan_collector，跑在门店电脑上）的状态看护。
    /// 采集程序每 5 分钟发一次心跳；服务器据此发企业微信提醒——提醒只能从服务器发，
    /// 因为企业微信接口有 IP 白名单，门店电脑的 IP 不在白名单里。
    /// 提醒两类：
    ///   1. 美团管家登录失效（采集程序上报 need_login）：立即提醒，没恢复每 2 小时再提醒；恢复后发「已恢复」。
    ///   2. 采集电脑停了（关机、断网、程序退出）：超过 20 分钟没收到心跳就提醒，没恢复每 6 小时再提醒；
    ///      程序还在跑、但超过 2 小时没完成一轮抓取（卡住）也提醒。恢复后发「已恢复」。
    /// 状态存在工作目录的 meituan_collector_state.json，服务重启（发布）不丢。
    /// </summary>
    public sealed class MeituanCollectorMonitor
    {
        public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(20);
        public static readonly TimeSpan OfflineRepeat = TimeSpan.FromHours(6);
        public static readonly TimeSpan NeedLoginRepeat = TimeSpan.FromHours(2);
        public static readonly TimeSpan StalledAfter = TimeSpan.FromHours(2);
        public static readonly TimeSpan StalledRepeat = TimeSpan.FromHours(6);
        static readonly TimeSpan Beijing = TimeSpan.FromHours(8);

        public sealed class Heartbeat
        {
            public string status { get; set; } = "ok";          // ok / running / need_login / error
            public string? message { get; set; }
            public string? machine { get; set; }
            public string? version { get; set; }
            public int? seconds_since_last_round { get; set; }   // 距上一轮抓取完成多少秒（用秒数而不是时间，免得两边时钟不一致）
            public bool? last_round_ok { get; set; }
            public int backfill_pending { get; set; }
        }

        public sealed class State
        {
            public DateTime? last_heartbeat_utc { get; set; }
            public string? status { get; set; }
            public string? message { get; set; }
            public string? machine { get; set; }
            public string? version { get; set; }
            public DateTime? last_round_utc { get; set; }
            public bool? last_round_ok { get; set; }
            public int backfill_pending { get; set; }
            public DateTime? need_login_since_utc { get; set; }
            public DateTime? need_login_alerted_utc { get; set; }
            public DateTime? offline_alerted_utc { get; set; }
            public DateTime? stalled_alerted_utc { get; set; }
        }

        readonly Func<DateTime> _utcNow;
        readonly Func<string, Task<bool>> _send;
        readonly string? _statePath;
        readonly SemaphoreSlim _gate = new(1, 1);

        public State Current { get; private set; }

        public MeituanCollectorMonitor(Func<string, Task<bool>> send, Func<DateTime>? utcNow = null, string? statePath = null)
        {
            _send = send;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _statePath = statePath;
            Current = Load();
        }

        State Load()
        {
            try
            {
                if (_statePath != null && File.Exists(_statePath))
                    return JsonConvert.DeserializeObject<State>(File.ReadAllText(_statePath)) ?? new State();
            }
            catch { }
            return new State();
        }

        void Save()
        {
            if (_statePath == null) return;
            try
            {
                string tmp = _statePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Current, Formatting.Indented));
                File.Move(tmp, _statePath, true);
            }
            catch { }
        }

        static string Bj(DateTime? utc) => utc == null ? "未知" : (utc.Value + Beijing).ToString("MM-dd HH:mm");

        static string Span(TimeSpan t) =>
            t.TotalDays >= 1 ? $"{(int)t.TotalDays} 天 {t.Hours} 小时"
            : t.TotalHours >= 1 ? $"{(int)t.TotalHours} 小时 {t.Minutes} 分钟"
            : $"{Math.Max(1, (int)t.TotalMinutes)} 分钟";

        string Where() => string.IsNullOrWhiteSpace(Current.machine) ? "采集电脑" : $"采集电脑（{Current.machine}）";

        /// <summary>收到心跳：更新状态，按需发「登录失效」「已恢复」提醒。返回本次发出的消息（测试用）。</summary>
        public async Task<List<string>> OnHeartbeat(Heartbeat h)
        {
            var sent = new List<string>();
            await _gate.WaitAsync();
            try
            {
                var now = _utcNow();
                var s = Current;

                // 停机后恢复上报
                if (s.offline_alerted_utc != null && s.last_heartbeat_utc != null)
                {
                    string msg = $"【美团采集】{(string.IsNullOrWhiteSpace(h.machine) ? "采集电脑" : $"采集电脑（{h.machine}）")}已恢复上报，" +
                                 $"中断了约 {Span(now - s.last_heartbeat_utc.Value)}。停机期间的订单会自动补抓。";
                    if (await _send(msg)) sent.Add(msg);
                    s.offline_alerted_utc = null;
                }

                s.last_heartbeat_utc = now;
                s.status = h.status;
                s.message = h.message;
                s.machine = h.machine ?? s.machine;
                s.version = h.version ?? s.version;
                s.backfill_pending = h.backfill_pending;
                if (h.seconds_since_last_round != null)
                {
                    s.last_round_utc = now - TimeSpan.FromSeconds(h.seconds_since_last_round.Value);
                    s.last_round_ok = h.last_round_ok;
                }

                if (h.status == "need_login")
                {
                    s.need_login_since_utc ??= now;
                    if (s.need_login_alerted_utc == null || now - s.need_login_alerted_utc.Value >= NeedLoginRepeat)
                    {
                        string again = s.need_login_alerted_utc == null ? "" : $"（已持续 {Span(now - s.need_login_since_utc.Value)}，再次提醒）";
                        string msg = $"【美团采集】美团管家登录已失效{again}，订单暂停抓取。\n" +
                                     $"请到{Where()}上，在美团管家窗口里用验证码登录（验证码请自己输入，出现滑块也请手动完成）。\n" +
                                     $"失效发现时间：{Bj(s.need_login_since_utc)}。登录后会自动补抓这段时间的订单。";
                        if (await _send(msg))
                        {
                            sent.Add(msg);
                            s.need_login_alerted_utc = now;
                        }
                    }
                }
                else if (s.need_login_since_utc != null)
                {
                    if (s.need_login_alerted_utc != null)
                    {
                        string msg = $"【美团采集】美团管家已重新登录，订单抓取已恢复（登录失效约 {Span(now - s.need_login_since_utc.Value)}）。";
                        if (await _send(msg)) sent.Add(msg);
                    }
                    s.need_login_since_utc = null;
                    s.need_login_alerted_utc = null;
                }

                // 卡住后又完成了一轮
                if (s.stalled_alerted_utc != null && s.last_round_utc != null && s.last_round_utc > s.stalled_alerted_utc)
                {
                    string msg = "【美团采集】采集程序已恢复，又开始正常完成抓取。";
                    if (await _send(msg)) sent.Add(msg);
                    s.stalled_alerted_utc = null;
                }
                Save();
            }
            finally
            {
                _gate.Release();
            }
            return sent;
        }

        /// <summary>定时检查：心跳中断、抓取卡住。返回本次发出的消息（测试用）。</summary>
        public async Task<List<string>> Check()
        {
            var sent = new List<string>();
            await _gate.WaitAsync();
            try
            {
                var now = _utcNow();
                var s = Current;
                if (s.last_heartbeat_utc == null) return sent;  // 还从没上报过（没部署采集程序），不提醒

                var silent = now - s.last_heartbeat_utc.Value;
                if (silent >= OfflineAfter)
                {
                    if (s.offline_alerted_utc == null || now - s.offline_alerted_utc.Value >= OfflineRepeat)
                    {
                        string msg = $"【美团采集】{Where()}已经 {Span(silent)}没有上报，订单暂停抓取。\n" +
                                     $"可能是电脑关机、断网，或采集程序退出了。最后一次上报：{Bj(s.last_heartbeat_utc)}。\n" +
                                     "恢复后程序会自动补抓停机期间的订单；如果电脑一直开着，请检查网络和采集程序。";
                        if (await _send(msg))
                        {
                            sent.Add(msg);
                            s.offline_alerted_utc = now;
                        }
                    }
                }
                else if (s.status != "need_login" && s.last_round_utc != null && now - s.last_round_utc.Value >= StalledAfter)
                {
                    // 心跳正常但一直完不成一轮（登录失效时不算，那种情况已单独提醒）
                    if (s.stalled_alerted_utc == null || now - s.stalled_alerted_utc.Value >= StalledRepeat)
                    {
                        string msg = $"【美团采集】采集程序在运行，但已经 {Span(now - s.last_round_utc.Value)}没有完成一轮抓取。\n" +
                                     $"最后一次完成：{Bj(s.last_round_utc)}。程序信息：{s.message ?? "无"}。请到{Where()}上看看浏览器窗口。";
                        if (await _send(msg))
                        {
                            sent.Add(msg);
                            s.stalled_alerted_utc = now;
                        }
                    }
                }
                Save();
            }
            finally
            {
                _gate.Release();
            }
            return sent;
        }

        // ---------- 服务器上的配置文件（与 config.sqlServer 同一模式：工作目录下、不进 git） ----------

        /// <summary>采集程序上报用的令牌：工作目录 config.meituanCollectorToken。文件不存在或为空时一律拒绝。</summary>
        public static string? ReadToken() => ReadConfigLine("config.meituanCollectorToken");

        /// <summary>提醒接收人：工作目录 config.meituanCollectorNotify，一行企业微信 UserId，多个用 | 分隔。缺省不发。</summary>
        public static string? ReadReceivers() => ReadConfigLine("config.meituanCollectorNotify");

        static string? ReadConfigLine(string fileName)
        {
            try
            {
                string path = Path.Combine(Util.workingPath, fileName);
                if (!File.Exists(path)) return null;
                string v = File.ReadAllText(path).Trim();
                return v.Length == 0 ? null : v;
            }
            catch
            {
                return null;
            }
        }

        public static MeituanCollectorMonitor Create(IServiceScopeFactory scopes)
        {
            async Task<bool> Send(string text)
            {
                string? to = ReadReceivers();
                if (to == null)
                {
                    Console.WriteLine("[美团采集提醒] 没有配置接收人（config.meituanCollectorNotify），未发送：" + text);
                    return false;
                }
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var res = await new FnbWeComController(db, config).SendText(text, to, "美团采集提醒");
                return res != null && res.errcode == 0;
            }
            return new MeituanCollectorMonitor(Send, null, Path.Combine(Util.workingPath, "meituan_collector_state.json"));
        }
    }

    /// <summary>每 5 分钟检查一次采集程序的心跳。服务刚启动时先等 6 分钟，给采集程序重新上报的时间。</summary>
    public sealed class MeituanCollectorWatchdog : BackgroundService
    {
        readonly MeituanCollectorMonitor _monitor;

        public MeituanCollectorWatchdog(MeituanCollectorMonitor monitor) => _monitor = monitor;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(6), stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await _monitor.Check();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[美团采集提醒] 检查出错：" + e.Message);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}

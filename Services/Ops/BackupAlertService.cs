using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Ops
{
    /// <summary>
    /// 服务器上的数据库备份脚本（Tools/db_backup）出问题时，通过它给企业微信发提醒。
    /// 提醒只能从这里发：企业微信接口有 IP 白名单，脚本所在的服务器本机不直接调企业微信，而是调本服务的
    /// POST api/BackupAlert/Notify（请求头 X-Backup-Token，与工作目录 config.backupAlertToken 一致才接受）。
    /// 接收人：工作目录 config.backupAlertReceivers（一行企业微信 UserId，多个用 | 分隔）；没有这个文件时
    /// 沿用 config.meituanCollectorNotify；都没有就不发。
    /// 同一条内容 30 分钟内只发一次，避免备份连续失败时刷屏。
    /// </summary>
    public sealed class BackupAlertService
    {
        public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);
        public const int MaxMessageLength = 600;
        static readonly TimeSpan Beijing = TimeSpan.FromHours(8);

        public sealed class Alert
        {
            public string? level { get; set; }      // error / warning / info
            public string? message { get; set; }
        }

        readonly Func<string, string, Task<bool>> _send;   // (内容, 接收人)
        readonly Func<DateTime> _utcNow;
        readonly Dictionary<string, DateTime> _recent = new();
        readonly object _lock = new();

        public BackupAlertService(Func<string, string, Task<bool>> send, Func<DateTime>? utcNow = null)
        {
            _send = send;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>返回：sent / duplicate / no_receivers / empty / send_failed。</summary>
        public async Task<string> Notify(string? level, string? message, string? receivers)
        {
            string clean = Clean(message);
            if (clean.Length == 0) return "empty";
            if (string.IsNullOrWhiteSpace(receivers)) return "no_receivers";
            DateTime now = _utcNow();
            lock (_lock)
            {
                foreach (var k in _recent.Where(p => now - p.Value >= DuplicateWindow).Select(p => p.Key).ToList())
                    _recent.Remove(k);
                if (_recent.ContainsKey(clean)) return "duplicate";
            }
            bool ok = await _send(Format(level, clean, now), receivers.Trim());
            if (!ok) return "send_failed";
            lock (_lock) _recent[clean] = now;
            return "sent";
        }

        public static string Clean(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "";
            var sb = new StringBuilder();
            foreach (char c in message.Trim())
                if (c == '\n' || !char.IsControl(c)) sb.Append(c);
            string s = sb.ToString().Trim();
            return s.Length <= MaxMessageLength ? s : s.Substring(0, MaxMessageLength) + "…";
        }

        public static string Format(string? level, string message, DateTime utcNow)
        {
            string label = level switch
            {
                "error" => "出问题了",
                "warning" => "请留意",
                _ => "通知",
            };
            return "【数据库备份】" + label + "\n" + message + "\n" + (utcNow + Beijing).ToString("MM-dd HH:mm");
        }

        // ---------- 服务器上的配置文件（与 config.sqlServer 同一模式：工作目录下、不进 git） ----------

        public static string? ReadToken() => ReadConfigLine("config.backupAlertToken");

        public static string? ReadReceivers() =>
            ReadConfigLine("config.backupAlertReceivers") ?? ReadConfigLine("config.meituanCollectorNotify");

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

        public static BackupAlertService Create(IServiceScopeFactory scopes)
        {
            async Task<bool> Send(string text, string to)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var res = await new FnbWeComController(db, config).SendText(text, to, "数据库备份提醒");
                return res != null && res.errcode == 0;
            }
            return new BackupAlertService(Send);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;
using SixLabors.ImageSharp;

namespace SnowmeetApi.Services.Tickets;

public interface IMiniTicketTokenProvider { Task<string> GetTokenAsync(bool forceRefresh = false); }
public sealed class MiniTicketTokenProvider(ApplicationDBContext db, IConfiguration config) : IMiniTicketTokenProvider
{
    public Task<string> GetTokenAsync(bool forceRefresh = false) => Task.FromResult(new MiniAppHelperController(db, config).GetAccessToken(forceRefresh));
}
public interface IMiniTicketQrService { Task<byte[]> GetAsync(int batchId, string envVersion = "release"); }

public sealed class MiniTicketQrService(ApplicationDBContext db, IConfiguration config, IFileStorage storage,
    IMiniTicketTokenProvider tokens, HttpClient http) : IMiniTicketQrService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public async Task<byte[]> GetAsync(int batchId, string envVersion = "release")
    {
        envVersion = string.IsNullOrWhiteSpace(envVersion) ? "release" : envVersion.Trim();
        if (envVersion != "release" && envVersion != "trial" && envVersion != "develop")
            throw new ArgumentException("小程序版本必须是 release、trial 或 develop");
        string appId = config["Settings:AppId"]?.Trim();
        if (string.IsNullOrWhiteSpace(appId)) throw new InvalidOperationException("未配置小程序 AppId");
        string purpose = "tmq:" + appId + ":" + batchId + ":" + envVersion;
        if (purpose.Length > 50) throw new InvalidOperationException("小程序码缓存标识超出存储限制");
        await Gate.WaitAsync();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await MiniTicketTransaction.Lock(db, purpose);
            TicketShareBatch batch = await db.ticketShareBatch.AsNoTracking().FirstOrDefaultAsync(b => b.id == batchId && b.valid == 1);
            if (batch == null || !MiniTicketRules.IsMini(batch.share_type)) throw new InvalidOperationException("小程序分享批次不存在或已失效");
            if (!await db.ticketTemplate.AsNoTracking().AnyAsync(t => t.id == batch.template_id && t.valid == 1))
                throw new InvalidOperationException("该券模板已停用");
            UploadFile cached = await db.UploadFile.AsNoTracking().Where(f => f.purpose == purpose).OrderByDescending(f => f.id).FirstOrDefaultAsync();
            if (cached != null)
            {
                byte[] bytes = await storage.ReadAsync(cached.file_path_name, cached.is_web == 1);
                if (bytes == null) throw new IOException("缓存的小程序码文件不存在");
                ValidatePng(bytes);
                await tx.CommitAsync();
                return bytes;
            }
            byte[] png = await Generate(batchId, envVersion);
            using var stream = new MemoryStream(png, false);
            string path = await storage.SaveAsync(stream, "mini-ticket-qr.png", false, "ticket_miniqr_");
            if (string.IsNullOrWhiteSpace(path) || path.Length > 100) throw new IOException("小程序码文件存储失败");
            db.UploadFile.Add(new UploadFile { file_path_name = path, is_web = 0, purpose = purpose, file_type = "image/png", create_date = DateTime.Now });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return png;
        }
        finally { Gate.Release(); }
    }

    private async Task<byte[]> Generate(int batchId, string envVersion)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await tokens.GetTokenAsync(attempt > 0);
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("获取小程序 access token 失败");
            string body = JsonSerializer.Serialize(new { scene = "batch=" + batchId, page = MiniTicketRules.ClaimPage,
                env_version = envVersion, check_path = envVersion == "release", width = 430 });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://api.weixin.qq.com/wxa/getwxacodeunlimit?access_token=" + Uri.EscapeDataString(token), content);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("微信小程序码服务失败：HTTP " + (int)response.StatusCode);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            // WeChat reports business failures as JSON with HTTP 200.
            if (bytes.Length == 0) throw new InvalidOperationException("微信未返回小程序码图片");
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (text.StartsWith("{"))
            {
                using var error = JsonDocument.Parse(text);
                int code = error.RootElement.TryGetProperty("errcode", out var c) ? c.GetInt32() : -1;
                string message = error.RootElement.TryGetProperty("errmsg", out var m) ? m.GetString() : "未知错误";
                if (attempt == 0 && (code == 40001 || code == 40014 || code == 42001)) continue;
                throw new InvalidOperationException("微信小程序码生成失败（" + code + "）：" + message);
            }
            ValidatePng(bytes);
            return bytes;
        }
        throw new InvalidOperationException("获取小程序码失败");
    }

    private static void ValidatePng(byte[] bytes)
    {
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 8 || !bytes.Take(8).SequenceEqual(signature))
            throw new InvalidOperationException("小程序码服务未返回 PNG 图片");
        try { Image.Identify(bytes); }
        catch (Exception e) { throw new InvalidOperationException("小程序码 PNG 图片损坏", e); }
    }
}

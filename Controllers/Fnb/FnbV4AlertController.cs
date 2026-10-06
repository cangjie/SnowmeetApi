using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;
namespace SnowmeetApi.Controllers.Fnb;

public interface IFnbExpirySender
{
    Task<(bool Success, string? MessageId, string? Error)> Send(FnbBatch batch, string name, string receivers, string status);
}
public sealed class FnbExpirySender(ApplicationDBContext db, IConfiguration config) : IFnbExpirySender
{
    public async Task<(bool Success, string? MessageId, string? Error)> Send(FnbBatch b, string name, string receivers, string status)
    {
        var response = await new FnbWeComController(db, config).SendNews(new List<FnbWeComController.NewsArticle> { new() {
            title = "【" + status + "】" + name, description = "批次 " + b.batch_no + " · 到期 " + b.effective_expire.ToString("yyyy/MM/dd"),
            url = "https://mini.snowmeet.top/fnb/b?id=" + b.id, picurl = "https://mini.snowmeet.top/images/logo.png" } }, receivers, "食材过期提醒");
        return (response?.errcode == 0, response?.msgid, response?.errcode == 0 ? null : response?.errmsg ?? "企业微信接口调用失败");
    }
}
[ApiController]
public sealed class FnbV4AlertController(ApplicationDBContext db, IConfiguration config, IFnbExpirySender? sender = null) : FnbV4ControllerBase(db)
{
    // Preserve the existing cron URL and its daily deduplication policy. No inventory write occurs here.
    [HttpGet, Route("api/FnbMaterial/PushExpireAlert")]
    public async Task<ApiResult<object>> PushExpireAlert(string? touser = null, string? sessionKey = null)
    {
        string path = Path.Combine(Util.workingPath, "config.fnbAlertReceivers");
        string receivers = touser?.Trim() ?? (System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : "@all");
        if (string.IsNullOrEmpty(receivers)) receivers = "@all";
        try { receivers = FnbV4Rules.RequiredText(receivers, 1000, "接收人"); } catch (ArgumentException ex) { return Result(1, ex.Message); }
        DateTime now = DateTime.UtcNow, today = now.AddHours(8).Date;
        var batches = await db.fnbBatch.AsNoTracking().Where(x => x.quantity > 0 && x.dispose_status == null).OrderBy(x => x.effective_expire).ToArrayAsync();
        var items = await db.fnbItem.AsNoTracking().ToArrayAsync(); var cats = await db.fnbCategory.AsNoTracking().ToArrayAsync();
        int sent = 0, skipped = 0; var failed = new List<object>();
        foreach (var b in batches)
        {
            var item = items.Single(x => x.id == b.item_id); var cat = cats.Single(x => x.id == item.category_id);
            if (b.effective_expire > today.AddDays(FnbV4Rules.Defaults(item, cat).WarnDays)) continue;
            db.ChangeTracker.Clear();
            bool claimed = false;
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var log = await db.fnbAlertDelivery.SingleOrDefaultAsync(x => x.batch_id == b.id && x.business_date == today);
                if (log?.status != "success" && (log?.status != "pending" || log.attempted_at < now.AddMinutes(-5)))
                {
                    if (log == null) { log = new FnbAlertDelivery { batch_id = b.id, business_date = today }; db.fnbAlertDelivery.Add(log); }
                    else db.Entry(log).State = EntityState.Modified;
                    log.status = "pending"; log.attempted_at = now; log.receivers = receivers; log.error = null;
                    await db.SaveChangesAsync(); claimed = true;
                }
                await tx.CommitAsync();
            }
            catch (Exception ex) when (HasSqlError(ex, 1205) || HasSqlError(ex, 2601) || HasSqlError(ex, 2627) || ex is DbUpdateConcurrencyException)
            { skipped++; continue; }
            if (!claimed) { skipped++; continue; }
            (bool Success, string? MessageId, string? Error) response;
            string status = b.effective_expire < today ? "已过期" : b.effective_expire == today ? "今日到期" : "临期";
            try { response = await (sender ?? new FnbExpirySender(db, config)).Send(b, item.name, receivers, status); }
            catch (Exception) { response = (false, null, "企业微信消息发送失败"); }
            db.ChangeTracker.Clear(); var delivery = await db.fnbAlertDelivery.SingleAsync(x => x.batch_id == b.id && x.business_date == today);
            delivery.status = response.Success ? "success" : "failed"; delivery.message_id = response.MessageId;
            delivery.error = response.Success ? null : "企业微信消息发送失败"; db.Entry(delivery).State = EntityState.Modified; await db.SaveChangesAsync();
            if (response.Success) sent++; else failed.Add(new { batchId = b.id, name = item.name, message = delivery.error });
        }
        return Result(failed.Count > 0 && sent == 0 ? 1 : 0, failed.Count == 0 ? "" : "部分推送失败", new { sent, skipped, failed });
    }
}

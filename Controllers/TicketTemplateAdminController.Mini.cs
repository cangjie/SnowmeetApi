using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

public partial class TicketTemplateAdminController
{
    [HttpGet]
    public Task<ActionResult<ApiResult<object>>> ShareMiniTicketTemplateByStaff(int templateId, string shareType,
        string sessionKey, string sessionType = "wechat_mini_openid") => CreateMiniBatch(templateId, shareType, null, sessionKey, sessionType);

    [HttpGet]
    public Task<ActionResult<ApiResult<object>>> CreateMiniQrCodeBatchByStaff(int templateId, string channel,
        string sessionKey, string sessionType = "wechat_mini_openid") => CreateMiniBatch(templateId, "qrcode", channel, sessionKey, sessionType);

    private async Task<ActionResult<ApiResult<object>>> CreateMiniBatch(int templateId, string shareType, string channel,
        string sessionKey, string sessionType)
    {
        Staff staff = await GetStaff(sessionKey, sessionType);
        if (staff == null || staff.title_level < MIN_LEVEL) return Ok(Deny());
        string mode = (shareType ?? "").Trim();
        if (mode != "personal" && mode != "group" && mode != "qrcode")
            return Ok(new ApiResult<object> { code = 1, message = "分享方式无效", data = null });
        string ch = Util.UrlDecode(channel ?? "").Trim();
        if (mode == "qrcode" && ch == "") return Ok(new ApiResult<object> { code = 1, message = "请填写投放场景", data = null });
        string raw = mode == "personal" ? MiniTicketRules.Personal : mode == "group" ? MiniTicketRules.Group : MiniTicketRules.QrCode;
        var result = await MiniTicketTransaction.Run(_db, async () => {
            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == templateId);
            if (tpl == null || tpl.valid != 1 || tpl.sharable != 1)
                return new ApiResult<object> { code = 1, message = "该模板未开放分享或已停用", data = null };
            if (mode != "qrcode" && tpl.cover_upload_id == null)
                return new ApiResult<object> { code = 1, message = "请先上传分享海报", data = null };
            TicketShareBatch batch = mode != "qrcode" ? null : await _db.ticketShareBatch.AsNoTracking().FirstOrDefaultAsync(
                b => b.staff_id == staff.id && b.template_id == templateId && b.share_type == MiniTicketRules.QrCode && b.channel == ch);
            if (batch == null) {
                batch = new TicketShareBatch { template_id = templateId, staff_id = staff.id, share_type = raw,
                    channel = mode == "qrcode" ? ch : null, max_claims = mode == "personal" ? 1 : null, valid = 1, create_date = DateTime.Now };
                _db.ticketShareBatch.Add(batch);
            } else if (batch.valid != 1) {
                batch.valid = 1; batch.update_date = DateTime.Now; _db.Entry(batch).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            string parameters = "?batchId=" + batch.id + "&sessionKey=" + Uri.EscapeDataString(sessionKey ?? "")
                + "&sessionType=" + Uri.EscapeDataString(sessionType ?? "wechat_mini_openid");
            return new ApiResult<object> { code = 0, message = "", data = new { batchId = batch.id,
                templateName = (tpl.name ?? "").Trim(), shareType = mode,
                sharePath = "/" + MiniTicketRules.ClaimPage + "?batch=" + batch.id,
                posterUrl = "https://mini.snowmeet.top/api/TicketPoster/GenerateMini" + parameters,
                qrCodeUrl = "https://mini.snowmeet.top/api/TicketPoster/GetMiniQrCode" + parameters,
                channel = ch, claimCount = batch.claim_count } };
        });
        return Ok(result);
    }
}

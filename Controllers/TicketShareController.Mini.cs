using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

public partial class TicketShareController
{
    private static ApiResult<object> MiniError(string message) => new() { code = 1, message = message, data = null };

    [HttpGet]
    public async Task<ActionResult<ApiResult<object>>> GetMiniShareBatch(int batchId,
        string sessionKey, string sessionType = "wechat_mini_openid")
    {
        Member member = await new MemberController(_db, _config).GetMemberBySessionKey(Util.UrlDecode(sessionKey ?? ""), sessionType);
        if (member == null) return Ok(MiniError("请先登录"));
        TicketShareBatch batch = await _db.ticketShareBatch.AsNoTracking().FirstOrDefaultAsync(b => b.id == batchId);
        if (batch == null || batch.valid != 1 || !MiniTicketRules.IsMini(batch.share_type)) return Ok(MiniError("分享链接已失效"));
        TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == batch.template_id && t.valid == 1 && t.sharable == 1);
        if (tpl == null) return Ok(MiniError("该券已停用"));
        DateTime today = DateTime.Now.Date, tomorrow = today.AddDays(1);
        string claimedCode;
        if (batch.share_type == MiniTicketRules.QrCode)
            claimedCode = await _db.ticket.AsNoTracking().Where(t => t.template_id == tpl.id && t.member_id == member.id
                && t.valid == 1 && t.create_date >= today && t.create_date < tomorrow).Select(t => t.code).FirstOrDefaultAsync();
        else
            claimedCode = await _db.ticketShareClaim.AsNoTracking().Where(c => c.batch_id == batch.id && c.member_id == member.id
                && (batch.share_type != MiniTicketRules.Group || (c.claim_date >= today && c.claim_date < tomorrow)))
                .Select(c => c.ticket_code).FirstOrDefaultAsync();
        return Ok(new ApiResult<object> { code = 0, message = "", data = new {
            batchId = batch.id, templateName = (tpl.name ?? "").Trim(), templateMemo = (tpl.memo ?? "").Trim(),
            shareType = MiniTicketRules.DisplayType(batch.share_type), claimed = claimedCode != null,
            claimedCode = claimedCode ?? "", soldOut = !TicketShareRules.IsClaimable(batch) } });
    }

    [HttpGet]
    public Task<ActionResult<ApiResult<object>>> ClaimMiniSharedTicket(int batchId, string sessionKey,
        string sessionType = "wechat_mini_openid") => ClaimMini(batchId, sessionKey, sessionType, false);

    [HttpGet]
    public Task<ActionResult<ApiResult<object>>> ClaimMiniQrCodeTicket(int batchId, string sessionKey,
        string sessionType = "wechat_mini_openid") => ClaimMini(batchId, sessionKey, sessionType, true);

    private async Task<ActionResult<ApiResult<object>>> ClaimMini(int batchId, string sessionKey, string sessionType, bool qr)
    {
        Member member = await new MemberController(_db, _config).GetMemberBySessionKey(Util.UrlDecode(sessionKey ?? ""), sessionType);
        if (member == null) return Ok(MiniError("请先登录"));
        ApiResult<object> result = await MiniTicketTransaction.Run(_db, async () => {
            TicketShareBatch batch = await _db.ticketShareBatch.AsNoTracking().FirstOrDefaultAsync(b => b.id == batchId);
            if (batch == null || batch.valid != 1 || (qr ? batch.share_type != MiniTicketRules.QrCode
                : batch.share_type != MiniTicketRules.Personal && batch.share_type != MiniTicketRules.Group))
                return MiniError("分享链接已失效，请使用对应的领取入口");
            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == batch.template_id && t.valid == 1 && t.sharable == 1);
            if (tpl == null) return MiniError("该券已停用");
            DateTime now = DateTime.Now, today = now.Date, tomorrow = today.AddDays(1);
            DateTime expiry = TicketTemplateRules.ResolveTicketExpireDate(tpl, now);
            if (expiry.Date < today) return MiniError("该券已过期");
            bool claimed = qr
                ? await _db.ticket.AsNoTracking().AnyAsync(t => t.template_id == tpl.id && t.member_id == member.id && t.valid == 1 && t.create_date >= today && t.create_date < tomorrow)
                : await _db.ticketShareClaim.AsNoTracking().AnyAsync(c => c.batch_id == batch.id && c.member_id == member.id
                    && (batch.share_type != MiniTicketRules.Group || (c.claim_date >= today && c.claim_date < tomorrow)));
            if (claimed) return MiniError(qr ? "同一种优惠券每天只能领一张，明天再来吧" : "你已经领过这张券了");
            if (!TicketShareRules.IsClaimable(batch)) return MiniError("这次分享的券已经领完了");
            Staff staff = await new StaffController(_db).GetStaffBySocialNum((member.wechatMiniOpenId ?? "").Trim(), "wechat_mini_openid", now);
            if (!TicketTransferRules.IsExemptFromReceiveLimit(staff)) {
                int count = await _db.ticket.AsNoTracking().CountAsync(TicketTransferRules.UsableTicketFilter(member.id, tpl.id, now));
                if (count >= MaxUsableTicketsPerTemplate) return MiniError("您名下未使用的同款券已有 " + count + " 张，用掉一些再来领吧");
            }
            string code = await new TicketController(_db, _config).GetNewTicketCode();
            Ticket ticket = new() { code = code, template_id = tpl.id, name = (tpl.name ?? "").Trim(), memo = (tpl.memo ?? "").Trim(),
                member_id = member.id, open_id = (member.wechatMiniOpenId ?? "").Trim(), start_date = now, expire_date = expiry,
                used = 0, is_active = 1, valid = 1, shared = 0, printed = 0, staff_id = batch.staff_id,
                channel = qr ? (batch.channel ?? "").Trim() : "店员分享", miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(), create_date = now };
            _db.ticket.Add(ticket);
            _db.ticketShareClaim.Add(new TicketShareClaim { batch_id = batch.id, member_id = member.id, ticket_code = code, claim_date = today, create_date = now });
            batch.claim_count++; batch.update_date = now; _db.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return new ApiResult<object> { code = 0, message = "", data = new { code = ticket.code, name = ticket.name } };
        });
        return Ok(result);
    }
}

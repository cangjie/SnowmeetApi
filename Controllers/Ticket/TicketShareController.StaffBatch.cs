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
    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> ClaimStaffBatch(int batchId, string sessionKey, string sessionType = "wechat_mini_openid")
    {
        var member = await new MemberController(_db, _config).GetMemberBySessionKey(Util.UrlDecode(sessionKey ?? ""), sessionType);
        if (member == null) return Ok(MiniError("请先登录"));
        if (string.IsNullOrWhiteSpace(member.cell)) return Ok(MiniError("请先验证手机号"));
        return Ok(await MiniTicketTransaction.Run(_db, async () => {
            var batch = await _db.ticketShareBatch.AsTracking().FirstOrDefaultAsync(b => b.id == batchId && b.share_type == MiniTicketRules.StaffBatch);
            var settings = await _db.staffCouponBatch.AsNoTracking().FirstOrDefaultAsync(s => s.batch_id == batchId);
            if (batch == null || settings == null || batch.valid != 1) return MiniError("分享链接已失效");
            var tpl = await _db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == batch.template_id && t.valid == 1 && t.sharable == 1 && t.hide == 0);
            if (tpl == null) return MiniError("该券已停用");
            DateTime now = DateTime.Now;
            DateTime expiry = TicketTemplateRules.ResolveTicketExpireDate(tpl, now);
            if (expiry.Date < now.Date) return MiniError("该券已过期");
            if (await _db.ticketShareClaim.AsNoTracking().AnyAsync(c => c.batch_id == batchId && c.member_id == member.id)) return MiniError("你已经领取过这个批次");
            int count = settings.per_person_limit;
            if (count < 1 || batch.max_claims == null || batch.claim_count + count > batch.max_claims) return MiniError("这次分享的券已经领完了");
            var codes = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++) {
                string code = await new TicketController(_db, _config).GetNewTicketCode();
                if (string.IsNullOrWhiteSpace(code) || codes.Contains(code)) throw new InvalidOperationException("券码生成失败，请重试");
                codes.Add(code);
                _db.ticket.Add(new Ticket { code = code, template_id = tpl.id, name = (tpl.name ?? "").Trim(), memo = (tpl.memo ?? "").Trim(),
                    member_id = member.id, open_id = (member.wechatMiniOpenId ?? "").Trim(), start_date = now, expire_date = expiry,
                    used = 0, is_active = 1, valid = 1, shared = 0, printed = 0, staff_id = batch.staff_id,
                    channel = "店员批次分享", miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(), create_date = now });
                _db.staffCouponBatchTicket.Add(new StaffCouponBatchTicket { ticket_code = code, batch_id = batch.id, member_id = member.id, create_date = now });
            }
            _db.ticketShareClaim.Add(new TicketShareClaim { batch_id = batch.id, member_id = member.id, ticket_code = codes[0], claim_date = now.Date, create_date = now });
            batch.claim_count += count; batch.update_date = now;
            await _db.SaveChangesAsync();
            return new ApiResult<object> { code = 0, message = "", data = new { code = codes[0], count, name = (tpl.name ?? "").Trim() } };
        }));
    }
}

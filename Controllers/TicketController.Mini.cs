using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

public partial class TicketController
{
    private static ApiResult<Ticket> MiniTransferError(string message) => new() { code = 1, message = message, data = null };
    private static bool CanMiniTransfer(Ticket ticket, DateTime now) => ticket != null && ticket.valid == 1
        && ticket.used != 1 && ticket.is_active == 1 && TransferableTemplateIds.Contains(ticket.template_id)
        && TicketTransferRules.IsNotExpired(ticket, now);

    private Task<bool> IsMiniTransferTemplateEnabled(int templateId) => _context.ticketTemplate.AsNoTracking()
        .AnyAsync(t => t.id == templateId && t.valid == 1 && t.sharable == 1);

    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResult<Ticket>>> SetTicketToMiniShare(string code, string sessionKey,
        string sessionType = "wechat_mini_openid")
    {
        Member member = await new MemberController(_context, _oriConfig).GetMemberBySessionKey(Util.UrlDecode(sessionKey ?? ""), sessionType);
        if (member == null) return Ok(MiniTransferError("用户未登录"));
        var result = await MiniTicketTransaction.Run(_context, async () => {
            Ticket ticket = await _context.ticket.AsNoTracking().FirstOrDefaultAsync(t => t.code == code);
            if (ticket == null || ticket.member_id != member.id) return MiniTransferError("优惠券不存在");
            if (!CanMiniTransfer(ticket, DateTime.Now) || !await IsMiniTransferTemplateEnabled(ticket.template_id))
                return MiniTransferError("该优惠券当前不可转赠");
            DateTime sharedTime = MiniTicketRules.ShareTime();
            // If a user starts two shares within the SQL datetime precision, the new share still needs a new marker.
            if (ticket.shared_time == sharedTime) sharedTime = new System.Data.SqlTypes.SqlDateTime(sharedTime.AddMilliseconds(10)).Value;
            ticket.shared = 1; ticket.shared_time = sharedTime;
            _context.Entry(ticket).State = EntityState.Modified;
            _context.ticketLog.Add(new TicketLog { code = ticket.code, sender_open_id = (member.wechatMiniOpenId ?? "").Trim(),
                accepter_open_id = "", memo = MiniTicketRules.ShareMarker, transact_time = sharedTime });
            await _context.SaveChangesAsync();
            Ticket response = new() { code = ticket.code, name = ticket.name, template_id = ticket.template_id,
                member_id = ticket.member_id, shared = ticket.shared, shared_time = ticket.shared_time, expire_date = ticket.expire_date,
                valid = ticket.valid, is_active = ticket.is_active, open_id = "", memo = ticket.memo };
            return new ApiResult<Ticket> { code = 0, message = "", data = response };
        });
        return Ok(result);
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResult<Ticket>>> AcceptMiniTicket(string code, string memo, string sessionKey,
        string sessionType = "wechat_mini_openid")
    {
        Member accepter = await new MemberController(_context, _oriConfig).GetMemberBySessionKey(Util.UrlDecode(sessionKey ?? ""), sessionType);
        if (accepter == null) return Ok(MiniTransferError("用户未登录"));
        Ticket reward = null; Member sender = null;
        ApiResult<Ticket> result = await MiniTicketTransaction.Run(_context, async () => {
            Ticket ticket = await _context.ticket.AsNoTracking().FirstOrDefaultAsync(t => t.code == code);
            if (!CanMiniTransfer(ticket, DateTime.Now) || !await IsMiniTransferTemplateEnabled(ticket.template_id)
                || !await MiniTicketRules.HasCurrentMarker(_context, ticket))
                return MiniTransferError("该优惠券当前不可接受，链接可能已失效");
            if (ticket.member_id == accepter.id) return MiniTransferError("不能转赠给自己");
            DateTime now = DateTime.Now;
            Staff staff = await new StaffController(_context).GetStaffBySocialNum((accepter.wechatMiniOpenId ?? "").Trim(), "wechat_mini_openid", now);
            if (!TicketTransferRules.IsExemptFromReceiveLimit(staff)) {
                int count = await _context.ticket.AsNoTracking().CountAsync(TicketTransferRules.UsableTicketFilter(accepter.id, ticket.template_id, now));
                if (count >= MaxUsableTicketsPerTemplate) return MiniTransferError("您名下未使用的同款券已有 " + count + " 张，用掉一些再来领吧");
            }
            sender = ticket.member_id == null ? null : await new MemberController(_context, _oriConfig).GetWholeMemberById(ticket.member_id.Value);
            string transferMemo = Util.UrlDecode(memo ?? "").Trim();
            if (transferMemo == "") transferMemo = "接受小程序转赠";
            _context.ticketLog.Add(new TicketLog { code = ticket.code, sender_open_id = (sender?.wechatMiniOpenId ?? ticket.open_id ?? "").Trim(),
                accepter_open_id = (accepter.wechatMiniOpenId ?? "").Trim(), memo = transferMemo, transact_time = now });
            ticket.member_id = accepter.id; ticket.open_id = (accepter.wechatMiniOpenId ?? "").Trim();
            ticket.shared = 0; ticket.shared_time = null;
            _context.Entry(ticket).State = EntityState.Modified;
            // Transfer, completed-transfer audit and reward commit together. Notification follows the commit.
            if (sender != null && !string.IsNullOrWhiteSpace(sender.wechatMiniOpenId)) {
                TicketTemplate tpl = await _context.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == ticket.template_id);
                if (tpl != null) {
                    reward = new Ticket { code = await GetNewTicketCode(), template_id = tpl.id, name = (tpl.name ?? "").Trim(), memo = (tpl.memo ?? "").Trim(),
                        open_id = sender.wechatMiniOpenId.Trim(), member_id = sender.id, valid = 1, is_active = 1, used = 0,
                        start_date = now, expire_date = TicketTransferRules.SeasonEndDate(now), create_date = now, accepted_time = now,
                        create_memo = "转赠被领取回赠", channel = "", miniapp_recept_path = "", oper_open_id = "" };
                    _context.ticket.Add(reward);
                }
            }
            await _context.SaveChangesAsync();
            return new ApiResult<Ticket> { code = 0, message = "", data = ticket };
        });
        if (result.code == 0 && reward != null) {
            try { await NotifyMiniTransferSender(sender.wechatMiniOpenId, reward); } catch { /* Issuance already committed; notification is best effort. */ }
        }
        if (result.data != null) result.data.open_id = "";
        return Ok(result);
    }

    protected virtual Task NotifyMiniTransferSender(string openId, Ticket reward)
    {
        var data = new Dictionary<string, string> {
            ["thing1"] = TicketTransferRules.TruncateThing((reward.name ?? "").Trim()),
            ["time2"] = TicketTransferRules.FormatValidityRange(reward.start_date, reward.expire_date ?? TicketTransferRules.SeasonEndDate(DateTime.Now)),
            ["thing3"] = TicketTransferRules.TruncateThing(AcceptedRewardRemark) };
        return new SubscribeMessageHelper(_context, _oriConfig).Send(openId, TransferAcceptedTemplateId, MyTicketListPage, data);
    }
}

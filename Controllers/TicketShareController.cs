using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    // 店员分享发券的**领取端**（顾客侧，无需店员权限）。
    //
    // 与顾客转赠的分工：转赠是把一张已有的券让出去，走 TicketController 那条
    // ticket_gift_{券码}_{分享时间} 的链路；这里是店员按模板凭空发新券，走分享批次。
    // 两条链路各自独立，互不影响。
    //
    // 领取前必须关注公众号：机制与转赠一致——公众号把关注/扫码事件原样落进 oa_receive，
    // 小程序端轮询这个接口，服务端查 oa_receive 里有没有本批次 scene 的命中记录。
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class TicketShareController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public TicketShareController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        /// <summary>同一模板名下未使用的券上限，与转赠链路同一个口径。</summary>
        const int MaxUsableTicketsPerTemplate = 3;

        /// <summary>
        /// 这次分享的关注校验：查 oa_receive 里有没有扫本批次专属二维码产生的
        /// subscribe（扫码后新关注）或 SCAN（已关注者再次扫码）事件。
        /// 本来就已关注公众号的人直接放行，不用每次都重扫一遍。
        /// </summary>
        [NonAction]
        private async Task<bool> HasFollowedForBatch(TicketShareBatch batch, Member member)
        {
            if (batch == null)
            {
                return false;
            }
            if (member != null && member.following_wechat == 1)
            {
                return true;
            }
            string scene = batch.share_scene;
            string sceneWithPrefix = "qrscene_" + scene;
            return await _db.oAReceive.AnyAsync(r => r.MsgType == "event"
                && (r.Event == "subscribe" || r.Event == "SCAN")
                && (r.EventKey == scene || r.EventKey == sceneWithPrefix));
        }

        /// <summary>
        /// 领取页数据：模板信息 + 本人是否已关注 + 是否已领过 + 批次还能不能领。
        /// 前端据此决定是显示二维码（去关注）、领取按钮、还是「已领过 / 已领完」。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetShareBatch(int batchId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(Util.UrlDecode(sessionKey), sessionType);
            TicketShareBatch batch = await _db.ticketShareBatch.AsNoTracking()
                .FirstOrDefaultAsync(b => b.id == batchId);
            if (batch == null || batch.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "分享链接已失效", data = null });
            }
            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(t => t.id == batch.template_id);
            if (tpl == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "券模板不存在", data = null });
            }

            DateTime today = DateTime.Now.Date;
            TicketShareClaim mine = member == null ? null
                : await _db.ticketShareClaim.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.batch_id == batchId && c.member_id == member.id
                        && (batch.share_type != TicketShareBatch.ShareGroup
                            || (c.claim_date >= today && c.claim_date < today.AddDays(1))));
            bool followed = await HasFollowedForBatch(batch, member);
            bool soldOut = !TicketShareRules.IsClaimable(batch);

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new
                {
                    batchId = batch.id,
                    templateName = (tpl.name ?? "").Trim(),
                    templateMemo = (tpl.memo ?? "").Trim(),
                    shareType = batch.share_type,
                    followed = followed,
                    claimed = mine != null,
                    claimedCode = mine != null ? mine.ticket_code : "",
                    soldOut = soldOut,
                    // 未关注时前端拿它拼公众号带参二维码；已关注就不用显示了
                    scene = batch.share_scene
                }
            });
        }

        /// <summary>
        /// 领券。校验顺序刻意如此：先判「已领过」和「已领完」，再判关注——
        /// 免得让人白关注一次公众号才被告知领不到。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> ClaimSharedTicket(int batchId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(Util.UrlDecode(sessionKey), sessionType);
            if (member == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "请先登录", data = null });
            }
            TicketShareBatch batch = await _db.ticketShareBatch
                .FirstOrDefaultAsync(b => b.id == batchId);
            if (batch == null || batch.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "分享链接已失效", data = null });
            }
            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(t => t.id == batch.template_id);
            if (tpl == null || tpl.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "该券已停用", data = null });
            }

            // 同一批次每人只能领一张。库里还有唯一索引兜底，并发下两个请求同时进来也不会重复发。
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            bool claimed = await _db.ticketShareClaim.AsNoTracking()
                .AnyAsync(c => c.batch_id == batchId && c.member_id == member.id
                    && (batch.share_type != TicketShareBatch.ShareGroup
                        || (c.claim_date >= today && c.claim_date < today.AddDays(1))));
            if (claimed)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "你已经领过这张券了", data = null });
            }
            if (!TicketShareRules.IsClaimable(batch))
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1,
                    message = batch.share_type == TicketShareBatch.SharePersonal
                        ? "这张券已经被别人领走了" : "这次分享的券已经领完了",
                    data = null
                });
            }

            // 领取上限：与转赠链路同一个口径（分享中的计入、过期和已核销的不计入）。
            // 店员豁免同样沿用转赠那套判定。
            StaffController _staffHelper = new StaffController(_db);
            Staff claimerStaff = await _staffHelper.GetStaffBySocialNum(
                (member.wechatMiniOpenId ?? "").Trim(), "wechat_mini_openid", DateTime.Now);
            if (!TicketTransferRules.IsExemptFromReceiveLimit(claimerStaff))
            {
                int usableCount = await _db.ticket.AsNoTracking()
                    .CountAsync(TicketTransferRules.UsableTicketFilter(member.id, batch.template_id, DateTime.Now));
                if (usableCount >= MaxUsableTicketsPerTemplate)
                {
                    return Ok(new ApiResult<object>()
                    {
                        code = 1,
                        message = "您名下未使用的「" + (tpl.name ?? "").Trim() + "」已有 "
                            + usableCount.ToString() + " 张，用掉一些再来领吧",
                        data = null
                    });
                }
            }

            if (!await HasFollowedForBatch(batch, member))
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1, message = "请先扫码关注公众号后再领取", data = null
                });
            }

            TicketController _tHelper = new TicketController(_db, _config);
            string code = await _tHelper.GetNewTicketCode();
            Ticket ticket = new Ticket()
            {
                code = code,
                template_id = tpl.id,
                name = (tpl.name ?? "").Trim(),
                memo = (tpl.memo ?? "").Trim(),
                member_id = member.id,
                open_id = (member.wechatMiniOpenId ?? "").Trim(),
                start_date = now,
                expire_date = TicketTemplateRules.ResolveTicketExpireDate(tpl, now),
                used = 0,
                is_active = 1,
                valid = 1,
                shared = 0,
                printed = 0,
                // 发券人记的是分享的那位店员，不是领取人——后台「优惠券管理」按发券人能查到
                staff_id = batch.staff_id,
                channel = "店员分享",
                miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(),
                create_date = now
            };
            await _db.ticket.AddAsync(ticket);

            await _db.ticketShareClaim.AddAsync(new TicketShareClaim()
            {
                batch_id = batch.id,
                member_id = member.id,
                ticket_code = code,
                claim_date = today,
                create_date = now
            });
            batch.claim_count = batch.claim_count + 1;
            batch.update_date = now;
            _db.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { code = ticket.code, name = ticket.name }
            });
        }

        /// <summary>
        /// 公众号扫码关注后的自动领取。公众号回调没有小程序 session，直接用 OA openid
        /// 定位会员；前端轮询仍保留，作为回调延迟或用户已关注时的兜底。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> ClaimSharedTicketByOaFollow(
            int batchId, string oaOpenId)
        {
            oaOpenId = Util.UrlDecode(oaOpenId ?? "").Trim();
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetWholeMemberByNum(oaOpenId, "wechat_oa_openid");
            if (member == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "未找到对应会员", data = null });
            }

            TicketShareBatch batch = await _db.ticketShareBatch
                .FirstOrDefaultAsync(b => b.id == batchId);
            if (batch == null || batch.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "分享链接已失效", data = null });
            }
            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(t => t.id == batch.template_id);
            if (tpl == null || tpl.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "该券已停用", data = null });
            }

            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            bool claimed = await _db.ticketShareClaim.AsNoTracking()
                .AnyAsync(c => c.batch_id == batchId && c.member_id == member.id
                    && (batch.share_type != TicketShareBatch.ShareGroup
                        || (c.claim_date >= today && c.claim_date < today.AddDays(1))));
            if (claimed)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "你今天已经领过这张优惠券了", data = null });
            }
            if (!TicketShareRules.IsClaimable(batch))
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1,
                    message = batch.share_type == TicketShareBatch.SharePersonal
                        ? "这张券已经被别人领走了" : "这次分享的券已经领完了",
                    data = null
                });
            }
            if (!await HasFollowedForBatch(batch, member))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "请先关注公众号后再领取", data = null });
            }

            StaffController _staffHelper = new StaffController(_db);
            Staff claimerStaff = await _staffHelper.GetStaffBySocialNum(
                (member.wechatMiniOpenId ?? "").Trim(), "wechat_mini_openid", now);
            if (!TicketTransferRules.IsExemptFromReceiveLimit(claimerStaff))
            {
                int usableCount = await _db.ticket.AsNoTracking()
                    .CountAsync(TicketTransferRules.UsableTicketFilter(member.id, batch.template_id, now));
                if (usableCount >= MaxUsableTicketsPerTemplate)
                {
                    return Ok(new ApiResult<object>()
                    {
                        code = 1,
                        message = "您名下未使用的「" + (tpl.name ?? "").Trim() + "」已有 "
                            + usableCount.ToString() + " 张，用掉一些再来领吧",
                        data = null
                    });
                }
            }

            TicketController _tHelper = new TicketController(_db, _config);
            string code = await _tHelper.GetNewTicketCode();
            Ticket ticket = new Ticket()
            {
                code = code,
                template_id = tpl.id,
                name = (tpl.name ?? "").Trim(),
                memo = (tpl.memo ?? "").Trim(),
                member_id = member.id,
                open_id = (member.wechatMiniOpenId ?? "").Trim(),
                oper_open_id = oaOpenId,
                start_date = now,
                expire_date = TicketTemplateRules.ResolveTicketExpireDate(tpl, now),
                used = 0,
                is_active = 1,
                valid = 1,
                shared = 0,
                printed = 0,
                staff_id = batch.staff_id,
                channel = "店员分享",
                miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(),
                create_date = now
            };
            await _db.ticket.AddAsync(ticket);
            await _db.ticketShareClaim.AddAsync(new TicketShareClaim()
            {
                batch_id = batch.id,
                member_id = member.id,
                ticket_code = code,
                claim_date = today,
                create_date = now
            });
            batch.claim_count = batch.claim_count + 1;
            batch.update_date = now;
            _db.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { code = ticket.code, name = ticket.name }
            });
        }

        /// <summary>
        /// 固定二维码扫码领券（员工发券三条途径之三）。给**公众号服务器**调：
        /// 顾客扫码关注/打开公众号 → 公众号收到 subscribe/SCAN 事件（EventKey = ticketqr_{批次id}）
        /// → 调这个接口发券 → 拿返回的 message 拼图文消息回给顾客。
        ///
        /// 没有额外鉴权，与 AcceptTicketByOaFollow 同一个先例：oaOpenId 必须是真实存在的
        /// 公众号用户，且批次必须有效，伪造不出收益。
        ///
        /// 限领口径（用户 2026-08-21 拍板）：**按模板算，一人一天一张**——
        /// 直接查 ticket 表 (template_id + member_id + 当天)，不看是扫的哪张码，
        /// 所以"换一个店员的码再领一次"这个口子是堵死的。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> ClaimByOaScan(int batchId, string oaOpenId)
        {
            oaOpenId = Util.UrlDecode(oaOpenId ?? "").Trim();
            TicketShareBatch batch = await _db.ticketShareBatch
                .FirstOrDefaultAsync(b => b.id == batchId
                    && b.share_type == TicketShareBatch.ShareQrCode);
            if (batch == null || batch.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "该二维码已停用", data = null });
            }
            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(t => t.id == batch.template_id);
            if (tpl == null || tpl.valid != 1)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "该券已停用", data = null });
            }

            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetWholeMemberByNum(oaOpenId, "wechat_oa_openid");
            if (member == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "未找到对应会员", data = null });
            }

            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            DateTime tomorrow = today.AddDays(1);
            // 一人一天一张，**按模板**算：不查批次、不查 channel，所以换一张码也领不到第二张。
            // 口径与公众号侧原来写死在 case 12 里的那段一致，只是从"只对模板12"扩到了全部模板。
            bool gotToday = await _db.ticket.AsNoTracking().AnyAsync(t => t.template_id == tpl.id
                && t.member_id == member.id && t.valid == 1
                && t.create_date >= today && t.create_date < tomorrow);
            if (gotToday)
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1, message = "「" + (tpl.name ?? "").Trim() + "」每天只能领一张，明天再来吧", data = null
                });
            }

            // 名下未使用的同款券上限，与转赠/分享链路同一份口径
            StaffController _staffHelper = new StaffController(_db);
            Staff claimerStaff = await _staffHelper.GetStaffBySocialNum(
                (member.wechatMiniOpenId ?? "").Trim(), "wechat_mini_openid", now);
            if (!TicketTransferRules.IsExemptFromReceiveLimit(claimerStaff))
            {
                int usableCount = await _db.ticket.AsNoTracking()
                    .CountAsync(TicketTransferRules.UsableTicketFilter(member.id, tpl.id, now));
                if (usableCount >= MaxUsableTicketsPerTemplate)
                {
                    return Ok(new ApiResult<object>()
                    {
                        code = 1,
                        message = "您名下未使用的「" + (tpl.name ?? "").Trim() + "」已有 "
                            + usableCount.ToString() + " 张，用掉一些再来领吧",
                        data = null
                    });
                }
            }

            TicketController _tHelper = new TicketController(_db, _config);
            string code = await _tHelper.GetNewTicketCode();
            Ticket ticket = new Ticket()
            {
                code = code,
                template_id = tpl.id,
                name = (tpl.name ?? "").Trim(),
                memo = (tpl.memo ?? "").Trim(),
                member_id = member.id,
                open_id = (member.wechatMiniOpenId ?? "").Trim(),
                oper_open_id = oaOpenId,
                start_date = now,
                expire_date = TicketTemplateRules.ResolveTicketExpireDate(tpl, now),
                used = 0,
                is_active = 1,
                valid = 1,
                shared = 0,
                printed = 0,
                // 发券人 = 这张码归属的店员；channel 用批次上的投放场景，便于分渠道统计
                staff_id = batch.staff_id,
                channel = (batch.channel ?? "").Trim(),
                miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(),
                create_date = now
            };
            await _db.ticket.AddAsync(ticket);

            await _db.ticketShareClaim.AddAsync(new TicketShareClaim()
            {
                batch_id = batch.id,
                member_id = member.id,
                ticket_code = code,
                claim_date = today,
                create_date = now
            });
            batch.claim_count = batch.claim_count + 1;
            batch.update_date = now;
            _db.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { code = ticket.code, name = ticket.name }
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Models;
using SnowmeetApi.Helpers;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class TicketController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration _oriConfig;
        public string _appId = "";

        // 硬编码：允许转赠的优惠券模板（12=免费打蜡券，16=老顾客优惠券）。
        // 不同模板的转赠规则可能不同，目前两者规则一致（未使用+未过期即可转），后续如需差异化按 template_id 拆分判断。
        private static readonly HashSet<int> TransferableTemplateIds = new HashSet<int> { 12, 16 };

        // 领取上限：名下同模板"真正还能用的"券达到这个数就不许再领分享来的券，防止个人囤券。
        // 注意回赠（RewardSenderForAcceptedTransfer）不走这道门槛，分享人自己可能超过上限——
        // 这是业务有意为之：防的是"到处领别人的券"，不是"自己转赠攒下的奖励"。别当 bug 改掉。
        private const int MaxUsableTicketsPerTemplate = 3;

        // 小程序订阅消息：优惠券领取成功提醒（模版编号 38451）
        // 字段 thing1=优惠券名称 / time2=有效期 / thing3=备注，thing 类型上限 20 字
        private const string TransferAcceptedTemplateId = "TsWgivHWG5TT8OVI5hN7n56yCWJ5K8THFBtmmACfek4";
        private const string MyTicketListPage = "pages/mine/ticket/ticket_list";
        private const string AcceptedRewardRemark = "对方已领取，回赠您一张同款券";

        // 判断某会员当前是否处于关注状态：直接读 member.following_wechat——这个字段由
        // SnowmeetOfficialAccount 的 SetFollowingStatus 在每次收到 subscribe/SCAN/unsubscribe
        // 事件时同步维护，是当前关注状态的直接来源，不用再反查 oa_receive 事件历史去推断。
        // 用于「已经关注过、这次换了张新券转赠，不应该要求再扫一次码」的场景。
        [NonAction]
        public bool IsCurrentlyFollowingOA(Member member)
        {
            return member != null && member.following_wechat == 1;
        }
        // 转赠接受前必须关注公众号：用扫码关注生成的 scene（ticket.transfer_scene）去核对
        // oa_receive（微信公众号事件回调落库表，MiniAppHelperController.PushMessage 实时写入）里
        // 有没有对应的 subscribe/SCAN 事件——这两个事件分别对应"扫码后新关注"和"已关注用户再次扫码"，
        // 命中任一个都说明这次扫码确实完成了关注动作。
        // 场景值必须绑定"这一次分享"（ticket.shared_time），不能只绑定券的 code——
        // 否则同一张券换了收件人再转赠时，会复用到上一个收件人（甚至完全无关的人）
        // 历史上留下的扫码/关注记录，导致新收件人明明没扫码却直接判定"已关注"（2026-08-12 真实事故）
        [NonAction]
        public async Task<bool> HasFollowedForTransfer(Ticket ticket, Member accepter = null)
        {
            if (ticket == null || ticket.shared_time == null)
            {
                return false;
            }
            string scene = ticket.transfer_scene;
            string sceneWithPrefix = "qrscene_" + scene;
            bool scannedThisShare = await _context.oAReceive.AnyAsync(r => r.MsgType == "event"
                && (r.Event == "subscribe" || r.Event == "SCAN")
                && (r.EventKey == scene || r.EventKey == sceneWithPrefix));
            if (scannedThisShare)
            {
                return true;
            }
            // 没扫这一次的专属二维码，但如果这个人本来就已经是关注状态（比如接受上一张券时刚关注过），
            // 也应该直接放行，不用每张新券都强制重新扫一次码
            return IsCurrentlyFollowingOA(accepter);
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<bool>>> CheckTransferFollow(string code, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member accepter = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            Ticket ticket = await _context.ticket.FindAsync(code);
            bool followed = await HasFollowedForTransfer(ticket, accepter);
            return Ok(new ApiResult<bool>() { code = 0, message = "", data = followed });
        }

        public TicketController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        [HttpGet("{used}")]
        public async Task<ActionResult<ApiResult<List<Ticket>>>> GetMyTickets(int used,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return Ok(new ApiResult<List<Ticket>>()
                {
                    code = 0,
                    message = "用户未登录",
                    data = null
                });
            }
            List<Ticket> tickets = await _context.ticket
                .Where(t => t.member_id == member.id && t.valid == 1 && t.used == used)
                .OrderBy(t => t.create_date).AsNoTracking().ToListAsync();
            if (used == 0)
            {
                // 这里原本写的是 <=，保留的恰好是已过期的券、把真正还能用的券藏了起来
                // （2026-08-14 修正；口径见 TicketTransferRules.IsNotExpired）
                tickets = tickets.Where(t => TicketTransferRules.IsNotExpired(t, DateTime.Now)).ToList();
            }
            await FillCardFields(tickets,
                used == 1 ? TicketListContext.Used : TicketListContext.Unused,
                (member.wechatMiniOpenId ?? "").Trim());
            return Ok(new ApiResult<List<Ticket>>()
            {
                code = 0,
                message = "",
                data = tickets
            });
        }

        // 给一批券补上卡片要显示的派生字段：那行时间、票根面额、有效期文案、临期标记。
        // 两处数据都要批量捞，不要在循环里逐张查（N+1）：
        //   "我领取的时间"只存在于 ticket_log；面额在 ticket_template 上。
        [NonAction]
        public async Task FillCardFields(List<Ticket> tickets, TicketListContext context, string myOpenId)
        {
            if (tickets == null || tickets.Count == 0)
            {
                return;
            }
            Dictionary<string, DateTime> transferTimes = new Dictionary<string, DateTime>();
            // 未使用列表要区分"转赠领来的"和"自己获得的"，已分享-已接受要拿最后一次转赠时间；
            // 已使用 / 已分享-未接受 用不到转赠记录，省掉这次查询。
            if (context == TicketListContext.Unused || context == TicketListContext.SharedAccepted)
            {
                List<string> codes = tickets.Select(t => t.code).Distinct().ToList();
                bool anySender = context == TicketListContext.SharedAccepted;
                List<TicketLog> logs = await _context.ticketLog
                    .Where(TicketTransferRules.TransferLogFilter())
                    .Where(l => codes.Contains(l.code)
                                && (anySender || l.accepter_open_id == myOpenId))
                    .AsNoTracking().ToListAsync();
                foreach (IGrouping<string, TicketLog> g in logs.GroupBy(l => l.code))
                {
                    transferTimes[g.Key] = g.OrderByDescending(x => x.transact_time)
                        .ThenByDescending(x => x.id).First().transact_time;
                }
            }
            // 票根面额来自模板，一次批量查
            List<int> templateIds = tickets.Select(t => t.template_id).Distinct().ToList();
            var templateValues = await _context.ticketTemplate
                .Where(tt => templateIds.Contains(tt.id))
                .Select(tt => new { tt.id, tt.currency_value })
                .AsNoTracking().ToListAsync();

            DateTime now = DateTime.Now;
            foreach (Ticket t in tickets)
            {
                DateTime? tt = transferTimes.ContainsKey(t.code) ? transferTimes[t.code] : (DateTime?)null;
                TicketDisplayTime d = TicketTransferRules.ResolveDisplayTime(t, context, tt);
                t.displayTimeLabel = d.Label;
                t.displayTimeText = d.Text;

                var tv = templateValues.FirstOrDefault(x => x.id == t.template_id);
                t.currencyValue = tv != null ? tv.currency_value : 0;
                t.expireText = TicketTransferRules.FormatExpire(t.expire_date);
                t.expireUrgent = TicketTransferRules.IsExpiringSoon(t, now);
            }
        }

        // "我的优惠券-已分享"列表：包含两部分——① 我当前还持有、正在分享中等对方接受的券；
        // ② 我曾经转赠出去、对方已经接受的券（此时 ticket.member_id 已经改成对方了，
        // 单靠 ticket 表按 member_id 查是查不到的，所以这部分要从 ticket_log 里找"我作为
        // sender、且确实有 accepter"的转赠成功记录反查券码）。这样即使券已经转出去了，
        // 在我这边也能留一份"送出去过"的历史记录，而不是转赠成功后就凭空消失。
        //
        // 注意"来回转赠"的情况：我送给对方、对方又送回给我、我又接受了——这时这张券的
        // "最新一次转赠成功"记录里 sender 是对方不是我，它现在就是我手上一张普通未使用的券，
        // 不应该再挂在我的"已分享"历史里。所以判断标准是"这张券最新一条转赠成功记录的
        // sender 是不是我"，不是"我有没有转赠成功过"——同一张券反复转手，只认最后一次。
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Ticket>>>> GetMySharedTickets(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return Ok(new ApiResult<List<Ticket>>() { code = 0, message = "用户未登录", data = null });
            }

            List<Ticket> pending = await _context.ticket
                .Where(t => t.member_id == member.id && t.shared == 1 && t.valid == 1)
                .AsNoTracking().ToListAsync();

            string myOpenId = (member.wechatMiniOpenId ?? "").Trim();
            List<Ticket> accepted = new List<Ticket>();
            if (!myOpenId.Equals(""))
            {
                // 只认真转赠的日志，口径统一走 TicketTransferRules.TransferLogFilter()。
                // 原来这里只判 "accepter 非空且不等于 sender"，会把核销日志（sender 是空串）
                // 和店员开单发券日志（收发本来就是两个人）都当成转赠——店员账号的"已分享"
                // 会被塞满他从没转赠过的顾客券（2026-08-15 生产库实测：4158 条 vs 真实 17 条）。
                List<TicketLog> myAcceptLogs = await _context.ticketLog
                    .Where(TicketTransferRules.TransferLogFilter())
                    .Where(l => l.sender_open_id == myOpenId)
                    .AsNoTracking().ToListAsync();
                List<string> candidateCodes = myAcceptLogs.Select(l => l.code).Distinct().ToList();

                if (candidateCodes.Count > 0)
                {
                    // 同一份口径：否则核销日志会成为该券"最新一条转赠记录"（它的 sender 是空串），
                    // 导致转赠出去又被对方核销的券从我的"已分享"里凭空消失。
                    List<TicketLog> allAcceptLogsForCandidates = await _context.ticketLog
                        .Where(TicketTransferRules.TransferLogFilter())
                        .Where(l => candidateCodes.Contains(l.code))
                        .AsNoTracking().ToListAsync();
                    HashSet<string> stillMineToShow = allAcceptLogsForCandidates
                        .GroupBy(l => l.code)
                        .Where(g => g.OrderByDescending(x => x.transact_time).ThenByDescending(x => x.id)
                            .First().sender_open_id == myOpenId)
                        .Select(g => g.Key)
                        .ToHashSet();
                    if (stillMineToShow.Count > 0)
                    {
                        accepted = await _context.ticket
                            .Where(t => stillMineToShow.Contains(t.code) && t.valid == 1)
                            .AsNoTracking().ToListAsync();
                        // 这批券已经被对方接受、不在我名下了。必须显式告诉前端，
                        // 不能让它拿 shared 字段反推归属（见 Ticket.transferredOut 注释）。
                        accepted.ForEach(t => t.transferredOut = true);
                    }
                }
            }

            // 两个桶要显示的时间不一样：还在等对方接受的显示"分享时间"，
            // 已经被对方领走的显示"对方领取时间"，所以分开算再合并。
            await FillCardFields(pending, TicketListContext.SharedPending, myOpenId);
            await FillCardFields(accepted, TicketListContext.SharedAccepted, myOpenId);

            HashSet<string> pendingCodes = pending.Select(t => t.code).ToHashSet();
            List<Ticket> merged = pending
                .Concat(accepted.Where(t => !pendingCodes.Contains(t.code)))
                .GroupBy(t => t.code).Select(g => g.First())   // 按 code 兜底去重
                .OrderByDescending(t => t.shared_time ?? t.create_date)
                .ToList();

            return Ok(new ApiResult<List<Ticket>>()
            {
                code = 0,
                message = "",
                data = merged
            });
        }
        /// <summary>
        /// Old Season
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TicketTemplate>>> GetTemplateList()
        {
            var list = await _context.ticketTemplate.Where<TicketTemplate>(tt => tt.hide == 0).ToListAsync();
            return Ok(list);
        }
        [HttpGet("{templateId}")]
        public async Task<ActionResult<TicketTemplate>> GetTicketTemplateById(int templateId)
        {
            return Ok(await _context.ticketTemplate.FindAsync(templateId));
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetUnusedTicketsByCode(string ticketCodeArr)
        {
            ticketCodeArr = Util.UrlDecode(ticketCodeArr);
            var ticketArr = await _context.ticket
                .FromSqlRaw(" select * from ticket where used = 0 and code in ("
                + ticketCodeArr.Replace("'", "").Trim() + ") ")
                .ToListAsync();
            return ticketArr;
        }
        [HttpGet]
        [ActionName("GetChannels")]
        public async Task<ActionResult<IEnumerable<string>>> GetChannels()
        {
            return await _context.ticket
                .Where(tt => !tt.channel.Trim().Equals(""))
                .Select(tt => tt.channel).Distinct().ToListAsync();
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<Ticket>>> SetTicketToShare(string code, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);

            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "用户未登录", data = null });
            }

            Ticket ticket = await _context.ticket.FindAsync(code);
            if (ticket == null || ticket.member_id != member.id)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "优惠券不存在", data = null });
            }
            if (!TransferableTemplateIds.Contains(ticket.template_id))
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "该优惠券不支持转赠", data = null });
            }
            if (ticket.valid != 1 || ticket.used == 1)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "该优惠券当前不可转赠", data = null });
            }

            ticket.shared = 1;
            ticket.shared_time = DateTime.Now;

            _context.Entry(ticket).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            ticket.open_id = "";

            return Ok(new ApiResult<Ticket>() { code = 0, message = "", data = ticket });
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<Ticket>>> AcceptTicket(string code, string memo, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            memo = Util.UrlDecode(memo).Trim();
            sessionKey = Util.UrlDecode(sessionKey);

            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member accepter = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (accepter == null)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "用户未登录", data = null });
            }
            return Ok(await AcceptTicketCore(code, accepter, memo));
        }

        // 公众号那边收到"扫码关注"事件后，直接调这个接口自动完成接受，不再依赖小程序端轮询触发。
        // 用 oaOpenId（不是 sessionKey）定位接受人，因为触发点是公众号服务器回调，
        // 压根没有小程序会话——只有微信推给我们的这个关注者的公众号 openid。
        // 没有额外鉴权：安全性完全靠 AcceptTicketCore 内部的 HasFollowedForTransfer 兜底——
        // 就算有人猜到券码和某人的 openid 直接调这个接口，没有一条真实的 subscribe/SCAN 事件
        // 命中这张券当前的场景值，一样会被拒绝，伪造不出关注记录。
        [HttpGet]
        public async Task<ActionResult<ApiResult<Ticket>>> AcceptTicketByOaFollow(string code, string oaOpenId)
        {
            oaOpenId = Util.UrlDecode(oaOpenId).Trim();
            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member accepter = await _memberHelper.GetWholeMemberByNum(oaOpenId, "wechat_oa_openid");
            if (accepter == null)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "未找到对应会员", data = null });
            }
            return Ok(await AcceptTicketCore(code, accepter, "扫码关注公众号后自动接受"));
        }

        [NonAction]
        public async Task<ApiResult<Ticket>> AcceptTicketCore(string code, Member accepter, string memo)
        {
            Ticket ticket = await _context.ticket.FindAsync(code);
            if (ticket == null || ticket.shared != 1)
            {
                return new ApiResult<Ticket>() { code = 1, message = "该优惠券当前不可接受，链接可能已失效", data = null };
            }
            if (ticket.member_id == accepter.id)
            {
                return new ApiResult<Ticket>() { code = 1, message = "不能转赠给自己", data = null };
            }
            // 领取上限查在关注校验之前：否则用户白关注一次公众号，才被告知自己券太多领不了。
            // 计数口径见 TicketTransferRules.UsableTicketFilter（分享中的计入、过期和已核销的不计入）。
            // 店员豁免：按接受人的小程序 openid 反查在职店员（GetStaffBySocialNum 内含在职时间窗判断）。
            // 反查不到就按普通顾客处理，限制照常生效。
            StaffController _staffHelper = new StaffController(_context);
            Staff accepterStaff = await _staffHelper.GetStaffBySocialNum(
                (accepter.wechatMiniOpenId ?? "").Trim(), "wechat_mini_openid", DateTime.Now);
            if (!TicketTransferRules.IsExemptFromReceiveLimit(accepterStaff))
            {
                int usableCount = await _context.ticket.AsNoTracking()
                    .CountAsync(TicketTransferRules.UsableTicketFilter(accepter.id, ticket.template_id, DateTime.Now));
                if (usableCount >= MaxUsableTicketsPerTemplate)
                {
                    return new ApiResult<Ticket>()
                    {
                        code = 1,
                        message = "您名下未使用的「" + (ticket.name ?? "").Trim() + "」已有 " + usableCount.ToString()
                            + " 张，用掉一些再来领吧",
                        data = null
                    };
                }
            }
            if (!await HasFollowedForTransfer(ticket, accepter))
            {
                return new ApiResult<Ticket>() { code = 1, message = "请先关注公众号后再接受这张优惠券", data = null };
            }

            // 必须用 GetWholeMemberById 而不是 FindAsync：wechatMiniOpenId 是遍历
            // memberSocialAccounts 算出来的计算属性，而 FindAsync 不加载导航属性、
            // 项目也没开延迟加载 —— 拿到的 sender 那个属性恒为空。
            // 原来只写 ticket_log 时有 `?? ticket.open_id` 兜底所以看不出来，但回赠要靠
            // 这个 openid 发券和发订阅消息，取不到就会静默跳过（2026-08-15 实测踩到）。
            MemberController _senderHelper = new MemberController(_context, _oriConfig);
            Member sender = ticket.member_id == null ? null
                : await _senderHelper.GetWholeMemberById((int)ticket.member_id);

            TicketLog log = new TicketLog()
            {
                code = ticket.code,
                sender_open_id = (sender?.wechatMiniOpenId ?? ticket.open_id ?? "").Trim(),
                accepter_open_id = (accepter.wechatMiniOpenId ?? "").Trim(),
                memo = memo,
                transact_time = DateTime.Now
            };
            await _context.AddAsync(log);

            ticket.member_id = accepter.id;
            ticket.open_id = (accepter.wechatMiniOpenId ?? "").Trim();
            ticket.shared = 0;
            ticket.shared_time = null;

            _context.Entry(ticket).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            NotifyAcceptedByOA(ticket, accepter);
            await RewardSenderForAcceptedTransfer(ticket, sender);

            ticket.open_id = "";

            return new ApiResult<Ticket>() { code = 0, message = "", data = ticket };
        }

        // 对方领取成功后，回赠分享人一张同款券，并用小程序订阅消息通知他。
        //
        // 为什么通知分享人只能走小程序订阅消息、不能走公众号客服消息：客服消息有额度窗口，
        // 「关注服务号」「扫描二维码」都只有 3 条 / 1 分钟（「用户主动发消息」才是 5 条 / 48 小时）。
        // 分享人在对方领取的那一刻早就不在任何窗口里，公众号那条路发不出去。
        //
        // 顺序上先发券、发成功了才发消息——不能让消息说"已回赠"但券其实没发出来。
        // 整个方法吞异常：接受动作已经落库成功，回赠或通知失败绝不能反过来把它弄失败。
        [NonAction]
        public async Task RewardSenderForAcceptedTransfer(Ticket acceptedTicket, Member sender)
        {
            try
            {
                if (sender == null)
                {
                    return;
                }
                string senderOpenId = (sender.wechatMiniOpenId ?? "").Trim();
                if (senderOpenId.Equals(""))
                {
                    // 没有小程序 openid，既发不了券（GenerateTicketByAction 要写 open_id）也发不了消息
                    return;
                }
                DateTime seasonEnd = TicketTransferRules.SeasonEndDate(DateTime.Now);
                // 有效期必须现算，不能抄模板：template 12「免费打蜡券」的 expire_date 是 2024-12-07
                // 早就过期，template 16 是 NULL 会被写成 9999 年，照抄两个都是废值。
                Ticket reward = await GenerateTicketByAction(acceptedTicket.template_id, sender.id,
                    1, 0, "转赠被领取回赠", "", seasonEnd);
                if (reward == null)
                {
                    return;
                }
                Dictionary<string, string> data = new Dictionary<string, string>()
                {
                    ["thing1"] = TicketTransferRules.TruncateThing((reward.name ?? "").Trim()),
                    ["time2"] = TicketTransferRules.FormatValidityRange(reward.start_date,
                        reward.expire_date == null ? seasonEnd : (DateTime)reward.expire_date),
                    ["thing3"] = TicketTransferRules.TruncateThing(AcceptedRewardRemark)
                };
                SubscribeMessageHelper msgHelper = new SubscribeMessageHelper(_context, _oriConfig);
                await msgHelper.Send(senderOpenId, TransferAcceptedTemplateId, MyTicketListPage, data);
            }
            catch
            {
                // 见方法头注释：绝不能把已经成功的接受动作弄失败
            }
        }

        // 接受成功后，通过公众号给接收人推一条确认消息（点进去直接是"我的优惠券"）。
        // 用公众号而不是小程序内提示，是因为很多人接受的时候刚从"扫码关注"流程过来，
        // 人还留在微信对话里，不一定会回到小程序页面。
        // 发送失败不影响已经成功的接受动作，所以吞掉异常。
        [NonAction]
        public void NotifyAcceptedByOA(Ticket ticket, Member accepter)
        {
            try
            {
                List<MemberSocialAccount> msaOaList = accepter.GetInfo("wechat_oa_openid");
                if (msaOaList == null || msaOaList.Count == 0)
                {
                    return;
                }
                string oaOpenId = msaOaList[0].num?.Trim();
                if (string.IsNullOrEmpty(oaOpenId))
                {
                    return;
                }
                string content = "您已经接受了" + ticket.name.Trim() + "，"
                    + "<a data-miniprogram-appid=\"wxd1310896f2aa68bb\" data-miniprogram-path=\"/pages/mine/ticket/ticket_list\" >点击查看</a>。";
                string notifyUrl = "https://wxoa.snowmeet.top/api/OfficialAccountApi/SendTextMessageByOpenId?openId="
                    + Util.UrlEncode(oaOpenId) + "&content=" + Util.UrlEncode(content);
                Util.GetWebContent(notifyUrl);
            }
            catch
            {
            }
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<Ticket>>> CancelShare(string code, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);

            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "用户未登录", data = null });
            }

            Ticket ticket = await _context.ticket.FindAsync(code);
            if (ticket == null || ticket.member_id != member.id)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "优惠券不存在", data = null });
            }
            if (ticket.shared != 1)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "该优惠券当前不是分享中状态", data = null });
            }

            TicketLog log = new TicketLog()
            {
                code = ticket.code,
                sender_open_id = (member.wechatMiniOpenId ?? "").Trim(),
                accepter_open_id = "",
                memo = "撤回分享",
                transact_time = DateTime.Now
            };
            await _context.AddAsync(log);

            ticket.shared = 0;
            ticket.shared_time = null;

            _context.Entry(ticket).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            ticket.open_id = "";

            return Ok(new ApiResult<Ticket>() { code = 0, message = "", data = ticket });
        }

        // GET: api/Ticket/5
        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<Ticket>>> GetTicket(string code)
        {
            var ticket = await _context.ticket.FindAsync(code);

            if (ticket == null)
            {
                return NotFound();
            }

            ticket.open_id = "";

            return Ok(new ApiResult<Ticket>()
            {
                code = 0,
                message = "",
                data = ticket
            });
        }


        [HttpGet("{templateId}")]
        public async Task<ActionResult<Ticket>> GenerateTicketsByUser(int templateId, string sessionKey, string source = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            string channel = Util.UrlDecode(source);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            int retryTimes = 0;
            bool isDuplicate = true;
            string code = Util.GetRandomCode(9);
            for (; isDuplicate && retryTimes < 1000;)
            {
                isDuplicate = _context.card.Any(e => e.card_no == code);
            }

            if (isDuplicate)
            {
                return NoContent();
            }

            TicketTemplate template = await _context.ticketTemplate.FindAsync(templateId);

            Card card = new Card
            {
                card_no = code,
                is_ticket = 1,
                type = ""
            };
            await _context.card.AddAsync(card);
            await _context.SaveChangesAsync();
            Ticket ticket = new Ticket
            {
                code = code,
                template_id = templateId,
                name = template.name.Trim(),
                memo = template.memo.Trim(),
                oper_open_id = user.miniAppOpenId.Trim(),
                printed = 0,
                used = 0,
                miniapp_recept_path = template.miniapp_recept_path.Trim(),
                open_id = user.miniAppOpenId.Trim(),
                create_date = DateTime.Now,
                channel = channel.Trim(),
                expire_date = ((template.expire_date == null) ? DateTime.MaxValue : (DateTime)template.expire_date)

            };
            await _context.ticket.AddAsync(ticket);
            await _context.SaveChangesAsync();
            return Ok(ticket);
        }

        [HttpGet("{templateId}")]
        public async Task<ActionResult<Ticket[]>> GenerateTickets(int templateId, int count, string sessionKey, string channel = "")
        {
            TicketTemplate template = _context.ticketTemplate.Find(templateId);
            if (template == null)
            {
                return NoContent();
            }

            sessionKey = Util.UrlDecode(sessionKey);

            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey);

            /*
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
            {
                return NoContent();
            }
            */

            Ticket[] tickets = new Ticket[count];
            for (int i = 0; i < count; i++)
            {
                int retryTimes = 0;
                bool isDuplicate = true;
                string code = Util.GetRandomCode(9);
                for (; isDuplicate && retryTimes < 1000;)
                {
                    isDuplicate = _context.card.Any(e => e.card_no == code);
                }
                if (isDuplicate)
                {
                    continue;
                }
                Card card = new Card
                {
                    card_no = code,
                    is_ticket = 1,
                    type = ""
                };
                _context.card.Add(card);
                await _context.SaveChangesAsync();
                Ticket ticket = new Ticket
                {
                    code = code,
                    template_id = templateId,
                    name = template.name.Trim(),
                    memo = template.memo.Trim(),
                    oper_open_id = member.wechatMiniOpenId.Trim(),
                    printed = 0,
                    used = 0,
                    miniapp_recept_path = template.miniapp_recept_path.Trim(),
                    open_id = "",
                    create_date = DateTime.Now,
                    channel = channel.Trim()

                };
                _context.ticket.Add(ticket);
                bool insertTicketSuccess = true;
                try
                {
                    await _context.SaveChangesAsync();
                    tickets[i] = ticket;
                }
                catch
                {
                    insertTicketSuccess = false;
                    _context.ticket.Remove(ticket);


                }
                if (!insertTicketSuccess)
                {
                    card = await _context.card.FindAsync(code);
                    _context.card.Remove(card);
                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateException exp)
                    {
                        Console.WriteLine(exp.ToString());
                    }
                }


            }
            return tickets;
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<Ticket>> SetPrinted(string code, string sessionKey)
        {

            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user.isAdmin)
            {
                Ticket ticket = await _context.ticket.FindAsync(code);
                ticket.printed = 1;
                _context.Entry<Ticket>(ticket).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return ticket;
            }
            else
            {
                return NoContent();
            }
        }
        /*
        [HttpGet("{used}")]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetMyTickets(int used, string sessionKey)
        {
            
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || user.miniAppOpenId == null || user.miniAppOpenId.Trim().Equals(""))
            {
                return NotFound();
            }

            return await _context.ticket.Where<Ticket>(t => (t.open_id == user.miniAppOpenId && t.used == used)).OrderByDescending(t=>t.create_date).ToListAsync();
        }
        */
        [HttpGet("{code}")]
        public async Task<ActionResult<bool>> Bind(string code, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null)
            {
                return NotFound();
            }
            Ticket ticket = await _context.ticket.FindAsync(code.Trim());
            if (ticket == null)
            {
                return NoContent();
            }
            ticket.open_id = user.miniAppOpenId.Trim();
            _context.Entry<Ticket>(ticket).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        [HttpGet("{used}")]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetTicketsByUser(int used, string openId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (!(await Util.IsAdmin(sessionKey, _context)))
            {
                return NoContent();
            }
            var ticketList = await _context.ticket.Where(t => (t.open_id.Trim().Equals(openId.Trim()) && t.used == used))
            .OrderByDescending(t => t.create_date).ToListAsync();
            return ticketList;
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<Ticket>> Use(string code, string sessionKey)
        {
            if (code.Trim().Equals(""))
            {
                return NotFound();
            }

            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }


            Ticket ticket = await _context.ticket.FindAsync(code);
            if (ticket == null || ticket.is_active != 1)
            {
                return NotFound();
            }
            ticket.used = 1;
            ticket.used_time = DateTime.Now;
            TicketLog tLog = new TicketLog()
            {
                code = code,
                sender_open_id = "",
                accepter_open_id = user.miniAppOpenId.Trim(),
                memo = "核销",
                transact_time = DateTime.Now
            };
            await _context.ticketLog.AddAsync(tLog);
            _context.Entry(ticket).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(ticket);

        }



        [NonAction]
        // expireDate：不传则维持原有行为（抄模板的 expire_date，模板为空则 DateTime.MaxValue）；
        // 传了就用传进来的，给"回赠券按雪季末算有效期"这种需要现算的场景用。
        public async Task<Ticket> GenerateTicketByAction(int templateId, int memberId, int isActive = 1, int orderId = 0, string createMemo = "", string channel = "", DateTime? expireDate = null)
        {
            TicketTemplate template = await _context.ticketTemplate.FindAsync(templateId);
            if (template == null)
            {
                return null;
            }
            MemberController _memberHelper = new MemberController(_context, _config);
            Member member = await _context.member.FindAsync(memberId);
            member.memberSocialAccounts = await _context.memberSocialAccount
                .Where(m => m.member_id == memberId).AsNoTracking().ToListAsync();
            if (orderId > 0 && createMemo.Trim().Equals(""))
            {
                OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
                if (order == null)
                {
                    return null;
                }
                if (order.type.Trim().Equals("雪票"))
                {
                    createMemo = "买雪票增券";
                }
            }
            string code = await GetNewTicketCode();
            Ticket ticket = new Ticket()
            {
                code = code,
                template_id = templateId,
                open_id = member.wechatMiniOpenId.Trim(),
                used = 0,
                accepted_time = DateTime.Now,
                name = template.name.Trim(),
                memo = template.memo.Trim(),
                expire_date = expireDate != null ? (DateTime)expireDate
                    : (template.expire_date == null ? DateTime.MaxValue : (DateTime)template.expire_date),
                // 补 start_date：原来这里不写，券落库后 start_date 为空。语义上等价于"立即生效"
                // （GetMyTickets 的已生效判断是 start_date == null || start_date <= today），
                // 但订阅消息要显示"有效期从哪天起"，得有个真值可取。
                start_date = DateTime.Now,
                oper_open_id = "",
                printed = 0,
                miniapp_recept_path = "",
                create_date = DateTime.Now,
                channel = channel.Trim(),
                order_id = orderId == 0 ? null : orderId,
                create_memo = createMemo,
                is_active = isActive,
                member_id = memberId,
                valid = 1,

            };
            await _context.ticket.AddAsync(ticket);
            await _context.SaveChangesAsync();
            return ticket;
        }

        [NonAction]
        public async Task Cancel(int orderId)
        {
            List<Ticket> ticketArr = await _context.ticket
                .Where(t => t.used == 0 && t.order_id == orderId).ToListAsync();
            for (int i = 0; i < ticketArr.Count; i++)
            {
                Ticket ticket = ticketArr[i];
                ticket.used = 0;
                ticket.used_time = DateTime.Now;
                ticket.use_memo = "订单取消";
                _context.ticket.Entry(ticket).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }

        [NonAction]
        public async Task ActiveTicket(int orderId)
        {
            var tl = await _context.ticket.Where(t => t.order_id == orderId
                && t.used == 0 && t.is_active == 0).ToListAsync();
            for (int i = 0; i < tl.Count; i++)
            {
                tl[i].is_active = 1;
                _context.ticket.Entry(tl[i]).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }
        [HttpGet("{templateId}")]
        public async Task<ActionResult<List<Ticket>>> MeGetTickListByMember(int templateId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            List<Ticket> tl = await _context.ticket.Where(t => t.open_id.Trim().Equals(member.wechatMiniOpenId.Trim())
                && t.template_id == templateId).OrderByDescending(t => t.create_date).AsNoTracking().ToListAsync();
            return Ok(tl);
        }
        [HttpGet("{templateId}")]
        public async Task<ActionResult<Ticket>> MePickTicket(int templateId, string channel,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            int count = (int)((OkObjectResult)(await MeGetPickCount(templateId)).Result).Value;
            if (count > 0)
            {
                return NoContent();
            }
            sessionKey = Util.UrlDecode(sessionKey);
            MemberController _memberHelper = new MemberController(_context, _oriConfig);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            Ticket ticket = await GenerateTicketByAction(templateId, member.id, 1, 0, "扫码领取", channel);
            return Ok(ticket);
        }
        [HttpGet]
        public async Task<ActionResult<int>> MeGetPickCount(int templateId)
        {
            List<Ticket> tl = await _context.ticket.Where(t => (t.template_id == templateId
                && (t.channel.Trim().StartsWith("pick") || t.channel.Trim().Equals(""))))
                .AsNoTracking().ToListAsync();
            return (Ok(tl.Count));
        }
        ////////////////////////////////////////////////////////
        [NonAction]
        public async Task<string> GetNewTicketCode()
        {
            string code = "";
            for (int i = 0; i < 100; i++)
            {
                code = Util.GetRandomCode(9);
                Ticket ticket = await _context.ticket.FindAsync(code);
                if (ticket == null)
                {
                    break;
                }
            }
            return code;
        }
        [NonAction]
        public async Task<List<Ticket>> GetMemberTickets(int memberId)
        {
            return await _context.ticket.Where(t => t.member_id == memberId && t.valid == 1 && t.is_active == 1)
                .Include(t => t.template).ThenInclude(t => t.productTicketTemplates).ThenInclude(t => t.product)
                .AsNoTracking().ToListAsync();
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<List<Ticket>?>>> GetMemberTicketsByStaff(int memberId, string? bizType,
            bool? canUse, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_context, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Ticket>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            List<Ticket> tickets = await GetMemberTickets(memberId);
            if (bizType != null)
            {
                tickets = tickets.Where(t => (t.biz_type == bizType) || (bizType == "养护" && t.template_id == 12)).ToList();
            }
            if (canUse != null)
            {
                if (canUse == true)
                {
                    tickets = tickets.Where(t => t.start_date == null || ((DateTime)t.start_date).Date <= DateTime.Now.Date)
                        .Where(t => t.expire_date == null || ((DateTime)t.expire_date).Date >= DateTime.Now.Date)
                        .Where(t => t.is_active == 1 && t.used == 0).ToList();
                }
                else
                {
                    tickets = tickets.Where(t => (t.start_date != null && ((DateTime)t.start_date).Date > DateTime.Now.Date) 
                        || (t.expire_date == null && ((DateTime)t.expire_date).Date < DateTime.Now.Date) || t.used == 1 || t.is_active == 0  ).ToList();
                }
            }
            tickets = tickets.OrderBy(t => t.expire_date).ToList();
            return Ok(new ApiResult<List<Ticket>?>()
            {
                code = 0,
                message = "",
                data = tickets
            });
        }
        [HttpGet("{id}")]
        public async Task ActiveSkipassTicketTest(int id)
        {
            Models.SkiPass pass = await _context.skiPass.Where(p => p.id == id).AsNoTracking().FirstOrDefaultAsync();
            await ActiveSkipassTicket(pass);
        }
        [NonAction]
        public async Task<Ticket> ActiveSkipassTicket(Models.SkiPass skiPass)
        {
            Ticket ticket = await _context.ticket.Where(t => t.create_memo == skiPass.id.ToString()).AsNoTracking().FirstOrDefaultAsync();
            if (ticket == null)
            {
                return null;
            }
            if (ticket.is_active == 1)
            {
                //return null;
            }
            DateTime? startDate = skiPass.card_member_pick_time;
            if (startDate == null)
            {
                startDate = DateTime.Now.Date;
            }
            ticket.start_date = startDate;
            ticket.expire_date = ((DateTime)startDate).AddDays(1);
            ticket.is_active = 1;
            _context.ticket.Entry(ticket).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            try
            {
                string first = "您好，您的免费打蜡优惠券已经激活。";
                string shop = "易龙雪聚";
                switch(skiPass.resort)
                {
                    case "南山":
                        shop = "易龙雪聚南山店";
                        break;
                    case "万龙":
                        shop = "易龙雪聚万龙服务中心";
                        break;
                    default:
                        break;
                }
                string remark = "有效期至" + ((DateTime)ticket.expire_date).ToString("yyyy-MM-dd");
                string content = first + "|" + Math.Round((double)skiPass.deal_price, 2).ToString() + "元|微信支付|" + "无|" + shop + "|" + ticket.name.Trim() + "|" + remark;

                string miniAppPath ="/pages/mine/ticket/ticket_detail";
                
                string miniAppQuery = skiPass.id.ToString();
                miniAppQuery = "code=" + ticket.code.Trim();
                string miniAppUrl = "https://mini.snowmeet.top/mapp/open_mapp_page.html?path=" + Util.UrlEncode(miniAppPath) + "&query=" + Util.UrlEncode(miniAppQuery);

                string notUrl = "https://wxoa.snowmeet.top/api/TemlateMessage/SendTemplateMessage?memberId=" + skiPass.member_id.ToString() 
                    + "&templateId=fvfTWtDQZdb-NcRyfsI4iC3kMTGrcMrzKrYXdT0TKmA&first=" + Util.UrlDecode(first) + "&keywords=" + Util.UrlDecode(content)
                    + "&remark=" + Util.UrlDecode(remark) + "&url=" + Util.UrlEncode(miniAppUrl) + "&sessionKey=" + Util.UrlEncode("abcd123!@#");
                Util.GetWebContent(notUrl);
                
            }
            catch
            {
                
            }
            return ticket;
        } 
        [NonAction]
        public async Task<Ticket> CreateTicketBySkiPass(Models.SkiPass skiPass)
        {
            int templateId = 12;
            Ticket ticket = await CreateTicket(templateId, skiPass.member_id, null, skiPass.id.ToString(), "养护", null, false, null, null);
            return ticket;
        }
        [NonAction]
        public async Task<Ticket> CreateTicketByUnipayOrder(Models.Order order)
        {
            string memo = "unipay_" + order.id.ToString();
            Ticket? oriT = await _context.ticket.Where(t => t.create_memo == memo).AsNoTracking().FirstOrDefaultAsync();
            if (oriT != null)
            {
                return null;
            }
            int templateId = 12;
            Ticket ticket = await CreateTicket(templateId, order.member_id, null, "unipay_" + order.id.ToString(), "养护", null, true);
            return ticket;
        }
        [NonAction]
        public async Task<Ticket> CreateTicket(int templateId, int? memberId, int? staffId,
            string? createMemo = null, string? bizType = null, int? bizId = null, 
            bool active = false, DateTime? startDate = null, DateTime? expireDate = null)
        {
            TicketTemplate template = await _context.ticketTemplate
                .Where(t => t.id == templateId).AsNoTracking().FirstOrDefaultAsync();
            if (memberId == null && staffId == null)
            {
                return null;
            }
            string code = await GetNewTicketCode();
            Ticket ticket = new Ticket()
            {
                code = code,
                template_id = templateId,
                name = template.name,
                member_id = memberId,
                create_memo = createMemo,
                memo = template.memo.Trim(),
                create_date = DateTime.Now,
                valid = 1,
                is_active = active?1:0,
                start_date = startDate,
                expire_date = expireDate,
                biz_id = bizId,
                biz_type = bizType
            };
            await _context.ticket.AddAsync(ticket);
            await _context.SaveChangesAsync();
            return await GetWholeTicket(code);
        }
        
        [NonAction]
        public async Task<Ticket> GetWholeTicket(string code)
        {
            Ticket ticket = await _context.ticket.Where(t => t.code == code)
                .Include(t => t.template).ThenInclude(t => t.productTicketTemplates.Where(p => p.valid))
                .AsNoTracking().FirstOrDefaultAsync();
            return ticket;
        }
        
    }
}

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

        // 判断某会员当前是否处于关注状态：取该会员公众号 openid 在 oa_receive 里最新一条
        // subscribe/SCAN/unsubscribe 事件，若最新一条不是 unsubscribe 就认为当前在关注。
        // 用于「已经关注过、这次换了张新券转赠，不应该要求再扫一次码」的场景。
        [NonAction]
        public async Task<bool> IsCurrentlyFollowingOA(Member member)
        {
            if (member == null)
            {
                return false;
            }
            List<MemberSocialAccount> msaOaList = member.GetInfo("wechat_oa_openid");
            if (msaOaList == null || msaOaList.Count == 0)
            {
                return false;
            }
            string? oaOpenId = msaOaList[0].num?.Trim();
            if (string.IsNullOrEmpty(oaOpenId))
            {
                return false;
            }
            OAReceive lastEvent = await _context.oAReceive
                .Where(r => r.MsgType == "event" && r.FromUserName == oaOpenId
                    && (r.Event == "subscribe" || r.Event == "SCAN" || r.Event == "unsubscribe"))
                .OrderByDescending(r => r.id).AsNoTracking().FirstOrDefaultAsync();
            return lastEvent != null && lastEvent.Event != "unsubscribe";
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
            return await IsCurrentlyFollowingOA(accepter);
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
                tickets = tickets.Where(t => t.expire_date == null ||  ((DateTime)t.expire_date).Date <= DateTime.Now.Date).ToList();
            }
            return Ok(new ApiResult<List<Ticket>>()
            {
                code = 0,
                message = "",
                data = tickets
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

            Ticket ticket = await _context.ticket.FindAsync(code);
            if (ticket == null || ticket.shared != 1)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "该优惠券当前不可接受，链接可能已失效", data = null });
            }
            if (ticket.member_id == accepter.id)
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "不能转赠给自己", data = null });
            }
            if (!await HasFollowedForTransfer(ticket, accepter))
            {
                return Ok(new ApiResult<Ticket>() { code = 1, message = "请先关注公众号后再接受这张优惠券", data = null });
            }

            Member sender = ticket.member_id == null ? null : await _context.member.FindAsync(ticket.member_id);

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

            ticket.open_id = "";

            return Ok(new ApiResult<Ticket>() { code = 0, message = "", data = ticket });
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
        public async Task<Ticket> GenerateTicketByAction(int templateId, int memberId, int isActive = 1, int orderId = 0, string createMemo = "", string channel = "")
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
                expire_date = template.expire_date == null ? DateTime.MaxValue : (DateTime)template.expire_date,
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

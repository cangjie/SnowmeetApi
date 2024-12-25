using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Ticket;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Card;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class TicketController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;

        public string _appId = "";

        public TicketController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TicketTemplate>>> GetTemplateList()
        {
            var list = await _context.TicketTemplate.Where<TicketTemplate>(tt => tt.hide == 0).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{templateId}")]
        public async Task<ActionResult<TicketTemplate>> GetTicketTemplateById(int templateId)
        {
            return Ok(await _context.TicketTemplate.FindAsync(templateId));
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetUnusedTicketsByCode(string ticketCodeArr)
        {
            ticketCodeArr = Util.UrlDecode(ticketCodeArr);
            var ticketArr = await _context.Ticket
                .FromSqlRaw(" select * from ticket where used = 0 and code in ("
                + ticketCodeArr.Replace("'", "").Trim() + ") ")
                .ToListAsync();
            return ticketArr;
        }

        [HttpGet]
        [ActionName("GetChannels")]
        public async Task<ActionResult<IEnumerable<string>>> GetChannels()
        {
            return await _context.Ticket
                .Where(tt=>!tt.channel.Trim().Equals(""))
                .Select(tt => tt.channel).Distinct().ToListAsync();
            
            
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<Ticket>> SetTicketToShare(string code, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);

            Ticket ticket = await _context.Ticket.FindAsync(code);
            if (ticket == null || !ticket.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                return NotFound();
            }
            ticket.shared = 1;
            ticket.shared_time = DateTime.Now;

            _context.Entry(ticket).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            ticket.open_id = "";

            return Ok(ticket);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<Ticket>> AcceptTicket(string code, string memo, string sessionKey)
        {
            memo = Util.UrlDecode(memo).Trim();
            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);

            Ticket ticket = await _context.Ticket.FindAsync(code);

            if (ticket.shared != 1)
            {
                return NotFound();
            }

            TicketLog log = new TicketLog()
            {
                code = ticket.code,
                sender_open_id = ticket.open_id.Trim(),
                accepter_open_id = user.miniAppOpenId.Trim(),
                memo = memo,
                transact_time = DateTime.Now
            };
            await _context.AddAsync(log);
            await _context.SaveChangesAsync();

            ticket.open_id = user.miniAppOpenId.Trim();

            _context.Entry(ticket).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            ticket.open_id = "";

            return Ok(ticket);



        }

        // GET: api/Ticket/5
        [HttpGet("{code}")]
        public async Task<ActionResult<Ticket>> GetTicket(string code)
        {
            var ticket = await _context.Ticket.FindAsync(code);

            if (ticket == null)
            {
                return NotFound();
            }

            ticket.open_id = "";

            return ticket;
        }


        [HttpGet("{templateId}")]
        public async Task<ActionResult<Ticket>> GenerateTicketsByUser(int templateId, string sessionKey, string source = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            string channel = Util.UrlDecode(source);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            /*
            var tList = await _context.Ticket.Where(t => (t.template_id == templateId
                && t.open_id.Trim().Equals(user.miniAppOpenId)
                && t.used == 0 )).ToListAsync();
            if (tList.Count > 0)
            {
                return BadRequest();
            }
            */
            int retryTimes = 0;
            bool isDuplicate = true;
            string code = Util.GetRandomCode(9);
            for (; isDuplicate && retryTimes < 1000;)
            {
                isDuplicate = _context.Card.Any(e => e.card_no == code);
            }

            if (isDuplicate)
            {
                return NoContent();
            }

            TicketTemplate template = await _context.TicketTemplate.FindAsync(templateId);

            Card card = new Card
            {
                card_no = code,
                is_ticket = 1,
                type = ""
            };
            await _context.Card.AddAsync(card);
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
                expire_date = ((template.expire_date == null)? DateTime.MaxValue : (DateTime)template.expire_date)

            };
            await _context.Ticket.AddAsync(ticket);
            await _context.SaveChangesAsync();
            return Ok(ticket);
        }

        [HttpGet("{templateId}")]
        public async Task<ActionResult<Ticket[]>> GenerateTickets(int templateId, int count, string sessionKey, string channel = "")
        {
            TicketTemplate template = _context.TicketTemplate.Find(templateId);
            if (template == null)
            {
                return NoContent();
            }

            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
            {
                return NoContent();
            }
            Ticket[] tickets = new Ticket[count];
            for (int i = 0; i < count; i++)
            {
                int retryTimes = 0;
                bool isDuplicate = true;
                string code = Util.GetRandomCode(9);
                for (; isDuplicate && retryTimes < 1000;)
                {
                    isDuplicate = _context.Card.Any(e => e.card_no == code);
                }
                if (isDuplicate)
                {
                    continue;
                }
                Card card = new Card {
                    card_no = code,
                    is_ticket = 1,
                    type = ""
                };
                _context.Card.Add(card);
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
                    open_id = "",
                    create_date = DateTime.Now,
                    channel = channel.Trim()

                };
                _context.Ticket.Add(ticket);
                bool insertTicketSuccess = true;
                try
                {
                    await _context.SaveChangesAsync();
                    tickets[i] = ticket;
                }
                catch(DbUpdateException exp1)
                {
                    insertTicketSuccess = false;
                    _context.Ticket.Remove(ticket);
                    
                    
                }
                if (!insertTicketSuccess)
                {
                    card = await _context.Card.FindAsync(code);
                    _context.Card.Remove(card);
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
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user.isAdmin)
            {
                Ticket ticket = await _context.Ticket.FindAsync(code);
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

        [HttpGet("{used}")]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetMyTickets(int used, string sessionKey)
        {
            
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || user.miniAppOpenId == null || user.miniAppOpenId.Trim().Equals(""))
            {
                return NotFound();
            }

            return await _context.Ticket.Where<Ticket>(t => (t.open_id == user.miniAppOpenId && t.used == used)).OrderBy(t=>t.create_date).ToListAsync();
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<bool>> Bind(string code, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null)
            {
                return NotFound();
            }
            Ticket ticket = await _context.Ticket.FindAsync(code.Trim());
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
            var ticketList = await _context.Ticket.Where(t => (t.open_id.Trim().Equals(openId.Trim()) && t.used == used)).ToListAsync();
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

            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            

            Ticket ticket = await _context.Ticket.FindAsync(code);
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
        public async Task<string> GetNewTicketCode()
        {
            string code = "";
            for(int i = 0; i < 100; i++)
            {
                code = Util.GetRandomCode(9);
                Ticket ticket = await _context.Ticket.FindAsync(code);
                if (ticket == null)
                {
                    break;
                }
            }
            return code;
        }

        [HttpGet]
        public async Task<Ticket> GenerateTicketByAction(int templateId, int memberId, int orderId = 0, string createMemo = "")
        {
            TicketTemplate template = await _context.TicketTemplate.FindAsync(templateId);
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
                expire_date = template.expire_date == null? DateTime.MaxValue : (DateTime)template.expire_date,
                oper_open_id = "",
                printed = 0,
                miniapp_recept_path = "",
                create_date = DateTime.Now,
                channel = "",
                order_id = orderId == 0 ? null : orderId,
                create_memo = createMemo
            };
            await _context.Ticket.AddAsync(ticket);
            await _context.SaveChangesAsync();
            return ticket;
        }

        [NonAction]
        public async Task Cancel(int orderId)
        {
            List<Ticket> ticketArr = await _context.Ticket
                .Where(t => t.used == 0 && t.order_id == orderId).ToListAsync();
            for(int i = 0; i < ticketArr.Count; i++)
            {
                Ticket ticket = ticketArr[i];
                ticket.used = 0;
                ticket.used_time = DateTime.Now;
                ticket.use_memo = "订单取消";
                _context.Ticket.Entry(ticket).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }


       
        private bool TicketExists(string id)
        {
            return _context.Ticket.Any(e => e.code == id);
        }
    }
}

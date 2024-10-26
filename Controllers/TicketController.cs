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

            return await _context.TicketTemplate.Where<TicketTemplate>(tt => tt.hide == 0).ToListAsync();
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
        public async Task<ActionResult<Ticket>> GenerateTicketsByUser(int templateId, string sessionKey, string channel = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            channel = Util.UrlDecode(channel);
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
                channel = channel.Trim()

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
            if (ticket == null)
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


        /*

        // GET: api/Ticket
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Ticket>>> GetTicket()
        {
            return await _context.Ticket.ToListAsync();
        }

        

        // PUT: api/Ticket/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTicket(string id, Ticket ticket)
        {
            if (id != ticket.code)
            {
                return BadRequest();
            }

            _context.Entry(ticket).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TicketExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Ticket
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Ticket>> PostTicket(Ticket ticket)
        {
            _context.Ticket.Add(ticket);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (TicketExists(ticket.code))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetTicket", new { id = ticket.code }, ticket);
        }

        // DELETE: api/Ticket/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTicket(string id)
        {
            var ticket = await _context.Ticket.FindAsync(id);
            if (ticket == null)
            {
                return NotFound();
            }

            _context.Ticket.Remove(ticket);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool TicketExists(string id)
        {
            return _context.Ticket.Any(e => e.code == id);
        }
    }
}

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
    [Route("[controller]/[action]")]
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

        // GET: api/Ticket/5
        [HttpGet("{code}")]
        public async Task<ActionResult<Ticket>> GetTicket(string code)
        {
            var ticket = await _context.Ticket.FindAsync(code);

            if (ticket == null)
            {
                return NotFound();
            }

            return ticket;
        }

        [HttpGet("{templateId}")]
        public async Task<ActionResult<IEnumerable<Ticket>>> GenerateTickets(int templateId, int count, string sessionKey)
        {
            TicketTemplate template = _context.TicketTemplate.Find(templateId);
            if (template == null)
            {
                return NoContent();
            }

            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (user == null || !user.isAdmin)
            {
                //return NoContent();
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
                    is_ticket = 1
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
                    miniapp_recept_path = template.miniapp_recept_path,
                    open_id = ""
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

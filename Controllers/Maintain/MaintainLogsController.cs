using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Ticket;

namespace SnowmeetApi.Controllers.Maintain
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MaintainLogsController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration _originConfig;

        public MaintainLogsController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _originConfig = config;
            //UnicUser._context = context;
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<MaintainLog>> StartStep(int taskId, string stepName, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            stepName = Util.UrlDecode(stepName);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            MaintainLog log = new MaintainLog()
            {
                id = 0,
                task_id = taskId,
                step_name = stepName.Trim(),
                start_time = DateTime.Now,
                staff_open_id = user.miniAppOpenId.Trim(),
                status = "已开始",
                stop_open_id = "",
                memo = "",
                staffName = user.miniAppUser.real_name.Trim()
            };
            await _context.MaintainLog.AddAsync(log);
            await _context.SaveChangesAsync();

            if (stepName.Trim().Equals("发板") || stepName.Trim().Equals("强行索回"))
            {
                MaintainLive task = await _context.MaintainLives.FindAsync(taskId);
                task.finish = 1;
                _context.Entry(task).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            return log;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintainReport>>> GetReport(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var list = await _context.maintainReport.FromSqlRaw(" select * from dbo.func_maintain_report('"
                + startDate.ToShortDateString() + "', '" + endDate.AddDays(1).ToShortDateString() + "')  "
                + "  order by create_date desc , order_id desc ")
                .AsNoTracking().ToListAsync();
            return Ok(list);
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<IEnumerable<MaintainLog>>> GetSteps(int taskId)
        {
            return await _context.MaintainLog.Where(m => m.task_id == taskId).OrderBy(m => m.id).ToListAsync();
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<IEnumerable<MaintainLog>>> GetStepsByStaff(int taskId, string sessionKey)
        {
            MiniAppUserController mUserController = new MiniAppUserController(_context, _originConfig);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var logList = await  _context.MaintainLog.Where(m => m.task_id == taskId).OrderBy(m => m.id).ToListAsync();
            for (int i = 0; i < logList.Count; i++)
            {
                if (!user.miniAppOpenId.Trim().Equals(logList[i].staff_open_id.Trim()))
                {
                    logList[i].isMine = false;
                }
                MiniAppUser staffUser = (MiniAppUser)((OkObjectResult)(await mUserController.GetMiniAppUser(logList[i].staff_open_id, sessionKey)).Result).Value;
                logList[i].staffName = staffUser.real_name.Trim();
            }
            return Ok(logList); 
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<MaintainLog>> EndStep(int id, string memo, string sessionKey)
        {
            memo = Util.UrlDecode(memo);
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            MaintainLog log = await _context.MaintainLog.FindAsync(id);
            log.memo = memo;
            if (log.staff_open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                log.end_time = DateTime.Now;
                log.status = "已完成";
            }
            else
            {
                log.end_time = DateTime.Now;
                log.status = "强行终止";
                log.stop_open_id = user.miniAppOpenId.Trim();
            }
            _context.Entry(log).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            log.staffName = user.miniAppUser.real_name.Trim();

            try
            {

                int taskId = log.task_id;

                if (taskId > 0)
                {
                    var ticketLogList = await _context.ticketLog.Where(log => log.memo.IndexOf("养护订单获得，ID:" + taskId.ToString()) >= 0).ToListAsync();
                    if (ticketLogList == null || ticketLogList.Count == 0)
                    {
                        

                        MaintainLive task = await _context.MaintainLives.FindAsync(taskId);
                        if (task != null)
                        {
                            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _originConfig);
                            OrderOnline order = (await orderHelper.GetWholeOrderByStaff(task.order_id, sessionKey)).Value;
                            if (order != null)
                            {
                                TicketController ticketHelper = new TicketController(_context, _originConfig);
                                Ticket ticket = (await ticketHelper.GenerateTickets(8, 1, sessionKey, "养护订单")).Value[0];
                                if (ticket == null)
                                {
                                    return BadRequest();
                                }
                                ticket.open_id = order.open_id.Trim();
                                _context.Entry(ticket).State = EntityState.Modified;
                                await _context.SaveChangesAsync();
                                TicketLog tLog = new TicketLog()
                                {
                                    code = ticket.code,
                                    sender_open_id = user.miniAppOpenId,
                                    accepter_open_id = order.open_id.Trim(),
                                    memo = "养护订单获得，ID:" + task.id.ToString(),
                                    transact_time = DateTime.Now
                                };
                                await _context.AddAsync(tLog);
                                await _context.SaveChangesAsync();

                                double paidAmount = order.paidAmount;
                                double orderPrice = order.order_price;

                                ServiceMessageController messageHelper = new ServiceMessageController(_context, _originConfig);

                                await messageHelper.SendTemplateMessage(order.open_id, "zk6Bde8PolaoPQVLytFZRhKIYux3uHABpzK9Oqy_lfk",
                                    "感谢您在易龙雪聚养护装备，特赠送一张养护券。", "" + Util.GetMoneyStr(orderPrice) + "|" + Util.GetMoneyStr(paidAmount)
                                    + "|" + Util.GetMoneyStr(orderPrice - paidAmount) + "|" + order.pay_method.Trim() + "|养护券",
                                    "点击下面👇公众号菜单查看", "", sessionKey);
                            }
                            



                        }
                    }
                }
            }
            catch
            {

            }

            return log;
        }

        /*

        // GET: api/MaintainLogs
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintainLog>>> GetMaintainLog()
        {
            return await _context.MaintainLog.ToListAsync();
        }

        // GET: api/MaintainLogs/5
        [HttpGet("{id}")]
        public async Task<ActionResult<MaintainLog>> GetMaintainLog(int id)
        {
            var maintainLog = await _context.MaintainLog.FindAsync(id);

            if (maintainLog == null)
            {
                return NotFound();
            }

            return maintainLog;
        }

        // PUT: api/MaintainLogs/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaintainLog(int id, MaintainLog maintainLog)
        {
            if (id != maintainLog.id)
            {
                return BadRequest();
            }

            _context.Entry(maintainLog).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MaintainLogExists(id))
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

        // POST: api/MaintainLogs
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<MaintainLog>> PostMaintainLog(MaintainLog maintainLog)
        {
            _context.MaintainLog.Add(maintainLog);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetMaintainLog", new { id = maintainLog.id }, maintainLog);
        }

        // DELETE: api/MaintainLogs/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaintainLog(int id)
        {
            var maintainLog = await _context.MaintainLog.FindAsync(id);
            if (maintainLog == null)
            {
                return NotFound();
            }

            _context.MaintainLog.Remove(maintainLog);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool MaintainLogExists(int id)
        {
            return _context.MaintainLog.Any(e => e.id == id);
        }
    }
}

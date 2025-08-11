using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class PrinterController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        public PrinterController(ApplicationDBContext context)
        {
            _db = context;
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Printer>>> GetPrinters(string shop, string color, string sessionKey)
        {
            var l = await _db.printer //.Where(p => (p.id <= 3 || p.id >= 8))
                .AsNoTracking().ToListAsync();
            return Ok(l);
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<PrintTask>>>> RefreshPrintTask(string shop, DateTime startDate)
        {
            return Ok(new ApiResult<List<PrintTask>>()
            {
                code = 0,
                message = "",
                data = await GetPrintTask(shop, startDate)
            });
        }
        [NonAction]
        public async Task<List<PrintTask>> GetPrintTask(string shop, DateTime startDate)
        {
            return await _db.printTask
                .Where(p => p.shop.Trim().Equals(shop.Trim()) && p.create_date.Date == startDate.Date)
                .OrderByDescending(p => p.id).AsNoTracking().ToListAsync();
        }
        [NonAction]
        public async Task<List<PrintTask>> QueryPrintTask(string shop, DateTime startDate)
        {
            for (; true;)
            {
                try
                {
                    List<PrintTask> tasks = await GetPrintTask(shop, startDate);
                    if (tasks.Where(t => t.fetched == 0).ToList().Count > 0)
                    {
                        return tasks;
                    }
                }
                catch
                { 
                    
                }
                Thread.Sleep(1000);
            }
        }
        [HttpGet("{taskId}")]
        public async Task<ActionResult<ApiResult<PrintTask>>> ReplyFetched(int taskId)
        {
            PrintTask pt = await _db.printTask.FindAsync(taskId);
            pt.fetched = 1;
            pt.update_date = DateTime.Now;
            _db.printTask.Entry(pt);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<PrintTask>()
            {
                code = 0,
                message = "",
                data = pt
            });
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using wechat_miniapp_base.Models;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
    [ApiController]
    public class WepayOrderController : ControllerBase
    {

        public class Resource
        {
            public string original_type { get; set; }
            public string algorithm { get; set; }
            public string ciphertext { get; set; }
            public string associated_data { get; set; }
            public string nonce { get; set; }
        }

        public class CallBackStruct
        {
            public string id { get; set; }
            public DateTimeOffset create_time { get; set; }
            public string resource_type { get; set; }
            public string event_type { get; set; }
            public string summary { get; set; }
            public Resource resource { get; set; }
        }

        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public WepayOrderController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        [HttpPost]
        public async Task<ActionResult<string>> CallBack(CallBackStruct postData)
        {
            string postJson = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            string path = $"{Environment.CurrentDirectory}";
            if (path.StartsWith("/"))
            {
                path = path + "/";
            }
            else
            {
                path = path + "\\";
            }
            path = path + "wepay_callback.txt";
            FileStream fs = System.IO.File.Create(path);
            // 此文本只添加到文件一次。
            using (StreamWriter fw = new StreamWriter(fs))
            {
                fw.WriteLine(postJson);
            }
            fs.Dispose();
            return "{ \r\n \"code\": \"SUCCESS\", \r\n \"message\": \"成功\" \r\n}";
        }


        /*
        // GET: api/WepayOrder
        [HttpGet]
        public async Task<ActionResult<IEnumerable<WepayOrder>>> GetWepayOrders()
        {
            return await _context.WepayOrders.ToListAsync();
        }

        // GET: api/WepayOrder/5
        [HttpGet("{id}")]
        public async Task<ActionResult<WepayOrder>> GetWepayOrder(string id)
        {
            var wepayOrder = await _context.WepayOrders.FindAsync(id);

            if (wepayOrder == null)
            {
                return NotFound();
            }

            return wepayOrder;
        }

        // PUT: api/WepayOrder/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutWepayOrder(string id, WepayOrder wepayOrder)
        {
            if (id != wepayOrder.out_trade_no)
            {
                return BadRequest();
            }

            _context.Entry(wepayOrder).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!WepayOrderExists(id))
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

        // POST: api/WepayOrder
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<WepayOrder>> PostWepayOrder(WepayOrder wepayOrder)
        {
            _context.WepayOrders.Add(wepayOrder);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (WepayOrderExists(wepayOrder.out_trade_no))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetWepayOrder", new { id = wepayOrder.out_trade_no }, wepayOrder);
        }

        // DELETE: api/WepayOrder/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWepayOrder(string id)
        {
            var wepayOrder = await _context.WepayOrders.FindAsync(id);
            if (wepayOrder == null)
            {
                return NotFound();
            }

            _context.WepayOrders.Remove(wepayOrder);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool WepayOrderExists(string id)
        {
            return _context.WepayOrders.Any(e => e.out_trade_no == id);
        }
        */
    }
}

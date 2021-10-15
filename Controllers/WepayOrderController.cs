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
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;

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

        private readonly IHttpContextAccessor _httpContextAccessor;

        public WepayOrderController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }
        /*
        [HttpGet]
        public void DecodeSign()
        {
            WepayKey key =  _context.WepayKeys.Find(1);
            var certManager = new InMemoryCertificateManager();
            var options = new WechatTenpayClientOptions()
            {
                MerchantId = key.mch_id.Trim(),
                MerchantV3Secret = "",
                MerchantCertSerialNumber = key.key_serial.Trim(),
                MerchantCertPrivateKey = key.private_key.Trim(),
                CertificateManager = certManager
            };

            string callbackJson = "{\"id\":\"f0b8f844-7bdc-5e53-9b8b-58150caa963c\",\"create_time\":\"2021-10-15T10:07:31+08:00\",\"resource_type\":\"encrypt-resource\",\"event_type\":\"TRANSACTION.SUCCESS\",\"summary\":\"支付成功\",\"resource\":{\"original_type\":\"transaction\",\"algorithm\":\"AEAD_AES_256_GCM\",\"ciphertext\":\"SP6+NyGoBJEATbTuzD+4DBUeK/3uwfEMW7fYVsw6rv3WfeLs9WeqtgbS7cVxH8BhKeclrGCaZCFY6gqHvKx3TV3hHUnbSUkz7WuMSEXOnNz/1YBBopGIHKJTSEohuVf+NqRZ79/U2JZ52iMsEXoYYTwhmOZ5ApbpzK4uzivNTCChti/t083TpwHhdWu1AKUOJBLQVy/S8ZBu0/98Y03JSZO9SzaVD3GAHTxTVQ5sLvfVAcbADCp0oa4ymlk/DXUiHhHMUhlHWY7fdKSahz1f5JjI4nTf/7H1L7+91UME09YJWB4/c0aH/SdrZ/nevCQCWSPSVCzwKitvPpX2j94tEDHdGMZb3snB/857kusKHjshv86X1aRj0l4Kxn/5wHZX9lPa1PhmMjjBfy2l/eXMPxDgYu1Uw9pPCHHxfaPDInErcGdX8WQy4AntNivSJVfLULRS+gyWpP/IbfT8VHLmzOCSV788lmZypZtZN6t86yZoqFlB7fC+pUSTv4ecMQhR75+HP9UzD7OrdqWj9gREg8Dk79iy7lpXUIM9SCRRKX0wtxm1GpOYzKZqMinxjzLK\",\"associated_data\":\"transaction\",\"nonce\":\"g553iJEtj6hg\"}}";
            string callbackTimestamp = "微信回调通知中的 Wechatpay-Timestamp 标头";
            string callbackNonce = "微信回调通知中的 Wechatpay-Nonce 标头";
            string callbackSignature = "微信回调通知中的 Wechatpay-Signature 标头";
            string callbackSerialNumber = "微信回调通知中的 Wechatpay-Serial 标头";
        }
        */
        [HttpPost]
        public async Task<ActionResult<string>> CallBack(CallBackStruct postData)
        {
            string postJson = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            string path = $"{Environment.CurrentDirectory}";
            string paySign = "no sign";
            string nonce = "no nonce";
            string serial = "no serial";
            string timeStamp = "no time";
            try
            {
                paySign = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Signature"].ToString();
                nonce = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Nonce"].ToString();
                serial = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Serial"].ToString();
                timeStamp = _httpContextAccessor.HttpContext.Request.Headers["Wechatpay-Timestamp"].ToString();
            }
            catch
            { 
            
            }
            if (path.StartsWith("/"))
            {
                path = path + "/";
            }
            else
            {
                path = path + "\\";
            }
            path = path + "wepay_callback.txt";
            
            // 此文本只添加到文件一次。
            using (StreamWriter fw = new StreamWriter(path, true))
            {
                fw.WriteLine(DateTimeOffset.Now.ToString());
                fw.WriteLine(serial);
                fw.WriteLine(timeStamp);
                fw.WriteLine(nonce);
                fw.WriteLine(paySign);
                fw.WriteLine(postJson);
                fw.WriteLine("");
                fw.WriteLine("--------------------------------------------------------");
                fw.WriteLine("");
                fw.Close();
            }
            



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

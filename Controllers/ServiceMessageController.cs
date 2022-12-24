using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.CreateApplyForSubMerchantApplymentRequest.Types.Business.Types.SaleScene.Types;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ServiceMessageController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private readonly string _originId = "gh_2ec89153fa16";

        private readonly IConfiguration _config;

        private readonly string _appId;

        public ServiceMessageController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
            _appId = _config.GetSection("Settings").GetSection("AppId").Value.Trim();
        }

        [NonAction]
        public string GetToken()
        {
            return Util.GetWebContent("http://weixin.snowmeet.top/get_token.aspx").Trim();
        }

        [NonAction]
        public async Task<string> GetOAOpenId(string miniAppOpenId)
        {
            MiniAppUser miniAppUser = await _context.MiniAppUsers.FindAsync(miniAppOpenId);
            List<UnionId> uidList = await _context.UnionIds.Where(u => u.union_id.Trim().Equals(miniAppUser.union_id.Trim())
                && u.source.Trim().Equals("snowmeet_official_account_new")).ToListAsync();
            if (uidList == null || uidList.Count < 1)
            {
                return "";
            }
            return uidList[0].open_id.Trim();
        }


        [HttpGet]
        public async Task<ActionResult<ServiceMessage>> SendMiniAppMessage(string miniAppOpenId, string title, string path, string mediaId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            path = Util.UrlDecode(path).Trim();
            mediaId = Util.UrlDecode(mediaId).Trim();
            miniAppOpenId = Util.UrlDecode(miniAppOpenId).Trim();
            title = Util.UrlDecode(title).Trim();
            string token = GetToken();
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (user == null || !user.isAdmin)
            {
                return BadRequest();
            }
            string openId = await GetOAOpenId(miniAppOpenId.Trim());
            string postJson = "{\"touser\":\"" + openId + "\",  \"msgtype\":\"miniprogrampage\",    \"miniprogrampage\": {"
                + "\"title\":\"" + title + "\",    \"appid\":\"" + _appId + "\", "
                + "\"pagepath\":\"" + path + "\",        \"thumb_media_id\":\"" + mediaId + "\"   }}";
            string postUrl = "https://api.weixin.qq.com/cgi-bin/message/custom/send?access_token=" + token.Trim();
            string ret = Util.GetWebContent(postUrl, postJson);

            ServiceMessage msg = new ServiceMessage()
            {
                type = "mapp",
                from = _originId,
                to = openId,
                content = postJson,
                return_code = ret
            };
            await _context.AddAsync(msg);
            await _context.SaveChangesAsync();


            return msg;


            /*
            string appId = _settings.miniAppId.Trim();
            //Util.GetWebContent()
            string postJson = "{\"touser\":\"" + openId + "\",  \"msgtype\":\"miniprogrampage\",    \"miniprogrampage\": {"
                + "\"title\":\"" + title + "\",    \"appid\":\"" + appId + "\", "
                + "\"pagepath\":\"" + path + "\",        \"thumb_media_id\":\"" + mediaId + "\"   }}";
            OfficialAccountApi oaHelper = new OfficialAccountApi(_db, _config);
            string token = oaHelper.GetAccessToken();
            string postUrl = "https://api.weixin.qq.com/cgi-bin/message/custom/send?access_token=" + token.Trim();
            Console.WriteLine(Util.GetWebContent(postUrl, postJson));
            */
        }


        /*


        // GET: api/ServiceMessage
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ServiceMessage>>> GetServiceMessage()
        {
            return await _context.ServiceMessage.ToListAsync();
        }

        // GET: api/ServiceMessage/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ServiceMessage>> GetServiceMessage(int id)
        {
            var serviceMessage = await _context.ServiceMessage.FindAsync(id);

            if (serviceMessage == null)
            {
                return NotFound();
            }

            return serviceMessage;
        }

        // PUT: api/ServiceMessage/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutServiceMessage(int id, ServiceMessage serviceMessage)
        {
            if (id != serviceMessage.id)
            {
                return BadRequest();
            }

            _context.Entry(serviceMessage).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ServiceMessageExists(id))
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

        // POST: api/ServiceMessage
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<ServiceMessage>> PostServiceMessage(ServiceMessage serviceMessage)
        {
            _context.ServiceMessage.Add(serviceMessage);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetServiceMessage", new { id = serviceMessage.id }, serviceMessage);
        }

        // DELETE: api/ServiceMessage/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteServiceMessage(int id)
        {
            var serviceMessage = await _context.ServiceMessage.FindAsync(id);
            if (serviceMessage == null)
            {
                return NotFound();
            }

            _context.ServiceMessage.Remove(serviceMessage);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */


        private bool ServiceMessageExists(int id)
        {
            return _context.ServiceMessage.Any(e => e.id == id);
        }
    }
}

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

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MiniAppUserController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        public string _appId = "";

        public MiniAppUserController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            UnicUser._context = context;
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> SetStaff(string openId, bool isStaff, string sessionKey)
        {
            openId = Util.UrlDecode(openId);
            sessionKey = Util.UrlDecode(sessionKey);
            MiniAppUser managerUser = (MiniAppUser)((OkObjectResult)(await GetMiniUser(sessionKey)).Result).Value;
            if (managerUser.is_manager != 1)
            {
                return NotFound();
            }
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(openId);
            if (isStaff)
            {
                user.is_admin = 1;
            }
            else
            {
                user.is_admin = 0;
            }
            _context.MiniAppUsers.Entry(user).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(user);

        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MiniAppUser>>> GetStaffList(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            return await _context.MiniAppUsers.Where(u => u.is_admin == 1).ToListAsync();
        }
        
        [NonAction]
        public async Task<ActionResult<string>> GetOpenIdByCell(string cell)
        {
            string openId = "";
            var uArr = await _context.MiniAppUsers.Where(u => u.cell_number.Trim().Equals(cell.Trim()))
                .OrderByDescending(u => u.create_date).ToListAsync();
            if (uArr != null && uArr.Count > 0)
            {
                openId = uArr[0].open_id.Trim();
            }
            return openId;
        }
        
        [HttpGet("{cell}")]
        public async Task<ActionResult<MiniAppUser>> GetUserByCell(string cell, string staffSessionKey)
        {
            if (!Util.IsAdmin(staffSessionKey, _context))
            {
                return NoContent();
            }
            var miniUserList = await _context.MiniAppUsers
                .Where(u => u.cell_number.Trim().Equals(cell.Trim()))
                .OrderByDescending(u => u.create_date).ToListAsync();
            if (miniUserList.Count == 0)
            {
                return NotFound();
            }
            else
            {
                return miniUserList[0];
            }
        }


        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> GetMiniAppUser(string openId, string sessionKey)
        {
            openId = Util.UrlDecode(openId.Trim());
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return NoContent();
            }
            MiniAppUser miniUser = await _context.MiniAppUsers.FindAsync(openId);

            var orderL = await _context.OrderOnlines.Where(o => o.open_id.Trim().Equals(miniUser.open_id.Trim())
                && o.pay_state == 1).AsNoTracking().Take(1).ToListAsync();
            if (orderL.Count > 0)
            {
                miniUser.isMember = true;
            }

            return await _context.MiniAppUsers.FindAsync(openId);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<MiniAppUser>> GetMiniUserByTicket(string code, string sessionKey)
        {
            var ticket = await _context.Ticket.FindAsync(code.Trim());
            if (ticket != null)
            {
                string openId = ticket.open_id.Trim();
                if (!openId.Trim().Equals(""))
                    return await GetMiniAppUser(openId, sessionKey);
                else
                    return NotFound();
            }
            return NotFound();
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUserList>> GetMiniUserOld(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());

            var mSessionList = await _context.MiniSessons.Where(m => (m.session_key.Trim().Equals(sessionKey.Trim()))).ToListAsync();
            if (mSessionList.Count == 0)
            {
                return NotFound();
            }
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(mSessionList[0].open_id);
            user.open_id = "";
            if (user != null)
            {
                MiniAppUserList l = new MiniAppUserList()
                {
                    mini_users = new MiniAppUser[] { user }
                };
                return l;
            }
            else
            {
                return NotFound();
            }
            
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUserList>> GetMiniUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());

            var mSessionList = await _context.MiniSessons.Where(m => (m.session_key.Trim().Equals(sessionKey.Trim()))).ToListAsync();
            if (mSessionList.Count == 0)
            {
                return NotFound();
            }
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(mSessionList[0].open_id);
            return Ok(user);

        }

        [HttpPost]
        public async Task<ActionResult<MiniAppUser>> UpdateMiniUser([FromQuery] string sessionKey, [FromBody] MiniAppUser miniUser)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin && !miniUser.open_id.Trim().Equals(""))
            {
                return BadRequest();
            }
            string openId = miniUser.open_id.Trim();
            if (openId.Equals(""))
            {
                openId = user.miniAppOpenId.Trim();
            }
            MiniAppUser trackUser = await _context.MiniAppUsers.FindAsync(openId);
            trackUser.nick = miniUser.nick.Trim();
            trackUser.real_name = miniUser.real_name.Trim();
            trackUser.gender = miniUser.gender.Trim();
            trackUser.cell_number = miniUser.cell_number.Trim();
            trackUser.wechat_id = miniUser.wechat_id.Trim();
            _context.Entry(trackUser).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(trackUser);
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> UpdateUserInfo(string sessionKey, string encData, string iv)
        {
            try
            {
                sessionKey = Util.UrlDecode(sessionKey);
                encData = Util.UrlDecode(encData);
                iv = Util.UrlDecode(iv);
                //
                UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
                MiniAppUser miniUser = user.miniAppUser;
                string json = Util.AES_decrypt(encData.Trim(), sessionKey, iv);
                Newtonsoft.Json.Linq.JToken jsonObj = (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                if (jsonObj["phoneNumber"] != null)
                {
                    miniUser.cell_number = jsonObj["phoneNumber"].ToString().Trim();
                }
                string nick = "";
                if (jsonObj["nickName"] != null && !jsonObj["nickName"].ToString().Trim().Equals("微信用户"))
                {
                    nick = jsonObj["nickName"].ToString().Trim();
                }
                else
                {
                    nick = miniUser.real_name.Trim();
                }
                miniUser.nick = nick;
                string gender = "";
                if (jsonObj["gender"] != null)
                {
                    if (jsonObj["gender"].ToString().Equals("0"))
                    {
                        gender = "男";
                    }
                    else
                    {
                        gender = "女";
                    }
                }
                miniUser.gender = gender.Trim();
                if (jsonObj["unionId"] != null && jsonObj["unionId"].ToString().Trim().Equals(""))
                {
                    miniUser.union_id = jsonObj["unionId"].ToString().Trim();
                }
                if (jsonObj["avatarUrl"] != null && !jsonObj["avatarUrl"].ToString().Trim().Equals(""))
                {
                    miniUser.head_image = jsonObj["avatarUrl"].ToString().Trim();
                }
                _context.Entry(miniUser).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return user.miniAppUser;
            }
            catch (Exception err)
            {

                return BadRequest();
            }
        }

       
        private bool MiniAppUserExists(string id)
        {
            return _context.MiniAppUsers.Any(e => e.open_id == id);
        }
    }
}

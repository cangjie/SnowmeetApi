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
        public async Task<ActionResult<IEnumerable<MiniAppUser>>> GetStaffList(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
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
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
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
        public async Task<ActionResult<MiniAppUser>> UpdateUserInfo(string sessionKey, string encData, string iv)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //UnicUser._context = _context;
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
            if (jsonObj["gender"].ToString().Equals("0"))
            {
                gender = "男";
            }
            else
            {
                gender = "女";
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

        /*

        // GET: api/MiniAppUser
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MiniAppUser>>> GetMiniAppUsers()
        {
            return await _context.MiniAppUsers.ToListAsync();
        }

        // GET: api/MiniAppUser/5
        [HttpGet("{id}")]
        public async Task<ActionResult<MiniAppUser>> GetMiniAppUser(string id)
        {
            var miniAppUser = await _context.MiniAppUsers.FindAsync(id);

            if (miniAppUser == null)
            {
                return NotFound();
            }

            return miniAppUser;
        }

        // PUT: api/MiniAppUser/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMiniAppUser(string id, MiniAppUser miniAppUser)
        {
            if (id != miniAppUser.open_id)
            {
                return BadRequest();
            }

            _context.Entry(miniAppUser).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MiniAppUserExists(id))
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

        // POST: api/MiniAppUser
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<MiniAppUser>> PostMiniAppUser(MiniAppUser miniAppUser)
        {
            _context.MiniAppUsers.Add(miniAppUser);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (MiniAppUserExists(miniAppUser.open_id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetMiniAppUser", new { id = miniAppUser.open_id }, miniAppUser);
        }

        // DELETE: api/MiniAppUser/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMiniAppUser(string id)
        {
            var miniAppUser = await _context.MiniAppUsers.FindAsync(id);
            if (miniAppUser == null)
            {
                return NotFound();
            }

            _context.MiniAppUsers.Remove(miniAppUser);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool MiniAppUserExists(string id)
        {
            return _context.MiniAppUsers.Any(e => e.open_id == id);
        }
    }
}

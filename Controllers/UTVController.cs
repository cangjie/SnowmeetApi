using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.UTV;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class UTVController : ControllerBase
    {
        private readonly ApplicationDBContext _db;

        public UTVController(ApplicationDBContext context)
        {
            _db = context;
        }

        [HttpGet("{reserveAble}")]
        public async Task<ActionResult<IEnumerable<UTVTrip>>> GetTrips(int reserveAble, DateTime date)
        {
            if (reserveAble == 1)
            {
                return Ok(await _db.utvTrip.Where(t => t.reserve_able == 1 && t.trip_date.Date == date.Date).ToListAsync());
            }
            else
            {
                return Ok(await _db.utvTrip.Where(t =>  t.trip_date.Date == date.Date).ToListAsync());
            }
        }

        [HttpGet("{sessionKey}")]
        public async Task<ActionResult<UTVUsers>> GetUserBySessionKey(string sessionKey)
        {
            string openId = await GetOpenId(sessionKey);
            if (openId == null)
            { 
                return NotFound();
            }
            UTVUsers user = await GetUser(openId);
            if (user == null)
            {
                var userList = await _db.MiniAppUsers.Where(u => u.open_id.Trim().Equals(openId.Trim())).ToListAsync();
                if (userList == null || userList.Count == 0)
                {
                    return NotFound();
                }
                MiniAppUser miniAppUser = userList[0];
                UTVUsers utvUser = new UTVUsers()
                {
                    id = 0,
                    wechat_open_id = openId.Trim(),
                    user_id = miniAppUser.member_id,
                    tiktok_open_id = "",
                    real_name = miniAppUser.real_name,
                    cell = miniAppUser.cell_number.Trim(),
                    driver_license = ""
                };
                await _db.utvUser.AddAsync(utvUser);
                await _db.SaveChangesAsync();
                return Ok(utvUser);
            }
            return Ok(user);
        }

        [NonAction]
        public async Task<string> GetOpenId(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            var sessionList = await _db.MiniSessons.Where(m => m.session_key.Trim().Equals(sessionKey)).OrderByDescending(s => s.create_date).ToListAsync();
            if (sessionList == null || sessionList.Count == 0)
            {
                return null;
            }
            return sessionList[0].open_id.Trim();
        }
        [NonAction]
        public async Task<UTVUsers> GetUser(string openId)
        {
            var userList = await _db.utvUser.Where(u => (u.tiktok_open_id.Trim().Equals(openId.Trim()) || u.wechat_open_id.Trim().Equals(openId))).ToListAsync();
            if (userList == null || userList.Count == 0)
            {
                return null;
            }
            else
            {
                return userList[0];
            }

        }
    }
}

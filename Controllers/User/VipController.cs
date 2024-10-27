using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using System.Linq;
namespace SnowmeetApi.Controllers.User
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class VipController:ControllerBase
	{
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        public string _appId = "";

        public VipController(ApplicationDBContext context, IConfiguration config)
		{
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        

        [HttpGet("{cell}")]
        public async Task<ActionResult<Vip>> GetVipInfo(string cell, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var vList = await _context.vip.Where(v => v.cell.Trim().Equals(cell.Trim()))
                .AsNoTracking().ToListAsync();
            if (vList == null || vList.Count == 0)
            {
                return NotFound();
            }
            else
            {
                return Ok(vList[0]);
            }
        }

        [HttpPost]
        public async Task<ActionResult<Vip>> UpdateVipInfo([FromBody] Vip vip, [FromQuery] string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (vip.cell.Length == 11 && System.Text.RegularExpressions.Regex.IsMatch(vip.cell, @"\d[11]")
                && !vip.name.Trim().Equals("") && !vip.memo.Trim().Equals(""))
            {
                var vList = await _context.vip.Where(v => v.cell.Trim().Equals(vip.cell)).ToListAsync();
                if (vList == null || vList.Count == 0)
                {
                    await _context.vip.AddAsync(vip);
                    await _context.SaveChangesAsync();
                    return Ok(vip);
                }
                else
                {
                    Vip v = vList[0];
                    v.cell = vip.cell;
                    v.name = vip.name.Trim();
                    v.memo = vip.memo.Trim();
                    _context.vip.Entry(v).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    return Ok(v);
                }
            }
            else
            {
                return NoContent();
            }
        }

	}
}


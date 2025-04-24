using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.Order
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ShopSaleInteractController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        public MiniAppUserController miniAppUserHelper;

        public MemberController _memberController;


        public ShopSaleInteractController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();

            miniAppUserHelper = new MiniAppUserController(context, config);
            _memberController = new MemberController(context, config);

        }

        [HttpGet]
        public async Task<ActionResult<int>> GetInterviewIdByScene(string scene, string sessionKey, 
            string sessionType = "wechat_mini_openid", int? bizId = null)
        {
            int retId = (int)((OkObjectResult)(await GetInterviewId(sessionKey)).Result).Value;
            ShopSaleInteract ssi = await _context.ShopSaleInteract.FindAsync(retId);
            ssi.scan_type = scene.Trim();
            ssi.biz_id = bizId;
            _context.ShopSaleInteract.Entry(ssi).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(retId);
        }

        [HttpGet]
        public async Task<ActionResult<int>> GetInterviewId(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            
            UnicUser staffUser = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!staffUser.isAdmin)
            {
                return NoContent();
            }
            bool needCreateNew = false;
                    
            int retId = 0;
            try
            {
                var scan = await _context
                    .ShopSaleInteract
                    .Where(s => (s.staff_mapp_open_id == staffUser.miniAppOpenId.Trim()))
                    .OrderByDescending(s => s.id).FirstAsync();
                if (scan == null || scan.scan == 1 || scan.create_date < DateTime.Now.AddMinutes(-600) || scan.needAuth)
                {
                    needCreateNew = true;
                }
                else
                {
                    retId = scan.id;
                }
            }
            catch
            {
                needCreateNew = true;
            }
            
            if (needCreateNew)
            {
               
                var scanNew = new ShopSaleInteract()
                {
                    id = 0,
                    staff_mapp_open_id = staffUser.miniAppOpenId.Trim(),
                    scan = 0,
                    staff_member_id = staffUser.member.id
                };
                await _context.AddAsync(scanNew);
                await _context.SaveChangesAsync();
                return Ok(scanNew.id);
            }
            else
            {
                return Ok(retId);
                //return BadRequest();
            }
            //return NotFound();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ShopSaleInteract>> GetScanInfo(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser staffUser = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!staffUser.isAdmin)
            {
                return NoContent();
            }
            var scan = await _context.ShopSaleInteract.FindAsync(id);
            bool haveAuthed = false;
            if (scan.cell != null)
            {
                List<ShopSaleInteract> scanList = await _context.ShopSaleInteract
                    .Where(s => s.cell.Trim().Equals(scan.cell) && s.create_date >= scan.create_date.AddHours(-1)
                    && s.auth_manager_member_id != null).OrderByDescending(s => s.id).AsNoTracking().ToListAsync();
                if (scanList.Count > 0)
                {
                    scan.auth_manager_member_id = scanList[0].auth_manager_member_id;
                    scan.scan = 1;
                    _context.ShopSaleInteract.Entry(scan).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    haveAuthed = true;
                }
                
                //return Ok(scan);


            }
            if (scan.scaner_oa_open_id.Trim().Equals("") && scan.auth_manager_member_id == null && !haveAuthed)
            {

                return NotFound();
            }
            UnicUser scanUser = await UnicUser.GetUnicUserByDetailInfo(scan.scaner_oa_open_id, "wechat_oa_openid", _context);
            if (scanUser!= null && !scanUser.miniAppOpenId.Trim().Equals(""))
            {
                
                scan.member = scanUser.member;
                scan.miniAppUser = scan.member.miniAppUser;
                
            }
            if (scanUser == null || scanUser.member == null)
            {
                await _context.ShopSaleInteract.Entry(scan).Reference(s => s.scanMember).LoadAsync();
                await _context.member.Entry(scan.scanMember).Collection(m => m.memberSocialAccounts).LoadAsync();
                scan.member = scan.scanMember;
            }
            if (scan == null)
            {
              
                return NotFound();
            }
            else
            {
                return scan;
            }
        }
        [HttpGet("{interactId}")]
        public async Task<ActionResult<ShopSaleInteract>> SetOpenIdByCell(int interactId, string cell, string openId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            Models.Users.CellWhiteList cellWhite = await _context.cellWhiteList.FindAsync(cell.Trim());

            List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                .Where(m => m.num.Trim().Equals(cell) && m.type.Trim().Equals("cell") && m.valid == 1)
                .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
            int? memberId = null;
            if (msaList.Count > 0)
            {
                memberId = msaList[0].member_id;
            }

            ShopSaleInteract interact = await _context.ShopSaleInteract.FindAsync(interactId);
            interact.scaner_mini_open_id = openId.Trim();
            interact.cell = cell.Trim();
            interact.scaner_member_id = memberId;

            if (cellWhite != null)
            {
                interact.scan = 1;
                interact.auth_manager_member_id = 0;
            }



            _context.ShopSaleInteract.Entry(interact).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(interact);
        }
        [HttpGet]
        public async Task<ActionResult<List<ShopSaleInteract>>> GetAuthList(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<ShopSaleInteract> authList = (await _context.ShopSaleInteract
                .Include(s => s.scanMember).ThenInclude(m => m.memberSocialAccounts.Where(s => s.valid == 1))
                .Include(s => s.staffMember).ThenInclude(m => m.memberSocialAccounts.Where(s => s.valid == 1))
                .Where(s => s.create_date >= DateTime.Now.AddDays(-7).Date && s.scaner_member_id != null)
                .OrderByDescending(s=>s.id).ToListAsync()).Where(a => a.needAuth).ToList();
            return Ok(authList);
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<ShopSaleInteract>> Auth(int id, string sessionKey, 
            string sessionType = "wechat_mini_openid")
        {
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user.member.is_admin == 0 && user.member.is_manager == 0)
            {
                return BadRequest();
            }
            ShopSaleInteract scan = await _context.ShopSaleInteract.FindAsync(id);
            scan.scan = 1;
            scan.auth_manager_member_id = user.member.id;
            _context.ShopSaleInteract.Entry(scan).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(scan);
        }
        [NonAction]
        private bool ShopSaleInteractExists(int id)
        {
            return _context.ShopSaleInteract.Any(e => e.id == id);
        }
    }
}

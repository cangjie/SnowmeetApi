using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Order;
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
        public async Task<ActionResult<int>> GetInterviewIdByScene(string scene, string sessionKey, string sessionType = "wechat_mini_openid", int? bizId = null)
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
                if (scan == null || scan.scan == 1 || scan.create_date < DateTime.Now.AddMinutes(-600))
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
                    scan = 0
                };
                await _context.AddAsync(scanNew);
                await _context.SaveChangesAsync();
                return scanNew.id;
            }
            else
            {
                return Ok(retId);
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
            if (scan.scaner_oa_open_id.Trim().Equals(""))
            {
                return NotFound();
            }
            UnicUser scanUser = await UnicUser.GetUnicUserByDetailInfo(scan.scaner_oa_open_id, "wechat_oa_openid", _context);
            if (scanUser!= null && !scanUser.miniAppOpenId.Trim().Equals(""))
            {
                
                scan.member = scanUser.member;
                scan.miniAppUser = scan.member.miniAppUser;
                
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
                [NonAction]
        private bool ShopSaleInteractExists(int id)
        {
            return _context.ShopSaleInteract.Any(e => e.id == id);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;

using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using System.Collections;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class SchoolController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        //private readonly Order.OrderPaymentController payCtrl;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;

        private readonly User.MemberController _memberHelper;

        public SchoolController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _memberHelper = new User.MemberController(_db, _config);
        }

        [HttpGet]
        public async Task<ActionResult<Models.School.Staff>> GetStaffInfo(string sessionKey, string sessionType="wl_wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            sessionType = Util.UrlDecode(sessionType.Trim());
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            var staffList = await _db.schoolStaff.Where(s => s.member_id == member.id).AsNoTracking().ToListAsync();
            if (staffList.Count <= 0)
            {
                return null;
            }
            else
            {
                return Ok(RemoveSensitiveInfo(staffList[0]));
            }

        }
        [NonAction]
        public Models.School.Staff RemoveSensitiveInfo(Models.School.Staff staff)
        {
            staff.member_id = 0;
            return staff;
        }


       



    }
}
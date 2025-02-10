using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Deposit;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class DepositController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public DepositController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }
        /*
        [HttpGet]
        public async Task<ActionResult<List<DepositAccount>>> GetAccounts()
        {
            var l = await _db.depositAccount.ToListAsync();
            for(int i = 0; i < l.Count; i++)
            {
                l[i].type = DepositType.现金预存.ToString();
            }
            return Ok(l);
        }
        */

        [NonAction]
        public async Task<List<DepositAccount>> GetMemberAccountAvaliable(int memberId, string type, string subType)
        {
            type = Util.UrlDecode(type);
            subType = Util.UrlDecode(subType);
            List<DepositAccount> list = await _db.depositAccount
                .Where(a => ( (a.expire_date == null || ((DateTime)a.expire_date).Date >= DateTime.Now.Date)
                && a.consume_amount < a.income_amount && a.member_id == memberId)
                && (type.Trim().Equals("") || a.type.Trim().Equals(type))
                && (subType.Trim().Equals("") || subType.Trim().Equals(a.sub_type.Trim())))
                .OrderBy(a => a.expire_date).AsNoTracking().ToListAsync();
            return list;
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<double>> GetMemberAvaliableAmount(int memberId, string depositType, string depositSubType,
            string sessionKey, string sessionType)
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            if (member.id != memberId && !(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            double sum = 0;
            List<DepositAccount> accountList = await GetMemberAccountAvaliable(memberId, depositType, depositSubType);
            for(int i = 0; i < accountList.Count; i++)
            {
                sum += accountList[i].avaliableAmount;
            }

            return Ok(sum);
        }
        [HttpGet]
        public async Task<DepositAccount> ChargeByTemplate(int templateId)
        {
            DepositTemplate template = await _db.depositTemplate.FindAsync(templateId);
            if (template == null)
            {
                return null;
            }
            
            return null;
        }

    }   
}
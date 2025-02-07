using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        public async Task<List<DepositAccount>> GetMemberAccountAvaliable(int memberId)
        {
            List<DepositAccount> list = await _db.depositAccount
                .Where(a => ( (a.expire_date == null || ((DateTime)a.expire_date).Date >= DateTime.Now.Date)
                && a.consume_amount < a.income_amount && a.member_id == memberId))
                .OrderBy(a => a.expire_date).AsNoTracking().ToListAsync();
            return list;
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
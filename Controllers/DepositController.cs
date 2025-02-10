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
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;
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
        [HttpGet("{memberId}")]
        public async Task<ActionResult<List<DepositBalance>>> DepositCosume(int paymentId, 
            string depositType, string depositSubType, string sessionKey, string sessionType)
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            if (payment == null || payment.status.Trim().Equals("支付成功"))
            {
                return NotFound();
            }
            Member customer = await _memberHelper.GetMember(payment.open_id.Trim(), "wechat_mini_openid");
            if (customer == null)
            {
                return NoContent();
            }
            int memberId = customer.id;
            List<DepositAccount> accountList = await GetMemberAccountAvaliable(memberId, depositType, depositSubType);
            List<DepositBalance> balanceList = new List<DepositBalance>();
            double paidAmount = 0;
            double unPaidAmount = payment.amount;
            for(int i = 0; i < accountList.Count && unPaidAmount > 0; i++)
            {
                if (accountList[i].avaliableAmount >= unPaidAmount)
                {
                    DepositBalance balance = CreateDepositBalance(accountList[i], payment.amount);
                    if (balance != null)
                    {
                        paidAmount += -1 * balance.amount;
                        unPaidAmount = payment.amount - paidAmount;
                        balance.payment_id = payment.id;
                        balance.order_id = payment.order_id;
                        balanceList.Add(balance);
                    }
                }
                else
                {
                    DepositBalance balance = CreateDepositBalance(accountList[i], accountList[i].avaliableAmount);
                    if (balance != null)
                    {
                        paidAmount += -1 * balance.amount;
                        unPaidAmount = payment.amount - paidAmount;
                        balance.payment_id = payment.id;
                        balance.order_id = payment.order_id;
                        balanceList.Add(balance);
                    }
                }

            }
            if (unPaidAmount > 0)
            {
                return BadRequest();
            }
            for(int i = 0; i < balanceList.Count; i++)
            {
                DepositBalance balance = balanceList[i];
                await _db.depositBalance.AddAsync(balance);
                for(int j = 0; j < accountList.Count; j++)
                {
                    if (accountList[j].id == balance.deposit_id)
                    {
                        DepositAccount account = accountList[j];
                        account.consume_amount += balance.amount;
                        _db.depositAccount.Entry(account).State = EntityState.Modified;
                    }
                }
            }
            payment.status = "支付成功";
            _db.OrderPayment.Entry(payment).State = EntityState.Modified;
            try
            {
                await _db.SaveChangesAsync();
                return Ok(balanceList);
            }
            catch
            {
                return BadRequest();
            }
        }
        [NonAction]
        public DepositBalance CreateDepositBalance(DepositAccount account, double amount)
        {
            if (account.avaliableAmount < amount)
            {
                return null;
            }
            DepositBalance balance = new DepositBalance()
            {
                id = 0,
                deposit_id = account.id,
                member_id = account.member_id,
                amount = -1 * amount,
                payment_id = null,
                order_id = null,
                extend_expire_date = null,
                memo = "",
                biz_id = null,
                source = "",
                create_date = DateTime.Now
            };
            return balance;
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
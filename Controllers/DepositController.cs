using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Deposit;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models;
using System.Security;
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
        
        [NonAction]
        public async Task<List<DepositAccount>> GetMemberAccountAvaliable(int memberId, string type, string subType)
        {
            type = Util.UrlDecode(type);
            subType = Util.UrlDecode(subType);
            List<DepositAccount> list = await _db.depositAccount
                .Where(a => ( (a.expire_date == null || ((DateTime)a.expire_date).Date >= DateTime.Now.Date)
                && a.valid == 1 && a.member_id == memberId)
                && (type.Trim().Equals("") || a.type.Trim().Equals(type))
                && (subType.Trim().Equals("") || subType.Trim().Equals(a.sub_type.Trim()))
                && (a.expire_date == null || ((DateTime)a.expire_date).Date >= DateTime.Now.Date))
                .Include(a => a.balances.Where(b => b.valid == 1).OrderByDescending(b => b.id))
                    .ThenInclude(b => b.order)
                .OrderBy(a => a.expire_date).AsNoTracking().ToListAsync();
            return list;
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<double>> GetMemberAvaliableAmount(int memberId, string depositType, string depositSubType,
            string sessionKey, string sessionType = "wechat_mini_openid")
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
        [HttpGet("{rentOrderId}")]
        public async Task<ActionResult<List<DepositBalance>>> RentOderPay(int rentOrderId, double amount, 
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            RentOrder rentOrder = await _db.RentOrder.FindAsync(rentOrderId);
            if (rentOrder == null || rentOrder.order_id == 0)
            {
                return NotFound();
            }
            OrderPayment payment = await CreateDepositPayment((int)rentOrder.order_id, amount, sessionKey, sessionType);
            if (payment == null)
            {
                return BadRequest();
            }

            return await DepositCosume(payment.id, sessionKey, sessionType);
        }
        [NonAction]
        public async Task<OrderPayment> CreateDepositPayment(int orderId, double amount, 
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null || member.wechatMiniOpenId == null)
            {
                return null;
            }
            OrderOnline order = await _db.OrderOnlines.FindAsync(orderId);
            OrderPayment payment = new OrderPayment()
            {
                id = 0,
                order_id = orderId,
                open_id = order.open_id,
                amount = amount,
                status = OrderPayment.PaymentStatus.待支付.ToString(),
                deposit_type = "服务储值",
                deposit_sub_type = "",
                staff_open_id = member.wechatMiniOpenId.Trim(),
                pay_method = "储值支付",
                create_date = DateTime.Now
            };
            await _db.OrderPayment.AddAsync(payment);
            await _db.SaveChangesAsync();
            return payment;
        }
        [HttpGet("{paymentId}")]
        public async Task<ActionResult<List<DepositBalance>>> DepositCosume(int paymentId, 
            string sessionKey, string sessionType = "wechat_mini_openid")
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
            List<DepositAccount> accountList = await GetMemberAccountAvaliable(memberId, 
                payment.deposit_type, payment.deposit_sub_type);
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
                        account.consume_amount += balance.amount * -1;
                        account.update_date = DateTime.Now;
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
        [HttpGet("{cell}")]
        public async Task<ActionResult<DepositAccount>> DepositCharge(int accountId, string cell, double chargeAmount, 
            DateTime expireDate, string sessionKey, string sessionType = "wechat_mini_openid", 
             string type = "服务储值", string subType = "", string? mi7OrderId = null, string? memo = null)
        {
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMember(cell.Trim(), "cell");
            if (member == null)
            {
                return BadRequest();
            }
            
            DepositAccount? account = null;
            if (accountId != 0)
            {
                account = await _db.depositAccount.FindAsync(accountId);
                if (account == null || account.member_id != member.id)
                {
                    return BadRequest();
                }
            }
            else
            {
                List<DepositAccount> accList = await _db.depositAccount
                    .Where(a => a.valid == 1 && a.member_id ==  member.id 
                    && a.type.Trim().Equals(type.Trim()) && a.sub_type.Trim().Equals(subType.Trim()) )
                    .ToListAsync();
                if (accList == null || accList.Count == 0)
                {
                    account = new DepositAccount()
                    {
                        id = 0,
                        member_id = member.id,
                        type = "服务储值",
                        sub_type = "",
                        expire_date = expireDate,
                        income_amount = 0,
                        consume_amount = 0,
                        biz_id = mi7OrderId.Trim(),
                        memo = memo,
                        create_date = DateTime.Now,
                        create_member_id = user.member.id
                    };
                    await _db.depositAccount.AddAsync(account);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    account = accList[0];
                }
            }
            if (account == null || account.id == 0)
            {
                return NoContent();
            }
            double sumIncome = await _db.depositBalance
                .Where(b => b.valid == 1 && b.amount > 0 && b.deposit_id == account.id)
                .SumAsync(b => b.amount);
            double sumConsume = await _db.depositBalance.Where(b => b.valid == 1 && b.amount < 0 && b.deposit_id == account.id )
                .SumAsync(b => b.amount);
            DepositBalance b = new DepositBalance()
            {
                id = 0,
                deposit_id = account.id,
                amount = chargeAmount,
                member_id = user.member.id,
                biz_id = mi7OrderId,
                memo = memo,
                valid = 1,
                create_date = DateTime.Now
            };
            await _db.depositBalance.AddAsync(b);
            sumIncome += chargeAmount;
            account.income_amount = sumIncome;
            account.consume_amount = -1 * sumConsume;
            _db.depositAccount.Entry(account).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            account = (await _db.depositAccount.Where(a => a.id == account.id)
                .Include(a => a.balances.Where(b => b.valid == 1)).AsNoTracking().ToListAsync())[0];
            return Ok(account);
        }
        [NonAction]
        public async Task<DepositAccount> DepositAccountCharge(DepositAccount account, double amount, int operMemberId)
        {
            DepositBalance b = new DepositBalance()
            {
                id = 0,
                deposit_id = account.id,
                amount = amount,
                member_id = operMemberId,
                create_date = DateTime.Now
            };

            return null;
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<List<DepositAccount>>> GetAccounts(int memberId, string type, 
            string subType, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (subType == null)
            {
                subType = "";
            }
            type = Util.UrlDecode(type);
            subType = Util.UrlDecode(subType);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (memberId != user.memberId && !user.isAdmin)
            {
                return BadRequest();
            }
            List<DepositAccount> al = await GetMemberAccountAvaliable(memberId, type.Trim(), subType.Trim());
            return Ok(al);
        }
        [HttpGet]
        public async Task<ActionResult<List<DepositAccount>>> GetMyAccounts(string type, 
            string subType, string sessionKey, string sessionType = "wechat_mini_openid")
        {
           
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            return await GetAccounts(user.memberId, type, subType, sessionKey, sessionType);
        }
        [NonAction]
        public async Task<int?> GetMi7OrderId(string mi7OrderId)
        {
            List<Mi7Order> mi7OrderList = await _db
                .mi7Order.Where(o => o.mi7_order_id.Trim().Equals(mi7OrderId.Trim()) && o.order_id > 0)
                .AsNoTracking().ToListAsync();
            if (mi7OrderList != null && mi7OrderList.Count > 0)
            {
                return mi7OrderList[0].order_id;
            }
            else
            {
                return null;
            }
        }

    }   
}
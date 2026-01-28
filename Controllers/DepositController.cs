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
                .Where(a => ((a.expire_date == null || ((DateTime)a.expire_date).Date >= DateTime.Now.Date)
                && a.valid == 1 && a.member_id == memberId)
                && (type.Trim().Equals("") || a.type.Trim().Equals(type))
                && (subType.Trim().Equals("") || subType.Trim().Equals(a.sub_type.Trim()))
                && (a.expire_date == null || ((DateTime)a.expire_date).Date >= DateTime.Now.Date))
                .Include(a => a.balances.Where(b => b.valid == 1).OrderByDescending(b => b.id))
                    .ThenInclude(b => b.order)
                .Include(a => a.member)
                    .ThenInclude(m => m.memberSocialAccounts.Where(msa => msa.valid == 1))
                .OrderBy(a => a.expire_date).AsNoTracking().ToListAsync();
            return list;
        }
        [NonAction]
        public async Task<double> GetMemberTotalAmount(int memberId)
        {
            List<DepositAccount> accountList = await GetMemberAccountAvaliable(memberId, "", "");
            double sum = 0;
            for (int i = 0; i < accountList.Count; i++)
            {
                sum += accountList[i].income_amount;
            }
            return sum;
        }
        [NonAction]
        public async Task<double> GetMemberSummaryAmount(int memberId)
        {
            List<DepositAccount> accountList = await GetMemberAccountAvaliable(memberId, "", "");
            double sum = 0;
            for (int i = 0; i < accountList.Count; i++)
            {
                sum += accountList[i].avaliableAmount;
            }
            return sum;
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<double?>>> GetMemberTotalAmountByStaff(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> checkRightResult = await _staffHelper.CheckStaffLevel(0, sessionKey, sessionType);
            if (checkRightResult != null)
            {
                return Ok(checkRightResult);
            }
            double sum = 0;
            try
            {
                sum = await GetMemberTotalAmount(memberId);
            }
            catch
            {

            }
            return Ok(new ApiResult<double?>
            {
                code = 0,
                message = "",
                data = sum
            });
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<double?>>> GetMemberSummaryAmountByStaff(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> checkRightResult = await _staffHelper.CheckStaffLevel(0, sessionKey, sessionType);
            if (checkRightResult != null)
            {
                return Ok(checkRightResult);
            }
            double sum = 0;
            try
            {
                sum = await GetMemberSummaryAmount(memberId);
            }
            catch
            {

            }
            return Ok(new ApiResult<double?>
            {
                code = 0,
                message = "",
                data = sum
            });
        }
        /*
        [HttpGet("{memberId}")]
        public async Task<ActionResult<double>> GetMemberAvaliableAmount(int memberId, string depositType, string depositSubType,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
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
        */
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
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null || member.wechatMiniOpenId == null)
            {
                return null;
            }
            OrderOnline order = await _db.OrderOnlines.FindAsync(orderId);
            OrderPayment payment = new OrderPayment()
            {
                id = 0,
                order_id = orderId,
                open_id = null,
                member_id = 0,
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
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId);
            if (payment == null || payment.status.Trim().Equals("支付成功"))
            {
                return NotFound();
            }
            Member customer = await _memberHelper.GetWholeMemberByNum(payment.open_id.Trim(), "wechat_mini_openid");
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
            for (int i = 0; i < accountList.Count && unPaidAmount > 0; i++)
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
            for (int i = 0; i < balanceList.Count; i++)
            {
                DepositBalance balance = balanceList[i];
                await _db.depositBalance.AddAsync(balance);
                for (int j = 0; j < accountList.Count; j++)
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
        [HttpGet("{memberId}")]
        public async Task<ActionResult<DepositAccount>> DepositCharge(int memberId, int accountId, double chargeAmount,
            DateTime expireDate, string sessionKey, string sessionType = "wechat_mini_openid",
             string type = "服务储值", string subType = "", string? mi7OrderId = null, string? bizType = null, string? memo = null)
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest();
            }
            DepositAccount? account = null;
            if (accountId != 0)
            {
                account = await _db.depositAccount.FindAsync(accountId);
                if (account == null || (account.member_id != memberId && memberId != 0))
                {
                    return BadRequest();
                }
            }
            else
            {
                List<DepositAccount> accList = await _db.depositAccount
                    .Where(a => a.valid == 1 && a.member_id == memberId
                    && a.type.Trim().Equals(type.Trim()) && a.sub_type.Trim().Equals(subType.Trim()))
                    .ToListAsync();
                if (accList == null || accList.Count == 0)
                {
                    account = new DepositAccount()
                    {
                        id = 0,
                        member_id = memberId,
                        type = "服务储值",
                        sub_type = "",
                        expire_date = expireDate,
                        income_amount = 0,
                        consume_amount = 0,
                        biz_id = mi7OrderId.Trim(),
                        memo = memo,
                        create_date = DateTime.Now,
                        create_member_id = staff.id
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
            double sumConsume = await _db.depositBalance.Where(b => b.valid == 1 && b.amount < 0 && b.deposit_id == account.id)
                .SumAsync(b => b.amount);
            DepositBalance b = new DepositBalance()
            {
                id = 0,
                deposit_id = account.id,
                amount = chargeAmount,
                member_id = staff.id,
                biz_id = mi7OrderId,
                biz_type = bizType,
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
        public DepositAccount DepositAccountCharge(DepositAccount account, double amount, int operMemberId)
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
            /*
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (memberId != user.member.id && !user.isAdmin)
            {
                return BadRequest();
            }
            */
            List<DepositAccount> al = await GetMemberAccountAvaliable(memberId, type.Trim(), subType.Trim());
            return Ok(al);
        }
        
        [HttpGet]
        public async Task<ActionResult<List<DepositAccount>>> GetMyAccounts(string type, 
            string subType, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            return await GetAccounts(member.id, type, subType, sessionKey, sessionType);
        }
        
        [HttpGet]
        public async Task<ActionResult<List<Member>>> SearchMember(string key,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            */
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest();
            }
            MemberController _memberHelper = new MemberController(_db, _config);
            List<Member> members = await _memberHelper.SearchMember(key);
            OrderController _orderHelper = new OrderController(_db, _config, null);
            for (int i = 0; i < members.Count; i++)
            {
                Member member = members[i];
                await _db.member.Entry(member).Collection(m => m.memberSocialAccounts).LoadAsync();
                member.memberSocialAccounts = member.memberSocialAccounts.Where(m => m.valid == 1).ToList();
                if (member.wechatMiniOpenId == null)
                {
                    members.RemoveAt(i);
                    i--;
                    continue;
                }
                member.depositAccounts = await _db.member.Entry(member).Collection(m => m.depositAccounts)
                    .Query().Where(a => a.valid == 1).ToListAsync();

                /*
                member.orders = await _db.OrderOnlines
                    .Where(o => o.pay_state == 1 && o.open_id.Trim().Equals(member.wechatMiniOpenId.Trim()) && o.type.Trim().Equals("店销现货"))
                    .Include(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                    .OrderByDescending(o => o.pay_time).ToListAsync();
                */
                member.orders = await _orderHelper.GetCommonOrders(null, null, member.id, null, "零售", null, null, null, null, null, null, null, null, null, null, null, null);


            }
            return Ok(members);
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult<Member>> GetMember(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            OrderController _orderHelper = new OrderController(_db, _config, null);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest();
            }
            Member member = await _db.member.FindAsync(memberId);
            member.memberSocialAccounts = await _db.member.Entry(member)
                .Collection(m => m.memberSocialAccounts).Query()
                .Where(msa => msa.valid == 1).AsNoTracking().ToListAsync();
            member.depositAccounts = await _db.member.Entry(member)
                .Collection(m => m.depositAccounts).Query()
                .Where(a => a.valid == 1).AsNoTracking().ToListAsync();
            /*
            member.orders = await _db.OrderOnlines
                .Where(o => o.pay_state == 1 && o.type.Trim().Equals("店销现货")
                    && o.open_id.Trim().Equals(member.wechatMiniOpenId.Trim()))
                .Include(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || r.refund_id.Trim().Equals("")))
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            */
            member.orders = await _orderHelper.GetCommonOrders(null, null, member.id, null, "零售", null, null, null, null, null, null, null, null, null, null, null, null);
            return Ok(member);
        }

        [HttpGet]
        public async Task<ActionResult<List<DepositAccount>>> SearchDepositAccounts(string key,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (key == null)
            {
                key = "";
            }
            key = Util.UrlDecode(key);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 100)
            {
                return BadRequest();
            }
            if (key.Trim().Equals(""))
            {
                return Ok(await _db.depositAccount.Where(a => a.valid == 1)
                    .Include(d => d.member)
                        .ThenInclude(m => m.memberSocialAccounts)
                    .OrderByDescending(a => a.id).AsNoTracking().ToListAsync());
            }
            MemberController _memberHelper = new MemberController(_db, _config);
            List<Member> members = await _memberHelper.SearchMember(key);
            List<DepositAccount> ret = new List<DepositAccount>();
            for (int i = 0; members != null && i < members.Count; i++)
            {
                Member member = members[i];
                member.depositAccounts = await _db.member.Entry(member)
                    .Collection(m => m.depositAccounts)
                    .Query().Where(d => d.valid == 1)
                    .ToListAsync();
                for (int j = 0; j < member.depositAccounts.Count; j++)
                {
                    DepositAccount account = member.depositAccounts[j];
                    account.member = member;
                    ret.Add(account);
                }
            }
            return Ok(ret);
        }

        [HttpGet("{accountId}")]
        public async Task<ActionResult<DepositAccount>> GetAccount(int accountId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            DepositAccount account = await _db.depositAccount.FindAsync(accountId);
            _db.depositAccount.Entry(account).State = EntityState.Detached;
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest();
            }
            //await _db.depositAccount.Entry(account)
            //    .Collection(a => a.balances).LoadAsync();
            account.balances = await _db.depositBalance
                .Include(b => b.order).ThenInclude(o => o.cares)
                .Include(b => b.order).ThenInclude(o => o.rentals)
                .Where(b => b.deposit_id == account.id).AsNoTracking().ToListAsync();
            await _db.depositAccount.Entry(account)
                .Reference(a => a.member).LoadAsync();
            await _db.member.Entry(account.member)
                .Collection(m => m.memberSocialAccounts).LoadAsync();
            account.balances = account.balances.Where(b => b.valid == 1)
                .OrderByDescending(b => b.id).ToList();
            account.member.memberSocialAccounts
                = account.member.memberSocialAccounts.Where(a => a.valid == 1)
                .ToList();
            return Ok(account);
        }
        [HttpGet("{accountId}")]
        public async Task<ActionResult<DepositAccount>> ModAccountInfo(int accountId, string bizId, string memo,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (bizId == null)
            {
                bizId = "";
            }
            if (memo == null)
            {
                memo = "";
            }
            bizId = Util.UrlDecode(bizId);
            memo = Util.UrlDecode(memo);

            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest();
            }
            DepositAccount account = await _db.depositAccount.FindAsync(accountId);
            await _db.depositAccount.Entry(account).Collection(m => m.balances).LoadAsync();
            account.balances = account.balances.Where(b => b.valid == 1).OrderBy(m => m.id).ToList();
            account.biz_id = bizId;
            account.memo = memo;
            account.balances[0].biz_id = bizId;
            account.balances[0].memo = memo;
            _db.depositAccount.Entry(account).State = EntityState.Modified;
            _db.depositBalance.Entry(account.balances[0]).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(account);
        }
        /*
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
        */
        [HttpGet("{type}")]
        public async Task<ActionResult<List<DepositBalance>>> GetAllBalance(string type, DateTime start, DateTime end,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest();
            }
            List<DepositBalance> bList = await _db.depositBalance
                .Include(b => b.depositAccount)
                    .ThenInclude(a => a.member)
                        .ThenInclude(m => m.memberSocialAccounts)
                .Include(b => b.order)
                    .ThenInclude(o => o.cares)
                .Include(b => b.order)
                    .ThenInclude(o => o.rentals)
                .Where(b => b.valid == 1 && b.create_date.Date >= start.Date && b.create_date.Date <= end.Date
                && (type.Trim().Equals("all") ? true : (type.Trim().Equals("income") ? b.amount > 0 : b.amount < 0)))
                .OrderByDescending(b => b.id).AsNoTracking().ToListAsync();
            return Ok(bList);
        }
        /////////////////////////////////////
        /// new season
        /// ///////////////////////////////
        /// [HttpGet("{paymentId}")]
        [HttpGet]
        public async Task<List<DepositBalance>> ConsumeDeposit(OrderPayment payment)
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            /*
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return null;
            }
            */
            //OrderPayment payment = await _db.OrderPayment.Where(p => p.id == paymentId)
            //    .AsNoTracking().FirstOrDefaultAsync();
            if (payment == null || payment.status.Trim().Equals("支付成功"))
            {
                return null;
            }
            if (payment.member_id == null)
            {
                return null;
            }
            Member customer = await _memberHelper.GetWholeMemberById((int)payment.member_id);
            if (customer == null)
            {
                return null;
            }
            int memberId = customer.id;
            List<DepositAccount> accountList = await GetMemberAccountAvaliable(memberId,
                payment.deposit_type, payment.deposit_sub_type);
            List<DepositBalance> balanceList = new List<DepositBalance>();
            double paidAmount = 0;
            double unPaidAmount = payment.amount;
            for (int i = 0; i < accountList.Count && unPaidAmount > 0; i++)
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
                return null;
            }
            for (int i = 0; i < balanceList.Count; i++)
            {
                DepositBalance balance = balanceList[i];
                await _db.depositBalance.AddAsync(balance);
                for (int j = 0; j < accountList.Count; j++)
                {
                    if (accountList[j].id == balance.deposit_id)
                    {
                        DepositAccount account = accountList[j];
                        account.consume_amount += balance.amount * -1;
                        account.update_date = DateTime.Now;
                        try
                        {
                            _db.depositAccount.Entry(account).State = EntityState.Modified;
                        }
                        catch
                        {
                            _db.depositAccount.Entry(account).State = EntityState.Detached;
                            await _db.SaveChangesAsync();
                            _db.depositAccount.Entry(account).State = EntityState.Modified;
                        }
                    }
                }
            }
            payment.status = "支付成功";
            try
            {
                _db.OrderPayment.Entry(payment).State = EntityState.Modified;
            }
            catch
            {
                _db.orderPayment.Entry(payment).State = EntityState.Detached;
                await _db.SaveChangesAsync();
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
            }
            try
            {
                await _db.SaveChangesAsync();
                for(int i = 0; i < accountList.Count; i++)
                {
                    _db.depositAccount.Entry(accountList[i]).State = EntityState.Detached;
                }
                await _db.SaveChangesAsync();
                return balanceList;
            }
            catch
            {
                return null;
            }
        }
        [HttpGet]
        public async Task<ActionResult<DepositBalance>> FixDepositOrder(string code, double amount)
        {
            Models.Order order = await _db.order.Where(o => o.code == code && o.valid == 1)
                .Include(o => o.payments).ThenInclude(p => p.refunds.Where(r => r.state == 1 || r.refund_id != ""))
                .Include(o => o.payments).ThenInclude(p => p.depositBalances)
                .AsNoTracking().FirstOrDefaultAsync();
            List<OrderPayment> deopsitPayments = order.payments.Where(p => p.pay_method == "储值支付").ToList();
            for(int i = 0; i < deopsitPayments.Count; i++)
            {
                OrderPayment payment = deopsitPayments[i];
                payment.valid = 0;
                payment.update_date = DateTime.Now;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                for(int j = 0; payment.depositBalances != null && j < payment.depositBalances.Count; j++)
                {
                    payment.depositBalances[j].valid = 0;
                    payment.depositBalances[j].update_date = DateTime.Now;
                    _db.depositBalance.Entry(payment.depositBalances[j]).State = EntityState.Modified;
                }
            }
            OrderPayment newPayment = new OrderPayment()
            {
                id = 0,
                order_id = order.id,
                member_id = order.member_id,
                amount = amount,
                pay_method = "储值支付",
                deposit_type = "服务储值",
                status = OrderPayment.PaymentStatus.待支付.ToString(),
                create_date = DateTime.Now
            };
            await _db.orderPayment.AddAsync(newPayment);
            await _db.SaveChangesAsync();
            List<DepositBalance> balances = await ConsumeDeposit(newPayment);
            if (balances == null)
            {
                return NoContent();
            }
            newPayment = await _db.orderPayment.Where(p => p.id == newPayment.id)
                .Include(p => p.depositBalances).AsNoTracking().FirstOrDefaultAsync();
            return Ok(newPayment);
        }



        /*
        [NonAction]
        public async Task Fix2025FallItem(Models.Order order)
        {
            double consumeAmount = 0;
            if (order.type == "养护")
            {
                consumeAmount = order.total_amount;
                if (consumeAmount == 0)
                {
                    
                    double totalAmount = 0;
                    for(int i = 0; order.cares != null && i < order.cares.Count; i++)
                    {
                        Care care = order.cares[i];
                        if (care.need_edge == 1 && care.need_wax == 1)
                        {
                            totalAmount += 230;
                        }
                        if (care.need_edge == 1 || care.need_wax == 1)
                        {
                            totalAmount += 170;
                        }
                    }
                    consumeAmount = totalAmount;
                }

            }
            if (order.type == "租赁")
            {
                if (order.totalRentSummaryAmount != null)
                    consumeAmount = (double)order.totalRentSummaryAmount;
            }
            if (consumeAmount == 0)
            {
                return;
            }
            //return;
            OrderPayment payment = new OrderPayment()
            {
                id = 0,
                order_id = order.id,
                amount = consumeAmount,
                member_id = order.member_id,
                pay_method = "储值支付",
                status = OrderPayment.PaymentStatus.待支付.ToString(),
                deposit_type = "服务储值",
                create_date = DateTime.Now
            };
            await _db.orderPayment.AddAsync(payment);
            await _db.SaveChangesAsync();
            //_db.orderPayment.Entry(payment).State = EntityState.Detached;
            CoreDataModLog log = Util.CreateCoreDataModLog("OrderPay", "id", payment.id, null, consumeAmount.ToString(), null, null, "储值补扣", null);
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();

            List<DepositBalance>? balances = await ConsumeDeposit(payment);
            if (balances == null)
            {
                CoreDataModLog logFail = Util.CreateCoreDataModLog("OrderPay", "id", payment.id, null, consumeAmount.ToString(), null, null, "储值补扣失败", null);
                await _db.coreDataModLog.AddAsync(logFail);
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet]
        public async Task Fix2025Fall()
        {

            OrderController _orderHelper = new OrderController(_db, _config, null);
            DateTime fallDate = DateTime.Parse("2025-10-15");
            List<Models.Order> orders = await _db.order.Where(o => o.valid == 1 && o.memo.IndexOf("储值") >= 0 && o.create_date > fallDate)
                .AsNoTracking().ToListAsync();
            List<Models.Care> cares = await _db.care.Include(c => c.order)
                .Where(c => c.order_id != null && c.valid == 1 && c.order.valid == 1 && (c.memo.IndexOf("储值") >= 0 || c.order.memo.IndexOf("储值") >= 0) && c.create_date > fallDate)
                .AsNoTracking().ToListAsync();
            List<Rental> rentals = await _db.rental.Include(r => r.order)
                .Where(r => r.order_id != null && r.valid == 1 && r.order.valid == 1 && r.create_date > fallDate && (r.memo.IndexOf("储值") >= 0 || r.order.memo.IndexOf("储值") >= 0))
                .AsNoTracking().ToListAsync();
            List<Models.Order> depositOrders = new List<Models.Order>();
            for (int i = 0; i < orders.Count; i++)
            {
                if (depositOrders.Where(o => o.id == orders[i].id).ToList().Count == 0)
                {
                    Models.Order order = await _orderHelper.GetOrder(orders[i].id);
                    depositOrders.Add(order);
                    if (order.availablePayments.Where(p => p.pay_method == "储值支付" && p.status == OrderPayment.PaymentStatus.支付成功.ToString()).ToList().Count == 0
                        && (order.paidAmount == 0 || Math.Round(order.paidAmount - order.refundAmount, 2) == 0))
                    {
                        await Fix2025FallItem(order);
                    }
                    else
                    {
                        Console.WriteLine(order.id.ToString());
                    }
                }
                else
                {
                    Console.WriteLine(orders[i].id.ToString() + " duplicate");
                }
            }
            for (int i = 0; i < cares.Count; i++)
            {
                if (depositOrders.Where(o => o.id == cares[i].order_id).ToList().Count == 0)
                {
                    Models.Order order = await _orderHelper.GetOrder((int)cares[i].order_id);
                    depositOrders.Add(order);
                    if (order.availablePayments.Where(p => p.pay_method == "储值支付" && p.status == OrderPayment.PaymentStatus.支付成功.ToString()).ToList().Count == 0
                        && (order.paidAmount == 0 || Math.Round(order.paidAmount - order.refundAmount, 2) == 0))
                    {
                        await Fix2025FallItem(order);
                    }
                    else
                    {
                        Console.WriteLine(order.id.ToString());
                    }
                }
                else
                {
                    Console.WriteLine(cares[i].order_id.ToString() + " duplicate");
                }
            }
            for (int i = 0; i < rentals.Count; i++)
            {
                if (depositOrders.Where(o => o.id == rentals[i].order_id).ToList().Count == 0)
                {
                    Models.Order order = await _orderHelper.GetOrder((int)rentals[i].order_id);
                    depositOrders.Add(order);
                    if (order.availablePayments.Where(p => p.pay_method == "储值支付" && p.status == OrderPayment.PaymentStatus.支付成功.ToString()).ToList().Count == 0
                        && (order.paidAmount == 0 || Math.Round(order.paidAmount - order.refundAmount, 2) == 0))
                    {
                        await Fix2025FallItem(order);
                    }
                    else
                    {
                        Console.WriteLine(order.id.ToString());
                    }
                }
                else
                {
                    Console.WriteLine(rentals[i].order_id.ToString() + " duplicate");
                }
            }
            
        }
        */

    }
}
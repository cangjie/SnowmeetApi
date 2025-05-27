using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
//using Aop.Api.Domain;

//using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NuGet.ProjectModel;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MemberController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        public MemberController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }
        [HttpGet]
        public async Task Test()
        {
            await MergeMember(18363, 1486);
        }

        [NonAction]
        public async Task MergeMember(int sourceId, int targetId)
        {
            int traceId = (DateTime.Now - DateTime.Parse("1970-01-01")).Milliseconds;
            Member sourceMember = await _db.member.FindAsync(sourceId);
            Member targetMember = await _db.member.FindAsync(targetId);
            await _db.member.Entry(sourceMember).Collection(m => m.memberSocialAccounts).LoadAsync();
            await _db.member.Entry(targetMember).Collection(m => m.memberSocialAccounts).LoadAsync();
            sourceMember.is_merge = 1;
            sourceMember.merge_id = targetId;
            string? cell = null;
            for (int i = 0; i < sourceMember.memberSocialAccounts.Count; i++)
            {
                MemberSocialAccount msa = sourceMember.memberSocialAccounts[i];
                msa.valid = 0;
                msa.update_date = DateTime.Now;
                if (msa.type.Trim().Equals("cell"))
                {
                    cell = msa.num.Trim();
                }
                _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
            }
            for (int i = 0; i < targetMember.memberSocialAccounts.Count; i++)
            {
                MemberSocialAccount msa = targetMember.memberSocialAccounts[i];
                msa.valid = 1;
                msa.update_date = DateTime.Now;
                _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
            }

            targetMember.is_merge = 0;
            targetMember.merge_id = null;
            _db.member.Entry(sourceMember).State = EntityState.Modified;
            _db.member.Entry(targetMember).State = EntityState.Modified;
            CoreDataModLog logMember = new CoreDataModLog()
            {
                id = 0,
                trace_id = traceId,
                table_name = "member",
                key_value = sourceId,
                scene = "用户批量合并",
                current_value = targetId.ToString(),
                is_manual = 1,
                manual_memo = "用户批量合并"
            };
            await _db.coreDataModLog.AddAsync(logMember);
            List<Models.Order> orderList = await _db.order.Where(o => o.member_id == sourceId).ToListAsync();
            for (int i = 0; i < orderList.Count; i++)
            {
                Models.Order order = orderList[i];
                order.member_id = targetId;
                order.update_date = DateTime.Now;
                _db.order.Entry(order).State = EntityState.Modified;
                CoreDataModLog logOrder = new CoreDataModLog()
                {
                    id = 0,
                    trace_id = traceId,
                    table_name = "order",
                    key_value = order.id,
                    scene = "用户批量合并",
                    field_name = "member_id",
                    prev_value = sourceId.ToString(),
                    current_value = targetId.ToString(),
                    is_manual = 1,
                    manual_memo = "订单迁移"
                };
                await _db.coreDataModLog.AddAsync(logOrder);
            }
            List<DepositAccount> depositList = await _db.depositAccount.Where(d => d.member_id == sourceId).ToListAsync();
            for (int i = 0; i < depositList.Count; i++)
            {
                DepositAccount deposit = depositList[i];
                deposit.member_id = targetId;
                deposit.update_date = DateTime.Now;
                _db.depositAccount.Entry(deposit).State = EntityState.Modified;
                CoreDataModLog logDeposit = new CoreDataModLog()
                {
                    id = 0,
                    trace_id = traceId,
                    table_name = "deposit_account",
                    key_value = deposit.id,
                    scene = "用户批量合并",
                    field_name = "member_id",
                    prev_value = sourceId.ToString(),
                    current_value = targetId.ToString(),
                    is_manual = 1,
                    manual_memo = "充值账户迁移"
                };
                await _db.coreDataModLog.AddAsync(logDeposit);
            }
            await _db.SaveChangesAsync();
            if (cell != null && targetMember.memberSocialAccounts.Where(m => m.type.Trim().Equals("cell") && m.num.Trim().Equals(cell.Trim())).ToList().Count == 0)
            {
                MemberSocialAccount msaNew = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = targetId,
                    type = "cell",
                    num = cell.Trim(),
                    valid = 1,
                    memo = "批量合并用户时添加的手机号"
                };
                await _db.memberSocialAccount.AddAsync(msaNew);
                await _db.SaveChangesAsync();
            }
        }
        [NonAction]
        public async Task<bool> IsEmpty(int memberId, bool needValid)
        {
            bool isEmpty = true;
            List<Models.Order> orderList = await _db.order.Where(m => m.member_id == memberId && ((needValid && m.valid == 1 ) || !needValid) ).AsNoTracking().ToListAsync();
            List<DepositAccount> depositList = await _db.depositAccount.Where(m => m.member_id == memberId && ((needValid && m.valid == 1 ) || !needValid)  ).AsNoTracking().ToListAsync();
            if (orderList.Count == 0 && depositList.Count == 0)
            {
                isEmpty = true;
            }
            else
            {
                isEmpty = false;
            }
            return isEmpty;
        }
        [NonAction]
        public async Task<Member> GetMemberBySessionKey(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            List<MiniSession> sList = await _db.miniSession
                .Include(m => m.member).ThenInclude(m => m.memberSocialAccounts.Where(m => m.valid == 1))
                .Where(m => m.session_key.Trim().Equals(sessionKey) && m.session_type.Equals(sessionType) && m.valid == 1 && m.expire_date >= DateTime.Now)
                .OrderByDescending(m => m.expire_date).AsNoTracking().ToListAsync();
            for (int i = 0; i < sList.Count; i++)
            {
                MiniSession mSession = sList[i];
                if (mSession.member != null)
                {
                    return mSession.member;
                }
            }
            return null;
        }
        [NonAction]
        public async Task<Member> GetMemberByNum(string num, string type)
        {
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => (m.valid == 1 && m.type.Trim().Equals(type.Trim()) && m.num.Trim().Equals(num.Trim())))
                .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
            if (msaList == null || msaList.Count <= 0)
            {
                return null;
            }
            List<Member> mList = await _db.member.Include(m => m.memberSocialAccounts)
                .Where(m => m.id == msaList[0].member_id).AsNoTracking().ToListAsync();
            if (mList == null || mList.Count <= 0)
            {
                return null;
            }
            return mList[0];
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<Member?>>> GetMemberByNum(string num, string type,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            num = Util.UrlDecode(num);
            type = Util.UrlDecode(type);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Member? member = await GetMemberByNum(num, type);
            if (member == null)
            {
                return Ok(new ApiResult<Member>()
                {
                    code = 1,
                    message = "未找到会员",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<Member>()
                {
                    code = 0,
                    message = "",
                    data = member
                });
            }
        }






        /// <summary>
        /// ///////////////////////will be deleted////////////////////////////////////////////////////////////////////////////
        /// </summary>
        /// <param name="sessionKey"></param>
        /// <param name="sessionType"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<ActionResult<Member>> GetMemberInfoSimple(string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await GetMemberBySessionKey(sessionKey.Trim(), sessionType.Trim());
            //member.id = 0;
            //member.memberSocialAccounts = new List<MemberSocialAccount>();
            return Ok(RemoveSensitiveInfo(member));
        }



        [NonAction]
        public async Task<Member> UpdateDetailInfo(int memberId, string num, string type, bool isUnic)
        {
            Member mTest = await _db.member.FindAsync(memberId);
            if (mTest == null)
            {
                return null;
            }
            //int valid = 0;
            var msaList = await _db.memberSocialAccount.Where(m => m.member_id == memberId).ToListAsync();
            bool haveMod = true;
            for (int i = 0; i < msaList.Count; i++)
            {
                MemberSocialAccount msa = msaList[i];
                if (msa.type.Trim().Equals(type.Trim()) && msa.num.Trim().Equals(num.Trim()) && msa.valid == 1)
                {
                    haveMod = false;
                    break;
                }

            }
            if (!haveMod)
            {
                var ml = await _db.member.Where(m => m.id == memberId).Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
                if (ml == null || ml.Count == 0)
                {
                    return null;
                }
                return ml[0];
            }
            for (int i = 0; i < msaList.Count; i++)
            {
                MemberSocialAccount msa = msaList[i];
                if (isUnic && msa.type.Trim().Equals(type.Trim()))
                {
                    msa.valid = 0;
                    _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
                }
            }
            MemberSocialAccount msaNew = new MemberSocialAccount()
            {
                id = 0,
                member_id = memberId,
                type = type.Trim(),
                num = num.Trim(),
                valid = 1
            };
            await _db.memberSocialAccount.AddAsync(msaNew);
            await _db.SaveChangesAsync();
            var mlNew = await _db.member.Where(m => m.id == memberId).Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
            if (mlNew == null || mlNew.Count == 0)
            {
                return null;
            }
            return mlNew[0];



        }

        [NonAction]
        public async Task<Member> GetMember(string num, string type = "")
        {

            type = type.Trim();
            int memberId = 0;

            var msaList = await _db.memberSocialAccount
                        .Where(a => (a.valid == 1 && a.num.Trim().Equals(num) && a.type.Trim().Equals(type)))
                        .OrderByDescending(a => a.member_id).ToListAsync();
            if (msaList.Count == 0)
            {
                return null;
            }
            memberId = msaList[0].member_id;
            if (memberId == 0)
            {
                return null;
            }
            Member member = await _db.member.Include(m => m.memberSocialAccounts)
                .Where(m => m.id == memberId).FirstAsync();
            return member;
        }

        [NonAction]
        public async Task<Member> CreateMember(Member member)
        {
            if (member.memberSocialAccounts.Count == 0)
            {
                return null;
            }
            await _db.member.AddAsync(member);
            await _db.SaveChangesAsync();
            return member;
        }


        private bool MemberExists(int id)
        {
            return _db.member.Any(e => e.id == id);
        }

        [HttpGet]
        public async Task<ActionResult<Member>> RegStaff(string name, string gender, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            name = Util.UrlDecode(name);
            gender = Util.UrlDecode(gender);

            Member member = await GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            member = await _db.member.FindAsync(member.id);
            member.real_name = name;
            member.gender = gender;
            member.in_staff_list = 1;
            _db.member.Entry(member).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return RemoveSensitiveInfo(member);
        }

        /*

        [HttpGet("{memberId}")]
        public async Task<ActionResult> UpdateUserCell(int memberId, string encData, string iv, 
            string sessionKey, string sessionType = "wechat_mini_openid" )
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            encData = Util.UrlDecode(encData);
            iv = Util.UrlDecode(iv);
            string cell = "";
            string json = Util.AES_decrypt(encData.Trim(), sessionKey, iv);
            Newtonsoft.Json.Linq.JToken jsonObj = (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            if (jsonObj["phoneNumber"] != null)
            {
                cell = jsonObj["phoneNumber"].ToString().Trim();
            }
            if (cell.Trim().Equals(""))
            {
                return BadRequest();
            }
            return (await SetMemberInfo(memberId, "cell", cell.Trim(), sessionKey, sessionType));
        }
        */

        [HttpGet("{memberId}")]
        public async Task<ActionResult> SetMemberInfo(int memberId, string type, string num,
            string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            type = Util.UrlDecode(type);
            num = Util.UrlDecode(num);
            Member member = await GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            if (member.is_staff == 0 || member.id != memberId)
            {
                return BadRequest();
            }
            var list = await _db.memberSocialAccount.Where(m => (m.id == memberId && m.type.Trim().Equals(type.Trim())
                && m.num.Trim().Equals(num))).ToListAsync();
            if (list == null || list.Count == 0)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = memberId,
                    type = type,
                    num = num,
                    valid = 1
                };
                await _db.memberSocialAccount.AddAsync(msa);
                await _db.SaveChangesAsync();
            }
            else
            {
                MemberSocialAccount msa = list[0];
                if (msa.valid == 0)
                {
                    msa.valid = 1;
                    _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }

            }
            return Ok();
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult> SetStaffInfo(int memberId,
            string name, string gender, string cell,
            int isAdmin, int isManager, int isStaff, int inStaffList,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0)
            {
                return BadRequest();
            }
            Member staff = await _db.member.FindAsync(memberId);
            if (staff == null)
            {
                return NotFound();
            }
            staff.is_admin = isAdmin;
            staff.is_staff = isStaff;
            staff.is_manager = isManager;
            staff.in_staff_list = inStaffList;
            if (name != null)
                staff.real_name = name.Trim();
            if (gender != null)
                staff.gender = gender.Trim();
            if (cell != null)
                await ModMemberCell(memberId, cell.Trim());


            _db.member.Entry(staff).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult<Member>> GetWholeMemberInfo(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0 && admin.is_staff == 0 && admin.is_manager == 0)
            {
                return BadRequest();
            }
            var memberList = await _db.member.Where(m => m.id == memberId)
                .Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
            if (memberList == null || memberList.Count == 0)
            {
                return NotFound();
            }
            //memberList = GetCells(memberList);
            return Ok(memberList[0]);
        }

        [HttpGet]
        public async Task<ActionResult<List<Member>>> GetStaffList(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0)
            {
                return BadRequest();
            }
            var memberList = await _db.member.Where(m => (m.in_staff_list == 1))
                .OrderBy(m => (m.is_admin + m.is_manager + m.is_staff))
                .Include(m => m.memberSocialAccounts).AsNoTracking().ToListAsync();
            //memberList = GetCells(memberList);
            return Ok(memberList);

        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<Member>> GetMemberByCell(string cell,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member admin = await GetMemberBySessionKey(sessionKey, sessionType);
            if (admin.is_admin == 0 && admin.is_manager == 0 && admin.is_staff == 0)
            {
                return BadRequest();
            }
            return Ok(await GetMember(cell, "cell"));
        }

        /*
                [NonAction]
                public List<Member> GetCells(List<Member> memberList)
                {
                    for(int i = 0; i < memberList.Count; i++)
                    {
                        Member member = memberList[i];
                        foreach(MemberSocialAccount msa in member.memberSocialAccounts)
                        {
                            if (msa.type.Trim().Equals("cell"))
                            {
                                member.cell = msa.num.Trim();
                                break;
                            }
                        }
                    }
                    return memberList;
                }
        */
        [NonAction]
        public async Task ModMemberCell(int memberId, string cell)
        {
            var list = await _db.memberSocialAccount
                .Where(m => (m.type.Trim().Equals("cell") && m.num.Trim().Equals(cell.Trim())
                && m.member_id == memberId))
                .ToListAsync();
            if (list == null || list.Count == 0)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = memberId,
                    type = "cell",
                    num = cell.Trim(),
                    valid = 1
                };
                await _db.memberSocialAccount.AddAsync(msa);
                await _db.SaveChangesAsync();
            }
            else
            {
                MemberSocialAccount msa = list[0];
                msa.valid = 1;
                _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }

        }


        [NonAction]
        public Member RemoveSensitiveInfo(Member member)
        {
            if (member == null)
            {
                return member;
            }
            member.id = 0;
            IList<MemberSocialAccount> msaList = member.memberSocialAccounts.ToList();

            for (int i = 0; i < msaList.Count; i++)
            {
                MemberSocialAccount msa = msaList[i];
                msa.member_id = 0;
                if (msa.type.Trim().IndexOf("openid") >= 0)
                {
                    msaList.Remove(msa);
                    i--;
                }
                if (msa.type.Trim().IndexOf("unionid") >= 0)
                {
                    msaList.Remove(msa);
                    i--;
                }
            }
            member.memberSocialAccounts = msaList.ToList();
            return member;
        }

        [NonAction]
        public async Task<bool> isStaff(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            bool ret = false;
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin == 1 || member.is_manager == 1 || member.is_staff == 1)
            {
                ret = true;
            }
            return ret;
        }

        [NonAction]
        public async Task<List<Member>> SearchMember(string key)
        {
            List<Member> mList = await _db.member.Where(m => (m.real_name.IndexOf(key) >= 0))
                .Include(m => m.memberSocialAccounts.Where(msa => msa.valid == 1)).AsNoTracking().ToListAsync();

            List<MemberSocialAccount> cellList = await _db.memberSocialAccount
                .Where(msa => (msa.valid == 1 && msa.num.EndsWith(key) && key.Length >= 4 && msa.type.Trim().Equals("cell")))
                .Include(msa => msa.member).AsNoTracking().ToListAsync();


            List<Member> ret = new List<Member>();
            for (int i = 0; i < cellList.Count; i++)
            {
                Member member = cellList[i].member;
                if (mList.Where(m => m.id == member.id).ToList().Count == 0)
                {
                    mList.Add(member);
                }
            }
            return mList;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        [NonAction]
        public async Task<Member> GetWholeMemberById(int memberId)
        {
            List<Member> memberList = await _db.member
                .Where(m => m.id == memberId)
                .Include(m => m.memberSocialAccounts.Where(msa => msa.valid == 1))
                .Include(m => m.depositAccounts.Where(d => d.valid == 1))
                .Include(m => m.points.Where(p => p.valid == 1))
                .Include(m => m.tickets)
                .AsSplitQuery().AsNoTracking().ToListAsync();
            if (memberList == null || memberList.Count == 0)
            {
                return null;
            }
            else
            {
                return memberList[0];
            }
        }
        
        [NonAction]
        public async Task<Member> GetWholeMemberByNum(string num, string type)
        {
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => (m.valid == 1 && m.type.Trim().Equals(type.Trim()) && m.num.Trim().Equals(num.Trim())))
                .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
            if (msaList == null || msaList.Count <= 0)
            {
                return null;
            }
            else
            {
                int memberId = msaList[0].member_id;
                return await GetWholeMemberById(memberId);
            }
        }
        [NonAction]
        public async Task<Member> GetMemberBySessionKey(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            List<MiniSession> sList = await _db.miniSession
                .Where(m => m.session_key.Trim().Equals(sessionKey) && m.session_type.Equals(sessionType) && m.valid == 1 && m.expire_date >= DateTime.Now)
                .OrderByDescending(m => m.expire_date).AsNoTracking().ToListAsync();
            if (sList == null || sList.Count == 0)
            {
                return null;
            }
            else
            {
                if (sList[0].member_id != null)
                {
                    return await GetWholeMemberById((int)sList[0].member_id);
                }
                else
                {
                    return null;
                }
            }
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
            List<Models.Order> orderList = await _db.order.Where(m => m.member_id == memberId && ((needValid && m.valid == 1) || !needValid)).AsNoTracking().ToListAsync();
            List<DepositAccount> depositList = await _db.depositAccount.Where(m => m.member_id == memberId && ((needValid && m.valid == 1) || !needValid)).AsNoTracking().ToListAsync();
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
        public async Task<bool> UnbindMemberMainCellNum(int memberId, string num, string scene, Staff? staff)
        {
            List<MemberSocialAccount> list = await _db.memberSocialAccount
                .Where(m => m.type.Trim().Equals("cell") && m.member_id == memberId && m.valid == 1 && m.num.Trim().Equals(num.Trim()))
                .ToListAsync();
            if (list == null || list.Count <= 0)
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                list[i].valid = 0;
                list[i].update_date = DateTime.Now;
                _db.memberSocialAccount.Entry(list[i]).State = EntityState.Modified;
            }
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                trace_id = 0,
                table_name = "member",
                key_value = memberId,
                scene = scene.Trim(),
                field_name = "num",
                prev_value = num,
                current_value = null,
                is_manual = 1,
                manual_memo = "手机号解绑",
                staff_id = staff == null ? null : staff.id,
                member_id = staff != null ? memberId : null
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return true;
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<Member>>> ChangeCellNumByStaff(int memberId, string num,
            string sessionKey, string sessionType = "wechat_mini_openid", string scene = "")
        {
            scene = Util.UrlDecode(scene);
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
            List<MemberSocialAccount> validNumList = await _db.memberSocialAccount
                .Where(m => m.num.Trim().Equals(num.Trim()) && m.type.Trim().Equals("cell") && m.valid == 1 && m.member_id != memberId)
                .AsNoTracking().ToListAsync();
            if (validNumList != null && validNumList.Count > 0)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "手机号已被他人绑定",
                    data = null
                });
            }
            Member member = await _db.member.Where(m => m.id == memberId)
                .Include(m => m.memberSocialAccounts).AsNoTracking().FirstAsync();
            if (member.cell != null && member.cell.Trim().Equals(num.Trim()))
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "手机号没有改变",
                    data = null
                });
            }
            if (member.cell != null)
            {
                await UnbindMemberMainCellNum(memberId, member.cell, scene, staff);
            }
            MemberSocialAccount msa = await BindMemberMainCellNum(memberId, num, scene, staff);
            if (msa == null)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "绑定失败",
                    data = null
                });
            }
            member = await _db.member.Where(m => m.id == memberId)
                .Include(m => m.memberSocialAccounts).AsNoTracking().FirstAsync();
            return Ok(new ApiResult<Member?>()
            {
                code = 0,
                message = "",
                data = member
            });

        }
        [NonAction]
        public async Task<MemberSocialAccount?> BindMemberMainCellNum(int memberId, string num, string scene, Staff? staff)
        {
            List<MemberSocialAccount> oriList = await _db.memberSocialAccount
                .Where(m => m.member_id != memberId && m.valid == 1 && m.num.Trim().Equals(num.Trim()) && m.type.Trim().Equals("cell"))
                .AsNoTracking().ToListAsync();
            if (oriList != null && oriList.Count > 0)
            {
                return null;
            }
            bool find = false;
            string? oriNum = null;
            List<MemberSocialAccount> currentList = await _db.memberSocialAccount
                .Where(m => m.type.Trim().Equals("cell") && m.member_id == memberId)
                .ToListAsync();
            for (int i = 0; i < currentList.Count; i++)
            {
                MemberSocialAccount msa = currentList[i];
                if (msa.valid == 1)
                {
                    oriNum = msa.num.Trim();
                }
                if (msa.num.Trim().Equals(num.Trim()))
                {
                    find = true;
                    msa.valid = 1;
                    msa.update_date = DateTime.Now;
                }
                else
                {
                    msa.valid = 0;
                    msa.update_date = DateTime.Now;
                }
                _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
            }
            if (!find)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = memberId,
                    type = "cell",
                    num = num.Trim()
                };
                await _db.memberSocialAccount.AddAsync(msa);
            }
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                trace_id = 0,
                table_name = "member",
                key_value = memberId,
                scene = scene.Trim(),
                field_name = "num",
                prev_value = null,
                current_value = num.Trim(),
                is_manual = 1,
                manual_memo = "绑定手机号",
                staff_id = staff == null ? null : staff.id,
                member_id = staff != null ? memberId : null
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return await _db.memberSocialAccount
                .Where(m => m.member_id == memberId && m.valid == 1 && m.type.Trim().Equals("cell"))
                .AsNoTracking().FirstAsync();
        }
        [NonAction]
        public async Task<MemberSocialAccount?> UpdateUniqueTypeMemberSocialAccount(int memberId, string num, string type, string scene, Staff? staff)
        {
            List<MemberSocialAccount> otherMsaList = await _db.memberSocialAccount
                .Where(m => (m.valid == 1 && m.num.Trim().Equals(num.Trim()) && m.type.Trim().Equals(type.Trim()) && m.member_id != memberId))
                .AsNoTracking().ToListAsync();
            if (otherMsaList != null && otherMsaList.Count > 0)
            {
                return null;
            }
            string? oriNum = null;
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => (m.member_id == memberId && m.type.Trim().Equals(type.Trim())))
                .ToListAsync();
            MemberSocialAccount ret = null;
            if (msaList == null || msaList.Count == 0)
            {
                return null;
            }
            bool found = false;
            for (int i = 0; i < msaList.Count; i++)
            {
                MemberSocialAccount msa = msaList[i];
                if (msa.valid == 1)
                {
                    oriNum = msa.num.Trim();
                }
                if (msa.num.Trim().Equals(num.Trim()))
                {
                    found = true;
                    msa.valid = 1;
                    ret = msa;
                }
                else
                {
                    msa.valid = 0;
                }
                msa.update_date = DateTime.Now;
                _db.memberSocialAccount.Entry(msa).State = EntityState.Modified;
            }
            if (!found)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = memberId,
                    type = type.Trim(),
                    num = num.Trim(),
                    valid = 1
                };
                await _db.memberSocialAccount.AddAsync(msa);
                ret = msa;
            }
            string memo = "";
            switch (type)
            {
                case "cell":
                    memo = "修改会员手机号";
                    break;
                default:
                    break;
            }
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                trace_id = 0,
                table_name = "member",
                key_value = memberId, // This will be set later
                scene = scene.Trim(),
                field_name = "num",
                prev_value = oriNum,
                current_value = num.Trim(),
                is_manual = 1,
                manual_memo = memo,
                staff_id = staff == null ? null : staff.id,
                member_id = staff != null ? memberId : null
            };
            await _db.coreDataModLog.AddAsync(log);
            try
            {
                await _db.SaveChangesAsync();
                return ret;
            }
            catch
            {
                return null;
            }
        }

        [NonAction]
        public async Task<Member> UpdateMemberInfo(Member member, Staff? staff, string scene)
        {
            Member? oriMember = await GetWholeMemberById(member.id);
            if (oriMember == null)
            {
                return null;
            }
            /*
            if (!member._cell.Trim().Equals(oriMember.cell.Trim()))
            {
                MemberSocialAccount? msa = await UpdateUniqueTypeMemberSocialAccount(member.id, member._cell.Trim(), "cell", scene, staff);
                if (msa == null)
                {
                    return null;
                }
            }
            */
            if (member.currentContactNum != null && member.memberSocialAccounts.Where(m => (m.type.Equals("cell") || m.type.Equals("contact"))
                && m.valid == 1 && m.num.Trim().Equals(member.currentContactNum)).ToList().Count <= 0)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = member.id,
                    type = "contact",
                    num = member.currentContactNum.Trim(),
                    valid = 1,
                    create_date = DateTime.Now
                };
                await _db.memberSocialAccount.AddAsync(msa);
                await _db.SaveChangesAsync();
            }
            Member newMember = await _db.member.FindAsync(member.id);
            if (!member.real_name.Trim().Equals(oriMember.real_name.Trim()))
            {
                newMember.real_name = member.real_name.Trim();
                CoreDataModLog log = new CoreDataModLog()
                {
                    id = 0,
                    trace_id = 0,
                    table_name = "member",
                    key_value = member.id,
                    scene = scene.Trim(),
                    field_name = "real_name",
                    prev_value = oriMember.real_name.Trim(),
                    current_value = member.real_name.Trim(),
                    is_manual = 1,
                    manual_memo = "修改会员姓名",
                    staff_id = staff == null ? null : staff.id,
                    member_id = staff != null ? member.id : null
                };
                await _db.coreDataModLog.AddAsync(log);
            }
            if (!member.gender.Trim().Equals(oriMember.gender.Trim()))
            {
                newMember.gender = member.gender;
                CoreDataModLog log = new CoreDataModLog()
                {
                    id = 0,
                    trace_id = 0,
                    table_name = "member",
                    key_value = member.id,
                    scene = scene.Trim(),
                    field_name = "real_name",
                    prev_value = oriMember.gender.Trim(),
                    current_value = member.gender.Trim(),
                    is_manual = 1,
                    manual_memo = "修改会员性别",
                    staff_id = staff == null ? null : staff.id,
                    member_id = staff != null ? member.id : null
                };
                await _db.coreDataModLog.AddAsync(log);
            }
            newMember.update_date = DateTime.Now;
            _db.member.Entry(newMember).State = EntityState.Modified;
            try
            {
                await _db.SaveChangesAsync();
                return await GetWholeMemberById(member.id);
            }
            catch
            {
                return null;
            }
        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<ApiResult<Member?>>> GetMemberByNum(string cell,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            cell = Util.UrlDecode(cell);

            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }

            Member? member = await GetWholeMemberByNum(cell, "cell");
            if (member == null)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "会员不存在",
                    data = null
                });
            }

            return Ok(new ApiResult<Member?>()
            {
                code = 0,
                message = "",
                data = member
            });
        }

        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<Member?>>> GetMember(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
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
            Member? member = await GetWholeMemberById(memberId);
            if (member == null)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "会员不存在",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 0,
                    message = "",
                    data = member
                });
            }
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Member?>>> UpdateMemberInfo([FromBody] Member member, string scene,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            scene = Util.UrlDecode(scene);
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            MiniSession? session = null;
            List<MiniSession> mSeesions = await _db.miniSession
                .Where(m => (m.session_key.Trim().Equals(sessionKey) && m.session_type.Equals(sessionType) && m.valid == 1 && m.expire_date >= DateTime.Now))
                .OrderByDescending(m => m.expire_date).AsNoTracking().ToListAsync();
            if (mSeesions != null && mSeesions.Count != 0)
            {
                session = mSeesions[0];
            }
            if (session.member_id != member.id)
            {
                if (staff == null || staff.title_level < 100)
                {
                    return Ok(new ApiResult<Member?>()
                    {
                        code = 1,
                        message = "没有权限",
                        data = null
                    });
                }
            }
            Member? memberNew = await UpdateMemberInfo(member, staff, scene);
            if (memberNew == null)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "更新失败",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 0,
                    message = "",
                    data = memberNew
                });
            }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<bool>>> VerifyCell(string sessionKey, string encData, string iv)
        {
            encData = Util.UrlDecode(encData);
            iv = Util.UrlDecode(iv);
            string json = Util.AES_decrypt(encData.Trim(), sessionKey, iv);
            Newtonsoft.Json.Linq.JToken jsonObj = (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            string cell = "";
            string unionId = "";
            try
            {
                if (jsonObj["phoneNumber"] != null)
                {
                    cell = jsonObj["phoneNumber"].ToString().Trim();
                }
            }
            catch
            {
                return Ok(new ApiResult<bool>()
                {
                    code = 1,
                    message = "验证失败",
                    data = false
                });
            }
            try
            {
                if (jsonObj["unionId"] != null && jsonObj["unionId"].ToString().Trim().Equals(""))
                {
                    unionId = jsonObj["unionId"].ToString().Trim();
                }
            }
            catch
            {

            }
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey);
            MemberSocialAccount msa = await _db.memberSocialAccount
                .Where(m => m.valid == 1 && m.type.Trim().Equals("cell") && m.num.Trim().Equals(cell.Trim()))
                .AsNoTracking().FirstOrDefaultAsync();
            if (msa != null)
            {
                if (msa.member_id == member.id)
                {
                    return Ok(new ApiResult<bool>()
                    {
                        code = 1,
                        message = "手机号曾经验证过",
                        data = true

                    });
                }
                else
                {
                    return Ok(new ApiResult<bool>()
                    {
                        code = 1,
                        message = "手机号已被占用",
                        data = false

                    });
                }
            }
            else
            {
                CoreDataModLog log = new CoreDataModLog()
                {
                    table_name = "member",
                    key_value = member.id,
                    scene = "会员注册验证手机号",
                    field_name = "cell",
                    prev_value = null,
                    current_value = cell.Trim(),
                    member_id = member.id,
                    staff_id = null,
                    create_date = DateTime.Now
                };
                MemberSocialAccount msaNew = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = member.id,
                    type = "cell",
                    valid = 1,
                    num = cell,
                    create_date = DateTime.Now
                };
                await _db.memberSocialAccount.AddAsync(msaNew);
                await _db.coreDataModLog.AddAsync(log);
                await _db.SaveChangesAsync();
                return Ok(new ApiResult<bool>()
                {
                    code = 0,
                    message = "",
                    data = true
                });
            }
        }
        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<object?>>> StopQueryMemberBindCell(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            MemberSocialAccount msa = new MemberSocialAccount()
            {
                id = 0,
                member_id = memberId,
                type = "cell",
                num = "",
                valid = 1,
                memo = "临时",
                create_date = DateTime.Now
            };
            await _db.memberSocialAccount.AddAsync(msa);
            await _db.SaveChangesAsync();
            Thread.Sleep(2500);
            _db.memberSocialAccount.Remove(msa);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object?>()
            {
                code = 0,
                message = "",
                data = null
            });
        }
        [NonAction]
        public async Task<Member> QueryMemberBindCell(int memberId)
        {
            var contextOptions = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlServer(Util.GetSqlServerConnectionString()).Options;
            var db = new ApplicationDBContext(contextOptions);


            Member member = await GetWholeMemberById(memberId);
            if (member != null && member.cell != null)
            {
                return member;
            }
            int times = 0;
            bool find = false;
            for (; times <= 900 && !find; times++)
            {
                System.Threading.Thread.Sleep(1000);
                try
                {
                    MemberSocialAccount msa = await db.memberSocialAccount
                        .Where(m => m.member_id == member.id && m.type.Trim().Equals("cell") && m.valid == 1)
                        .AsNoTracking().FirstOrDefaultAsync();
                    if (msa != null)
                    {
                        find = true;
                    }
                }
                catch
                {

                }
            }
            if (find)
            {
                return await GetWholeMemberById(memberId);
            }
            else
            {
                return null;
            }
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<Member?>>> GetMyInfo(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Member member = await GetMemberBySessionKey(sessionKey.Trim());
            if (member == null)
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 1,
                    message = "未找到会员",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<Member?>()
                {
                    code = 0,
                    message = "",
                    data = member
                });
            }
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

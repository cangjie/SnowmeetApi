using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Models.Deposit;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MiniAppUserController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        public string _appId = "";

        public MemberController _memberHelper;


        public MiniAppUserController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _memberHelper = new MemberController(context, config);
            //UnicUser._context = context;
        }

        [HttpGet]
        public async Task<ActionResult<string>> GetNewStaffName(string sessionKey)
        {
            string name = "";
            sessionKey = Util.UrlDecode(sessionKey);
            //(MiniAppUser)((await GetMiniUserOld(sessionKey)).Value)
            //MiniAppUser miniUser = ((List<MiniAppUser>)(await GetMiniUserOld(sessionKey)).Value)[0];
            MiniAppUser miniUser = await GetMiniAppUser(sessionKey);
            if (miniUser == null)
            {
                return BadRequest();
            }
            string unionId = miniUser.union_id.Trim();
            var oaUserList = await _context.officialAccoutUsers.Where(o => o.union_id.Trim().Equals(unionId.Trim()))
                .AsNoTracking().ToListAsync();
            if (oaUserList == null || oaUserList.Count == 0)
            {
                return BadRequest();
            }
            var sendMsgList = await _context.oAReceive.Where(r => r.FromUserName.Trim().Equals(oaUserList[0].open_id))
                .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
            for (int i = 0; sendMsgList != null && i < sendMsgList.Count; i++)
            {
                string msg = sendMsgList[i].Content.Trim();
                if (msg.StartsWith("我要入职"))
                {
                    name = msg.Replace("我要入职", "").Trim();
                    if (!name.Trim().Equals(""))
                        break;
                }
            }
            return Ok(name);
        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<int>> StaffCheckIn(string cell, string name, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            MiniAppUser miniUser = await GetMiniAppUser(sessionKey);
            name = Util.UrlDecode(name);
            miniUser.real_name = name;
            miniUser.cell_number = cell;
            miniUser.is_admin = 1;
            _context.MiniAppUsers.Entry(miniUser).State = EntityState.Modified;
            int i = await _context.SaveChangesAsync();
            return Ok(i);
        }

        [NonAction]
        public async Task<MiniAppUser> GetMiniAppUser(string sessionKey)
        {
            /*
            var sList = await _context.MiniSessons.Where(s => s.session_key.Trim().Equals(sessionKey))
                .OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            if (sList == null || sList.Count <= 0)
            {
                return null;
            }
            return await _context.MiniAppUsers.FindAsync(sList[0].open_id);
            */
            return (await _memberHelper.GetMemberBySessionKey(sessionKey)).miniAppUser;
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> SetStaff(string openId, bool isStaff, string sessionKey)
        {
            openId = Util.UrlDecode(openId);
            sessionKey = Util.UrlDecode(sessionKey);
            MiniAppUser managerUser = (MiniAppUser)((OkObjectResult)(await GetMiniUser(sessionKey)).Result).Value;
            if (managerUser.is_manager != 1)
            {
                return NotFound();
            }
            //MiniAppUser user = await _context.MiniAppUsers.FindAsync(openId);
            MiniAppUser user = (await _memberHelper.GetWholeMemberByNum(openId, "wechat_mini_openid")).miniAppUser;
            if (isStaff)
            {
                user.is_admin = 1;
            }
            else
            {
                user.is_admin = 0;
            }
            _context.MiniAppUsers.Entry(user).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(user);


        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MiniAppUser>>> GetStaffList(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            return await _context.MiniAppUsers.Where(u => u.is_admin == 1).ToListAsync();
        }
        
        [NonAction]
        public async Task<ActionResult<string>> GetOpenIdByCell(string cell)
        {
            string openId = "";
            var uArr = await _context.MiniAppUsers.Where(u => u.cell_number.Trim().Equals(cell.Trim()))
                .OrderByDescending(u => u.create_date).ToListAsync();
            if (uArr != null && uArr.Count > 0)
            {
                openId = uArr[0].open_id.Trim();
            }
            return openId;
        }
        
        [HttpGet("{cell}")]
        public async Task<ActionResult<MiniAppUser>> GetUserByCell(string cell, string staffSessionKey)
        {
            if (!(await Util.IsAdmin(staffSessionKey, _context)))
            {
                return NoContent();
            }
            //Member member = (Member)((OkObjectResult)(await _memberHelper.GetMemberByCell(cell, staffSessionKey)).Result).Value;
            Member? member = await _memberHelper.GetWholeMemberByNum(cell.Trim(), "cell");
            if (member == null)
            {
                return NotFound();
            }
            return Ok(await GetMiniAppUser(member.wechatMiniOpenId.Trim()));

        }
        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> GetMiniAppUser(string openId, string sessionKey)
        {
            openId = Util.UrlDecode(openId.Trim());
            sessionKey = Util.UrlDecode(sessionKey);
            
            //UnicUser._context = _context;
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }

           
            Member? member = await _memberHelper.GetWholeMemberByNum(openId, "wechat_mini_openid");
            MiniAppUser mUser = new MiniAppUser();
            mUser.open_id = openId;
            mUser.union_id = member.wechatUnionId == null? "": member.wechatUnionId.Trim();
            mUser.cell_number = member.cell == null ? "" : member.cell.Trim();
            mUser.real_name = member.real_name.Trim();
            mUser.nick = "";
            mUser.head_image = "";
            mUser.gender = member.gender.Trim();
            mUser.blocked = 0;
            mUser.is_admin = member.is_staff;
            mUser.is_manager = member.is_manager;
            mUser.isMember = !mUser.cell_number.Trim().Equals("");
            mUser.wechat_id = (member.wechatId == null)? "" : member.wechatId.Trim();
            DepositController _depositHelper = new DepositController(_context, _config);
            List<DepositAccount> aList = await _depositHelper.GetMemberAccountAvaliable(member.id, "服务储值", "");
            if (aList.Count > 0)
            {
                DepositAccount account = aList[0];
                account.member = null;
                mUser.serviceDepositAccount = aList[0];
            }
            return Ok(mUser);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<MiniAppUser>> GetMiniUserByTicket(string code, string sessionKey)
        {
            var ticket = await _context.ticket.FindAsync(code.Trim());
            if (ticket != null)
            {
                string openId = ticket.open_id.Trim();
                if (!openId.Trim().Equals(""))
                    return await GetMiniAppUser(openId, sessionKey);
                else
                    return NotFound();
            }
            return NotFound();
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUserList>> GetMiniUserOld(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());

            var mSessionList = await _context.miniSession.Where(m => (m.session_key.Trim().Equals(sessionKey.Trim()))).ToListAsync();
            if (mSessionList.Count == 0)
            {
                return NotFound();
            }
            //MiniAppUser user = await _context.MiniAppUsers.FindAsync(mSessionList[0].open_id);
            MiniAppUser user = (await _memberHelper.GetWholeMemberByNum("", "wechat_mini_openid")).miniAppUser;
            user.open_id = "";
            if (user != null)
            {
                MiniAppUserList l = new MiniAppUserList()
                {
                    mini_users = new MiniAppUser[] { user }
                };
                return l;
            }
            else
            {
                return NotFound();
            }
            
        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUserList>> GetMiniUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());

            var mSessionList = await _context.miniSession.Where(m => (m.session_key.Trim().Equals(sessionKey.Trim()))).ToListAsync();
            if (mSessionList.Count == 0)
            {
                return NotFound();
            }
            //MiniAppUser user = await _context.MiniAppUsers.FindAsync(mSessionList[0].open_id);
            MiniAppUser user = (await _memberHelper.GetWholeMemberByNum("", "")).miniAppUser;
            return Ok(user);

        }

        [HttpPost]
        public async Task<ActionResult<MiniAppUser>> UpdateMiniUser([FromQuery] string sessionKey, [FromBody] MiniAppUser miniUser)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin && !miniUser.open_id.Trim().Equals(""))
            {
                return BadRequest();
            }
            string openId = miniUser.open_id.Trim();
            if (openId.Equals(""))
            {
                openId = user.miniAppOpenId.Trim();
            }
            Member? member = await _memberHelper.GetWholeMemberByNum(openId.Trim(), "wechat_mini_openid");
            member.real_name = miniUser.real_name.Trim();
            member.gender = miniUser.gender.Trim();
            _context.member.Entry(member).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            //await _memberHelper.UpdateDetailInfo(member.id, miniUser.cell_number.Trim(), "cell", true);
            //await _memberHelper.UpdateDetailInfo(member.id, miniUser.wechat_id.Trim(), "wechat_id", false);

            MiniAppUser mUser = (MiniAppUser)((OkObjectResult)(await GetMiniAppUser(member.wechatMiniOpenId, sessionKey)).Result).Value;
            return Ok(mUser);
        }

        // 会话还没有关联会员时（新顾客第一次授权手机号），按手机号定位或新建会员，并把 openid/unionid
        // 链到该会员、回填 mini_session.member_id。语义与 PaymentIdentityController._submitPhone
        // 的「扫码方未绑会员」分支一致：手机号已属于某会员 → 把当前 openid 链过去（一人多设备共享会员）；
        // 手机号没人用过 → 建新会员。
        [NonAction]
        public async Task<Member> ResolveOrCreateMemberByCell(string sessionKey, string cell)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                return null;
            }
            cell = cell.Trim();
            MiniSession sess = await _context.miniSession
                .Where(s => s.session_key.Trim().Equals(sessionKey)
                    && s.session_type.Equals("wechat_mini_openid")
                    && s.valid == 1 && s.expire_date >= DateTime.Now)
                .OrderByDescending(s => s.expire_date).FirstOrDefaultAsync();
            if (sess == null)
            {
                return null;
            }
            string openId = (sess.wechat_openid ?? "").Trim();
            string unionId = (sess.wechat_unionid ?? "").Trim();

            Member member = await _memberHelper.GetWholeMemberByNum(cell, MemberSocialAccount.TYPE_CELL);
            if (member == null)
            {
                // 全新顾客：建会员。valid 显式置 1，不依赖模型默认值
                //（某些 EF/DB schema 组合下默认值会落库成 0，会员随即"查不到"）
                member = new Member()
                {
                    real_name = "",
                    gender = "",
                    source = "小程序手机号验证",
                    valid = 1
                };
                await _context.member.AddAsync(member);
                await _context.SaveChangesAsync();
                await AddMsaIfMissing(member.id, cell, MemberSocialAccount.TYPE_CELL);
            }
            // 把当前微信身份链到该会员（已存在则不重复加）
            if (!string.IsNullOrEmpty(openId))
            {
                await AddMsaIfMissing(member.id, openId, MemberSocialAccount.TYPE_WECHAT_MINI_OPENID);
            }
            if (!string.IsNullOrEmpty(unionId))
            {
                await AddMsaIfMissing(member.id, unionId, MemberSocialAccount.TYPE_WECHAT_UNIONID);
            }
            // 回填会话归属，下次 GetMemberBySessionKey 就能直接命中，不用再走这条兜底
            if (sess.member_id == null)
            {
                sess.member_id = member.id;
                _context.miniSession.Entry(sess).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return await _memberHelper.GetWholeMemberById(member.id);
        }

        [NonAction]
        public async Task AddMsaIfMissing(int memberId, string num, string type)
        {
            bool exists = await _context.memberSocialAccount.AnyAsync(m => m.valid == 1
                && m.member_id == memberId && m.type.Trim().Equals(type) && m.num.Trim().Equals(num));
            if (exists)
            {
                return;
            }
            await _context.memberSocialAccount.AddAsync(new MemberSocialAccount()
            {
                id = 0,
                member_id = memberId,
                type = type,
                num = num,
                valid = 1   // 显式置 1：模型默认值在部分环境下会落库成 0，导致绑了等于没绑
            });
            await _context.SaveChangesAsync();
        }

        [HttpGet]
        public async Task<ActionResult<Member>> UpdateWechatMemberCell(string sessionKey, string encData, string iv)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            encData = Util.UrlDecode(encData);
            iv = Util.UrlDecode(iv);   
            string json = Util.AES_decrypt(encData.Trim(), sessionKey, iv);
            Newtonsoft.Json.Linq.JToken jsonObj = (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            string cell = "";
            //string gender = "";
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

            }

            try
            {
                if (jsonObj["gender"] != null)
                {
                    if (jsonObj["gender"].ToString().Equals("0"))
                    {
                        //gender = "男";
                    }
                    else
                    {
                        //gender = "女";
                    }
                }
            }
            catch
            {

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

            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, "wechat_mini_openid");

            // 2026-05-29 起 MemberLogin 不再自动建 stub 会员：没注册过的用户 mini_session.member_id 是 null，
            // GetMemberBySessionKey 直接返回 null。本方法后面要用 member.id 拼 LINQ 条件，
            // member 为 null 会在「求值查询参数」阶段抛 NRE（500）。
            // 而"刚授权手机号的新顾客"恰恰就是这种人，所以这里必须能按手机号找会员 / 建会员。
            if (member == null)
            {
                member = await ResolveOrCreateMemberByCell(sessionKey, cell);
            }
            if (member == null)
            {
                // 手机号没解出来（解密失败/授权被拒），无从定位会员，明确报错好过继续往下 NRE
                return NotFound();
            }

            var cellList = await _context.memberSocialAccount
                .Where(m => (m.type.Trim().Equals("cell") && m.num.Trim().Equals(cell.Trim()) && m.member_id == member.id))
                .AsNoTracking().ToListAsync();
            if (cellList == null || cellList.Count == 0)
            {
                MemberSocialAccount msa = new MemberSocialAccount()
                {
                    id = 0,
                    member_id = member.id,
                    type = "cell",
                    num = cell.Trim(),
                    valid = 1
                };
                await _context.memberSocialAccount.AddAsync(msa);
                await _context.SaveChangesAsync();

            }
            member = await _memberHelper.GetMemberBySessionKey(sessionKey, "wechat_mini_openid");
            if (cell != null && cell != "" && cell.Length == 11)
            {
                member._cell = cell;
            }
            //return Ok(_memberHelper.RemoveSensitiveInfo(member));
            return Ok(member);

        }

        [HttpGet]
        public async Task<ActionResult<MiniAppUser>> UpdateUserInfo(string sessionKey, string encData, string iv)
        {
            try
            {
                sessionKey = Util.UrlDecode(sessionKey);
                encData = Util.UrlDecode(encData);
                iv = Util.UrlDecode(iv);
                //
                UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
                MiniAppUser miniUser = user.miniAppUser;
                string json = Util.AES_decrypt(encData.Trim(), sessionKey, iv);
                Newtonsoft.Json.Linq.JToken jsonObj = (Newtonsoft.Json.Linq.JToken)Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                if (jsonObj["phoneNumber"] != null)
                {
                    miniUser.cell_number = jsonObj["phoneNumber"].ToString().Trim();
                }
                string nick = "";
                if (jsonObj["nickName"] != null && !jsonObj["nickName"].ToString().Trim().Equals("微信用户"))
                {
                    nick = jsonObj["nickName"].ToString().Trim();
                }
                else
                {
                    nick = miniUser.real_name.Trim();
                }
                miniUser.nick = nick;
                string gender = "";
                if (jsonObj["gender"] != null)
                {
                    if (jsonObj["gender"].ToString().Equals("0"))
                    {
                        gender = "男";
                    }
                    else
                    {
                        gender = "女";
                    }
                }
                miniUser.gender = gender.Trim();
                if (jsonObj["unionId"] != null && jsonObj["unionId"].ToString().Trim().Equals(""))
                {
                    miniUser.union_id = jsonObj["unionId"].ToString().Trim();
                }
                if (jsonObj["avatarUrl"] != null && !jsonObj["avatarUrl"].ToString().Trim().Equals(""))
                {
                    miniUser.head_image = jsonObj["avatarUrl"].ToString().Trim();
                }
                _context.Entry(miniUser).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return user.miniAppUser;
            }
            catch
            {

                return BadRequest();
            }
        }

       
        private bool MiniAppUserExists(string id)
        {
            return _context.MiniAppUsers.Any(e => e.open_id == id);
        }
    }
}

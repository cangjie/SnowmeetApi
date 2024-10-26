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
            UnicUser._context = context;
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
            var sList = await _context.MiniSessons.Where(s => s.session_key.Trim().Equals(sessionKey))
                .OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            if (sList == null || sList.Count <= 0)
            {
                return null;
            }
            return await _context.MiniAppUsers.FindAsync(sList[0].open_id);
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
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(openId);
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
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
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
            Member member = (Member)((OkObjectResult)(await _memberHelper.GetMemberByCell(cell, staffSessionKey)).Result).Value;
            if (member==null)
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
            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin)
            {
                return NoContent();
            }

           
            Member member = await _memberHelper.GetMember(openId, "wechat_mini_openid");

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
            return Ok(mUser);

    

            /*


            MiniAppUser miniUser = await _context.MiniAppUsers.FindAsync(openId);

            var orderL = await _context.OrderOnlines.Where(o => o.open_id.Trim().Equals(miniUser.open_id.Trim())
                && o.pay_state == 1).AsNoTracking().Take(1).ToListAsync();
            if (orderL.Count > 0)
            {
                miniUser.isMember = true;
            }

            return await _context.MiniAppUsers.FindAsync(openId);
            */
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<MiniAppUser>> GetMiniUserByTicket(string code, string sessionKey)
        {
            var ticket = await _context.Ticket.FindAsync(code.Trim());
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

            var mSessionList = await _context.MiniSessons.Where(m => (m.session_key.Trim().Equals(sessionKey.Trim()))).ToListAsync();
            if (mSessionList.Count == 0)
            {
                return NotFound();
            }
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(mSessionList[0].open_id);
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

            var mSessionList = await _context.MiniSessons.Where(m => (m.session_key.Trim().Equals(sessionKey.Trim()))).ToListAsync();
            if (mSessionList.Count == 0)
            {
                return NotFound();
            }
            MiniAppUser user = await _context.MiniAppUsers.FindAsync(mSessionList[0].open_id);
            return Ok(user);

        }

        [HttpPost]
        public async Task<ActionResult<MiniAppUser>> UpdateMiniUser([FromQuery] string sessionKey, [FromBody] MiniAppUser miniUser)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            if (!user.isAdmin && !miniUser.open_id.Trim().Equals(""))
            {
                return BadRequest();
            }
            string openId = miniUser.open_id.Trim();

            



            if (openId.Equals(""))
            {
                openId = user.miniAppOpenId.Trim();
            }

            Member member = await _memberHelper.GetMember(openId.Trim(), "wechat_mini_openid");
            
            member.real_name = miniUser.real_name.Trim();
            member.gender = miniUser.gender.Trim();
            
            _context.member.Entry(member).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await _memberHelper.UpdateDetailInfo(member.id, miniUser.cell_number.Trim(), "cell", true);
            await _memberHelper.UpdateDetailInfo(member.id, miniUser.wechat_id.Trim(), "wechat_id", false);

            MiniAppUser mUser = (MiniAppUser)((OkObjectResult)(await GetMiniAppUser(member.wechatMiniOpenId, sessionKey)).Result).Value;

            return Ok(mUser);

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
            string gender = "";
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
                        gender = "男";
                    }
                    else
                    {
                        gender = "女";
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
            return Ok(_memberHelper.RemoveSensitiveInfo(member));


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
                UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
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
            catch (Exception err)
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

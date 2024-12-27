using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;
using SnowmeetApi.Models.Users;
using System.Collections.Generic;
using SnowmeetApi.Models.Order;

namespace SnowmeetApi.Controllers.User
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RefereeController : ControllerBase
    {
        public ApplicationDBContext _db;
        public IConfiguration _config;
        public RefereeController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }

        [NonAction]
        public async Task<Models.Users.Referee> GetReferee(int memberId, string cosumeType)
        {
            var rl = await _db.referee.Where(r => r.consume_type.Trim().Equals(cosumeType.Trim()) && r.member_id == memberId)
                .AsNoTracking().ToListAsync();
            if (rl == null || rl.Count == 0)
            {
                return null;
            }
            else
            {
                return rl[0];
            }
        }
        [NonAction]
        public async Task<Models.Users.Referee> SetReferee(int memberId, int channelMemberId, string consumeType, int orderId = 0, int bizId = 0)
        {
            Referee r = await GetReferee(memberId, consumeType.Trim());
            if (r!=null)
            {
                return null;
            }
            r = new Referee()
            {
                id = 0,
                member_id = memberId,
                order_id = orderId,
                biz_id = bizId,
                consume_type = consumeType.Trim(),
                channel_member_id = channelMemberId
            };
            await _db.referee.AddAsync(r);
            await _db.SaveChangesAsync();
            return r;
        }

        [NonAction]
        public async Task<Models.Order.Kol> GetKol(int memberId)
        {
            var l = await _db.kol.Where(k => k.member_id == memberId).ToListAsync();
            if (l == null || l.Count == 0)
            {
                string openId = "";
                try
                {
                    MemberSocialAccount msa = await _db.memberSocialAccount
                        .Where(m => (m.type.Trim().Equals("wechat_mini_openid") && m.member_id == memberId))
                        .FirstAsync();
                    openId = msa.num.Trim();
                }
                catch
                {

                }
                if(!openId.Trim().Equals(""))
                {
                    Models.Order.Kol k = new Models.Order.Kol()
                    {
                        id = 0,
                        member_id = memberId,
                        wechat_bind = 1,
                        wechat_open_id = openId,
                        ali_bind = 0,
                        ali_login_name = ""
                    };
                    await _db.kol.AddAsync(k);
                    await _db.SaveChangesAsync();
                    return k;
                }
                else
                {
                    return null;
                }
                
            }
            else
            {
                Kol k = l[0];
                if (k.wechat_bind == 0 || k.wechat_open_id.Trim().Equals(""))
                {
                    string openId = "";
                    try
                    {
                        MemberSocialAccount msa = await _db.memberSocialAccount
                            .Where(m => (m.type.Trim().Equals("wechat_mini_openid") && m.member_id == memberId))
                            .FirstAsync();
                        openId = msa.num.Trim();
                    }
                    catch
                    {

                    }
                    if (openId.Trim().Equals(""))
                    {
                        k.wechat_bind = 1;
                        k.wechat_open_id = openId.Trim();
                        _db.kol.Entry(k).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                    }
                }
                return k;
            }
        }
    }
}
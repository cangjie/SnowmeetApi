using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Controllers.CoachPlatform;

// 顾客领取分销商的优惠券批次（小程序 pages/coach/coupon_claim）：扫批次二维码或点开转发的小程序卡片到领取页。
// 登录即可看；领取需要已是会员（授权过手机号），没有会员返回 code 5，前端授权手机号后再调一次。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class CouponBatchController(ApplicationDBContext db, IConfiguration config) : CoachApiBase(db)
{
    private readonly CoachCouponBatchService _batches = new(db, config);

    /// <summary>领取页打开时的状态：可领取 / 已领过（带券码）/ 今天领过 / 领完 / 过期 / 失效 / 分销商本人。不改数据。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetShare(string c, string? sessionKey = null)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        return await Run(async () => new { view = await _batches.GetShareAsync(c, member?.id, session.wechat_openid ?? "", DateTime.Now), needMember = member == null });
    }

    /// <summary>领取：领取时才生成券。每个 openid 每天只能领一次、同一批次只能领一次，领满规定张数不能再领。</summary>
    [HttpPost]
    public async Task<ApiResult<object>> Claim(string c, string sessionKey)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        if (member == null) return Result(5, "请先授权手机号");
        return await Run(async () => new { view = await _batches.ClaimAsync(c, member, session.wechat_openid ?? "", DateTime.Now), needMember = false });
    }
}

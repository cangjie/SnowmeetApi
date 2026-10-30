using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Controllers.CoachPlatform;

// 学员领取学员卡（小程序 pages/coach/claim），以及开单页选卡弹层用的「该会员可用的学员卡」。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class CoachCardController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http) : CoachApiBase(db)
{
    private readonly CoachCardService _cards = new(db, config);
    private readonly CoachPricingService _pricing = new(db);

    /// <summary>
    /// 分享卡当前状态：可领取 / 已领取（本人）/ 已被别人领走 / 已失效 / 教练本人 / 已有该教练的卡。
    /// 还没有会员（没授权过手机号）的人也能看，needMember=true 时前端走手机号授权。
    /// </summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetShareCard(string t, string? sessionKey = null)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        return await Run(async () =>
        {
            ShareCardView view = await _cards.GetShareCardAsync(t, member?.id, DateTime.Now);
            return new { view, needMember = member == null };
        });
    }

    /// <summary>领取。需要已是会员，否则返回 code 5，前端授权手机号（成为会员）后再调一次。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> ClaimShareCard(string t, string sessionKey)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        if (member == null) return Result(5, "请先授权手机号");
        CoachCardService.ClaimResult r;
        try { r = await _cards.ClaimAsync(t, member.id, DateTime.Now); }
        catch (CoachException e) { return Result(e.Code, e.Message); }
        // 成功（含「你已领取过」的重复点击）：返回卡的最新状态；其余状态（已被别人领走、已失效、本人、已有该教练的卡）
        // 给 code 1 + state，前端据此显示对应画面
        if (r.state == ShareCardState.ClaimedByMe)
        {
            ShareCardView view = await _cards.GetShareCardAsync(t, member.id, DateTime.Now);
            return Result(0, "", new { state = r.state, claimed = true, view });
        }
        return Result(1, r.message, new { state = r.state });
    }

    /// <summary>我的学员卡（我的页 / 领取成功后）：只列已领取的，含有效期和各店价格。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetMyCards(string sessionKey)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        if (member == null) return Result(0, "", new { cards = new List<object>() });
        return await Run(async () =>
        {
            DateTime now = DateTime.Now;
            List<CoachCard> cards = await Db.coachCard.AsNoTracking().Where(c => c.member_id == member.id && c.status == CoachCard.StatusClaimed)
                .OrderByDescending(c => c.claimed_at).ToListAsync();
            List<int> coachIds = cards.Select(c => c.coach_id).Distinct().ToList();
            Dictionary<int, string> names = await Db.coach.AsNoTracking().Where(c => coachIds.Contains(c.id)).ToDictionaryAsync(c => c.id, c => c.display_name);
            return new
            {
                cards = cards.Select(c => new
                {
                    id = c.id, coachName = names.TryGetValue(c.coach_id, out string? n) ? n : "", claimedAt = c.claimed_at, validUntil = c.end_date,
                    expired = c.end_date != null && c.end_date < now,
                    carePrices = CoachCardRules.ParseCarePrices(c.care_price_snapshot), rentalRate = c.rental_rate_snapshot,
                }),
            };
        });
    }

    /// <summary>
    /// 开单页「优惠券/卡」选择弹层用：该会员可用的学员卡，附本单预计优惠（店员 ≥ 100）。
    /// 养护：传 shop、tier（单项/双项）、commonCharge（该件当前服务费，含加急）、basePrice（单/双项基础价，不含加急加价）；
    /// 租赁：bizType=租赁，传 rentalAmount（用来预估优惠）。
    /// 客户端不知道不含加急加价的单/双项基础价：basePrice 不传（0）时，urgent=1 由服务端按门店价格表查非加急价，
    /// urgent=0 就取 commonCharge。
    /// </summary>
    [HttpGet]
    public async Task<ApiResult<object>> ListUsableCards(string sessionKey, int memberId, string bizType = "养护",
        string? shop = null, string? tier = null, double commonCharge = 0, double basePrice = 0, double rentalAmount = 0, int urgent = 0)
    {
        if (await SessionAsync(sessionKey) == null) return Result(2, "登录已失效，请重新进入小程序");
        Staff? staff = await Util.GetStaffBySessionKey(Db, Util.UrlDecode(sessionKey), "wechat_mini_openid");
        if (staff == null || staff.title_level < 100) return Result(3, "没有权限");
        return await Run(async () =>
        {
            string biz = (bizType ?? "").Trim();
            if (biz != CoachCardRules.BizCare && biz != CoachCardRules.BizRental) throw new CoachException("业务类型只能是养护或租赁");
            // 用档位造一个只含计费项的 care，复用 ResolveTier：双项 = 修刃 + 热蜡，单项 = 只修刃
            Care? care = null;
            if (biz == CoachCardRules.BizCare && CoachCardRules.IsValidTier(tier))
                care = new Care { equipment = "", need_edge = 1, need_wax = tier == CoachCardRules.TierTwo ? 1 : 0 };
            if (care != null && basePrice <= 0 && commonCharge > 0)
            {
                basePrice = commonCharge;
                if (urgent == 1)
                {
                    // 加急单：教练价只作用于基础价，加急加价不打折。基础价 = 同门店同档位的非加急商品价；没有加急商品的店 = commonCharge
                    Care nonUrgent = new Care { equipment = "", need_edge = 1, need_wax = tier == CoachCardRules.TierTwo ? 1 : 0, urgent = 0 };
                    Product? baseProduct = await new CareController(Db, config, http).GetProduct((shop ?? "").Trim(), nonUrgent);
                    if (baseProduct != null) basePrice = Math.Min(baseProduct.sale_price, commonCharge);
                }
            }
            List<UsableCoachCard> cards = await _pricing.ListUsableAsync(memberId, biz, shop, care, commonCharge, basePrice, rentalAmount, DateTime.Now);
            return new { cards };
        });
    }
}

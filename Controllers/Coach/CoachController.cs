using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Controllers.CoachPlatform;

// 教练自己的界面（小程序 pages/coach/home）：发学员卡、看领取情况、看学员消费、看自己的券。
// 身份 = 会话里的会员有一条教练记录；已停用的教练还能看历史，但不能再发新卡。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class CoachController(ApplicationDBContext db, IConfiguration config) : CoachApiBase(db)
{
    private readonly CoachCardService _cards = new(db, config);
    private readonly CoachQueryService _query = new(db);

    public sealed record CardIdInput(int cardId);

    /// <summary>分享卡的落地页路径（小程序 onShareAppMessage 的 path）。</summary>
    public const string ClaimPage = "/pages/coach/claim/claim";

    private async Task<ApiResult<object>> AsCoach(string? sessionKey, Func<Models.Coach, Task<object?>> action)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        if (member == null) return Result(5, "请先授权手机号");
        Models.Coach? coach = await Db.coach.AsNoTracking().Where(c => c.member_id == member.id)
            .OrderByDescending(c => c.valid).ThenByDescending(c => c.id).FirstOrDefaultAsync();
        if (coach == null) return Result(3, "你还不是教练");
        return await Run(() => action(coach));
    }

    private async Task<object> CoachInfoAsync(Models.Coach coach)
    {
        List<CoachCarePrice> prices = await Db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coach.id).OrderBy(p => p.shop).ThenBy(p => p.tier).ToListAsync();
        return new
        {
            id = coach.id, name = coach.display_name, valid = coach.valid == 1, rentalRate = coach.rental_rate,
            carePrices = prices.Select(p => new { p.shop, p.tier, p.mode, p.value }),
            validUntil = CoachCardRules.SeasonEnd(DateTime.Now, config),
        };
    }

    /// <summary>首页顶部：我的教练信息、学员卡权益、卡的统计。不是教练时 isCoach=false（我的页据此决定要不要显示入口）。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetMyCoach(string sessionKey)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return Result(2, "登录已失效，请重新进入小程序");
        Member? member = await MemberOfAsync(session);
        Models.Coach? coach = member == null ? null : await Db.coach.AsNoTracking().Where(c => c.member_id == member.id)
            .OrderByDescending(c => c.valid).ThenByDescending(c => c.id).FirstOrDefaultAsync();
        if (coach == null) return Result(0, "", new { isCoach = false });
        return await Run(async () => new { isCoach = true, coach = await CoachInfoAsync(coach), cards = await _query.CardCountsAsync(coach.id) });
    }

    /// <summary>先在服务器建一张待领取的卡，返回分享路径。前端随后用 open-type="share" 的按钮把它分享出去。</summary>
    [HttpPost]
    public Task<ApiResult<object>> CreateCard(string sessionKey) => AsCoach(sessionKey, async coach =>
    {
        CoachCard card = await _cards.CreateCardAsync(coach.id, DateTime.Now);
        return new { cardId = card.id, tokenTail = card.token.Substring(card.token.Length - 4).ToUpperInvariant(), sharePath = ClaimPage + "?t=" + card.token };
    });

    [HttpGet]
    public Task<ApiResult<object>> ListMyCards(string sessionKey, string? status = null) => AsCoach(sessionKey, async coach =>
    {
        List<CoachCardView> cards = await _query.ListCardsAsync(coach.id, status);
        // 待领取的卡带上分享路径，前端每行都有自己的分享按钮
        var withPath = cards.Select(c => new { card = c, sharePath = c.status == CoachCard.StatusIssued ? ClaimPage + "?t=" + c.token : null }).ToList();
        return new { counts = await _query.CardCountsAsync(coach.id), cards = withPath };
    });

    [HttpPost]
    public Task<ApiResult<object>> RevokeMyCard(string sessionKey, [FromBody] CardIdInput input) => AsCoach(sessionKey, async coach =>
    {
        await _cards.RevokeAsync(input.cardId, coach.id, DateTime.Now);
        return new { cardId = input.cardId };
    });

    [HttpGet]
    public Task<ApiResult<object>> GetStudents(string sessionKey) => AsCoach(sessionKey, async coach =>
    {
        List<StudentSummary> students = await _query.GetStudentsAsync(coach.id);
        return new
        {
            summary = new { students = students.Count, amount = CoachCardRules.Money(students.Sum(s => s.amount)), cardDiscount = CoachCardRules.Money(students.Sum(s => s.cardDiscount)) },
            students,
        };
    });

    [HttpGet]
    public Task<ApiResult<object>> GetStudentOrders(string sessionKey, int memberId) => AsCoach(sessionKey, async coach =>
    {
        var (student, orders) = await _query.GetStudentOrdersAsync(coach.id, memberId);
        return new { student, orders };
    });

    [HttpGet]
    public Task<ApiResult<object>> GetMyCoupons(string sessionKey) => AsCoach(sessionKey, async coach => await _query.GetCouponsAsync(coach.id, DateTime.Now));
}

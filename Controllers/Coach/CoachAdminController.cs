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
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Controllers.CoachPlatform;

// 管理员后台「教练管理」（小程序 pages/coach/admin）。只对系统管理员（title_level ≥ 300）开放。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class CoachAdminController(ApplicationDBContext db, IConfiguration config) : CoachApiBase(db)
{
    private readonly CoachAdminService _admin = new(db, config);
    private readonly CoachQueryService _query = new(db);
    private readonly CoachCardService _cards = new(db, config);

    public sealed record AddCoachInput(int? memberId, string? cell, string? name);
    public sealed record UpdateCoachInput(int coachId, string? name, bool? valid);
    public sealed record SetPricingInput(int coachId, List<CarePriceInput>? care, double? rental_rate);
    public sealed record CardIdInput(int cardId);
    public sealed record GrantInput(List<int>? coachIds, int templateId, int countEach, string? requestId);

    private async Task<ApiResult<object>> AsAdmin(string? sessionKey, Func<Staff, Task<object?>> action)
    {
        if (await SessionAsync(sessionKey) == null) return Result(2, "登录已失效，请重新进入小程序");
        Staff? op = await new StaffController(Db).GetStaffBySessionKey(sessionKey!);
        if (!StaffAccountRules.IsAdmin(op)) return Result(3, "只有系统管理员可以管理教练");
        return await Run(() => action(op!));
    }

    private async Task<object> CoachViewAsync(Models.Coach c)
    {
        List<CoachCarePrice> prices = await _admin.GetCarePricesAsync(c.id);
        object counts = await _query.CardCountsAsync(c.id);
        string? tail = await PhoneTailAsync(c.member_id);
        return new
        {
            id = c.id, memberId = c.member_id, name = c.display_name, valid = c.valid == 1, phoneTail = tail,
            rentalRate = c.rental_rate, carePrices = prices.Select(p => new { p.shop, p.tier, p.mode, p.value }), cards = counts,
        };
    }

    private async Task<string> PhoneTailAsync(int memberId)
    {
        string? cell = await Db.memberSocialAccount.AsNoTracking().Where(a => a.member_id == memberId && a.valid == 1 && a.type == MemberSocialAccount.TYPE_CELL)
            .OrderByDescending(a => a.id).Select(a => a.num).FirstOrDefaultAsync();
        return CoachCardRules.PhoneTail(cell);
    }

    [HttpGet]
    public Task<ApiResult<object>> ListCoaches(string sessionKey) => AsAdmin(sessionKey, async _ =>
    {
        List<Models.Coach> coaches = await Db.coach.AsNoTracking().OrderByDescending(c => c.valid).ThenBy(c => c.id).ToListAsync();
        List<object> rows = new();
        int issued = 0, claimed = 0; double consumption = 0;
        foreach (Models.Coach c in coaches)
        {
            var (students, amount) = await _query.SummaryAsync(c.id);
            object view = await CoachViewAsync(c);
            var counts = (dynamic)((dynamic)view).cards;
            issued += (int)counts.total; claimed += (int)counts.claimed; consumption += amount;
            int grants = await Db.coachCouponGrant.AsNoTracking().CountAsync(g => g.coach_id == c.id);
            rows.Add(new { coach = view, students, seasonAmount = amount, couponsGranted = grants });
        }
        return new { stats = new { coaches = coaches.Count(c => c.valid == 1), cardsIssued = issued, cardsClaimed = claimed, consumption = CoachCardRules.Money(consumption) }, coaches = rows };
    });

    [HttpGet]
    public Task<ApiResult<object>> GetCoach(string sessionKey, int id) => AsAdmin(sessionKey, async _ =>
    {
        Models.Coach c = await Db.coach.AsNoTracking().FirstOrDefaultAsync(x => x.id == id) ?? throw new CoachException("没有找到这位教练");
        return await CoachViewAsync(c);
    });

    [HttpGet]
    public Task<ApiResult<object>> LookupMember(string sessionKey, string cell) => AsAdmin(sessionKey, async _ => await _admin.LookupMemberAsync(cell));

    [HttpPost]
    public Task<ApiResult<object>> AddCoach(string sessionKey, [FromBody] AddCoachInput input) => AsAdmin(sessionKey, async op =>
    {
        Models.Coach c = await _admin.AddCoachAsync(op, input.memberId, input.cell, input.name);
        return await CoachViewAsync(c);
    });

    [HttpPost]
    public Task<ApiResult<object>> UpdateCoach(string sessionKey, [FromBody] UpdateCoachInput input) => AsAdmin(sessionKey, async _ =>
    {
        Models.Coach c = await _admin.UpdateCoachAsync(input.coachId, input.name, input.valid);
        return await CoachViewAsync(c);
    });

    [HttpPost]
    public Task<ApiResult<object>> SetCoachPricing(string sessionKey, [FromBody] SetPricingInput input) => AsAdmin(sessionKey, async _ =>
    {
        Models.Coach c = await _admin.SetPricingAsync(input.coachId, new PricingInput(input.care, input.rental_rate));
        return await CoachViewAsync(c);
    });

    [HttpGet]
    public Task<ApiResult<object>> ListCoachCards(string sessionKey, int coachId, string? status = null) =>
        AsAdmin(sessionKey, async _ => new { counts = await _query.CardCountsAsync(coachId), cards = await _query.ListCardsAsync(coachId, status) });

    [HttpPost]
    public Task<ApiResult<object>> RevokeCard(string sessionKey, [FromBody] CardIdInput input) =>
        AsAdmin(sessionKey, async _ => { await _cards.RevokeAsync(input.cardId, null, DateTime.Now); return new { cardId = input.cardId }; });

    [HttpGet]
    public Task<ApiResult<object>> ListCouponTemplates(string sessionKey) => AsAdmin(sessionKey, async _ => new
    {
        templates = await Db.ticketTemplate.AsNoTracking().Where(t => t.valid == 1 && t.hide == 0)
            .OrderBy(t => t.id).Select(t => new { t.id, t.name, t.memo }).ToListAsync(),
    });

    [HttpPost]
    public Task<ApiResult<object>> GrantCoupons(string sessionKey, [FromBody] GrantInput input) =>
        AsAdmin(sessionKey, async op => await _admin.GrantCouponsAsync(op, input.coachIds, input.templateId, input.countEach, input.requestId));
}

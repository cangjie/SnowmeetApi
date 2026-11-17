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
using SnowmeetApi.Services.Storage;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers.CoachPlatform;

// 教练自己的界面（小程序 pages/coach/home）：发学员卡、看领取情况、看学员消费、看自己的券。
// 身份 = 会话里的会员有一条教练记录；已停用的教练还能看历史，但不能再发新卡。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class CoachController(ApplicationDBContext db, IConfiguration config) : CoachApiBase(db)
{
    private readonly CoachCardService _cards = new(db, config);
    private readonly CoachQueryService _query = new(db, FileStorageOptions.From(config).PublicBaseUrl);

    public sealed record CardIdInput(int cardId);
    /// <param name="templateId">卡种（机构必填）</param>
    /// <param name="channel">分享方式：qr 二维码 / card 小程序卡片；每次分享都新建一张待领取的卡并记下方式（旧版小程序不传）</param>
    public sealed record CreateCardInput(int? templateId, string? channel = null);

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
        bool canIssue = coach.valid == 1 && await DistributorAccess.CanIssueAsync(Db, coach);
        string? introducerType = await Db.introducer.AsNoTracking().Where(i => i.coach_id == coach.id && i.valid == 1).Select(i => i.type).FirstOrDefaultAsync();
        return new
        {
            id = coach.id, name = coach.display_name, valid = canIssue, rentalRate = coach.rental_rate, introducerType,
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
        return await Run(async () => new { isCoach = true, coach = await CoachInfoAsync(coach), cards = await _query.CardCountsAsync(coach.id, hideDeletedShares: true) });
    }

    /// <summary>先在服务器建一张待领取的卡，返回分享路径。前端随后用 open-type="share" 的按钮把它分享出去。</summary>
    [HttpPost]
    public Task<ApiResult<object>> CreateCard(string sessionKey, [FromBody] CreateCardInput? input = null) => AsCoach(sessionKey, async coach =>
    {
        // 只有机构能发整个雪季有效的会籍卡；教练和 KOL 只能发单张优惠券（用户 2026-11-17 定）
        string? type = await Db.introducer.AsNoTracking().Where(i => i.coach_id == coach.id && i.valid == 1).Select(i => i.type).FirstOrDefaultAsync();
        if (type != IntroducerRules.TypeOrg) throw new CoachException("只有机构可以发整个雪季有效的会籍卡；教练和 KOL 请使用单张优惠券");
        CoachCard card = await _cards.CreateCardAsync(coach.id, DateTime.Now, input?.templateId, input?.channel);
        return new
        {
            cardId = card.id, cardName = card.card_name_snapshot, tokenTail = card.token.Substring(card.token.Length - 4).ToUpperInvariant(),
            sharePath = ClaimPage + "?t=" + card.token, channel = CoachCardRules.ShareChannelOf(card.remark), createDate = card.create_date,
        };
    });

    /// <summary>
    /// 分享二维码（PNG）：给「分享二维码」按钮刚建好的那张待领取卡。scene = t=卡 token，落到领取页；
    /// 只有这张卡所属的机构、且卡还没被领取没被取消时才能取。顾客扫码后由登录的会员领取（领取要求已授权头像昵称）。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCardQr(string sessionKey, int cardId, [FromServices] MiniStaffBookingQrService qr, string envVersion = "release")
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return BadRequest(new { code = 2, message = "登录已失效，请重新进入小程序" });
        Member? member = await MemberOfAsync(session);
        Models.Coach? coach = member == null ? null : await Db.coach.AsNoTracking().Where(c => c.member_id == member.id)
            .OrderByDescending(c => c.valid).ThenByDescending(c => c.id).FirstOrDefaultAsync();
        if (coach == null) return BadRequest(new { code = 3, message = "你还不是教练" });
        CoachCard? card = await Db.coachCard.AsNoTracking().FirstOrDefaultAsync(c => c.id == cardId && c.coach_id == coach.id);
        if (card == null) return BadRequest(new { code = 1, message = "没有找到这张会籍卡" });
        if (card.status != CoachCard.StatusIssued) return BadRequest(new { code = 1, message = "这张会籍卡已被领取或已取消，不能再生成二维码" });
        try { return File(await qr.GetForCoachCardAsync(card.token, envVersion), "image/png"); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        { return BadRequest(new { code = 1, message = e.Message }); }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is System.Text.Json.JsonException)
        { return BadRequest(new { code = 1, message = "小程序码服务暂时不可用，请重试" }); }
    }

    [HttpGet]
    public Task<ApiResult<object>> ListMyCards(string sessionKey, string? status = null) => AsCoach(sessionKey, async coach =>
    {
        // 机构「删除」的分享（没人领取就撤销的卡）不再列出，也不计入统计
        List<CoachCardView> cards = await _query.ListCardsAsync(coach.id, status, hideDeletedShares: true);
        // 待领取的卡带上分享路径，前端每行都有「分享小程序卡片」（再次分享同一张卡，不再新记一次）
        var withPath = cards.Select(c => new { card = c, sharePath = c.status == CoachCard.StatusIssued ? ClaimPage + "?t=" + c.token : null }).ToList();
        return new { counts = await _query.CardCountsAsync(coach.id, hideDeletedShares: true), cards = withPath };
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

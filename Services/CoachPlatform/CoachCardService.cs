using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.CoachPlatform;

/// <summary>业务规则不允许。Code 与控制器的 ApiResult.code 对应：1 业务规则 / 3 无权限。</summary>
public sealed class CoachException(string message, int code = 1) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>分享卡打开后的状态，前端据此显示 C1～C7 各画面。</summary>
public static class ShareCardState
{
    /// <summary>可领取</summary>
    public const string Claimable = "claimable";
    /// <summary>本人已领取（领取成功后再点开也是这个）</summary>
    public const string ClaimedByMe = "claimed_by_me";
    /// <summary>已被别人领走</summary>
    public const string Taken = "taken";
    /// <summary>已撤销 / 过期 / 教练已停用 / 卡不存在</summary>
    public const string Invalid = "invalid";
    /// <summary>教练本人打开自己发出的卡</summary>
    public const string Self = "self";
    /// <summary>本雪季已领过这位教练的卡</summary>
    public const string AlreadyHas = "already_has";
}

public sealed class ShareCardView
{
    public string state { get; set; } = ShareCardState.Invalid;
    public string message { get; set; } = "";
    public string coachName { get; set; } = "";
    public List<CoachCardRules.CarePriceCell> carePrices { get; set; } = new();
    public double? rentalRate { get; set; }
    public DateTime? validUntil { get; set; }
    public int? cardId { get; set; }
}

public sealed class CoachCardService(ApplicationDBContext db, IConfiguration? config = null)
{
    /// <summary>教练名下待领取的卡：先在服务器建一行，再由前端用 open-type=share 分享出去。</summary>
    public async Task<CoachCard> CreateCardAsync(int coachId, DateTime now)
    {
        Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId);
        if (coach == null || coach.valid != 1) throw new CoachException("你的教练身份已停用，不能再发学员卡");
        if (!await HasAnyBenefitAsync(coach)) throw new CoachException("管理员还没有给你设置学员卡的价格，请先联系管理员");
        int pending = await db.coachCard.AsNoTracking().CountAsync(c => c.coach_id == coachId && c.status == CoachCard.StatusIssued
            && (c.claim_deadline == null || c.claim_deadline >= now));
        if (pending >= CoachCardRules.MaxUnclaimedCards)
            throw new CoachException("待领取的学员卡已有 " + pending + " 张，请等学员领取或撤销一些再生成");
        CoachCard card = new() { coach_id = coachId, token = CoachCardRules.NewToken(), status = CoachCard.StatusIssued, create_date = now };
        db.coachCard.Add(card);
        await db.SaveChangesAsync();
        return card;
    }

    private async Task<bool> HasAnyBenefitAsync(Coach coach)
    {
        if (coach.rental_rate != null) return true;
        return await db.coachCarePrice.AsNoTracking().AnyAsync(p => p.coach_id == coach.id && p.mode != CoachCardRules.ModeNone);
    }

    /// <summary>学员打开分享卡看到的状态（不改数据）。memberId 为空 = 还没有会员。</summary>
    public async Task<ShareCardView> GetShareCardAsync(string? token, int? memberId, DateTime now)
    {
        CoachCard? card = await FindByTokenAsync(token);
        if (card == null) return Invalid("这张学员卡不存在或已失效");
        Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == card.coach_id);
        if (coach == null) return Invalid("这张学员卡不存在或已失效");
        ShareCardView view = new() { coachName = coach.display_name, cardId = card.id };
        if (memberId != null && memberId == coach.member_id)
        {
            view.state = ShareCardState.Self; view.message = "这是你发出的学员卡，转发给你的学员即可，教练本人不能领取";
            await FillBenefitAsync(view, card, coach, now); return view;
        }
        if (card.status == CoachCard.StatusClaimed)
        {
            bool mine = memberId != null && memberId == card.member_id;
            view.state = mine ? ShareCardState.ClaimedByMe : ShareCardState.Taken;
            view.message = mine ? "你已领取这张学员卡" : "这张学员卡已被领取";
            await FillBenefitAsync(view, card, coach, now); return view;
        }
        if (card.status != CoachCard.StatusIssued || !CoachCardRules.IsClaimable(card, now) || coach.valid != 1)
        {
            view.state = ShareCardState.Invalid; view.message = "这张学员卡已失效，请联系教练重新发送"; return view;
        }
        await FillBenefitAsync(view, card, coach, now);
        if (memberId != null && await HoldsCardAsync(coach.id, memberId.Value, CoachCardRules.SeasonEnd(now, config).Year))
        {
            view.state = ShareCardState.AlreadyHas; view.message = "本雪季你已经领过" + coach.display_name + "的学员卡了"; return view;
        }
        view.state = ShareCardState.Claimable; view.message = "";
        return view;
    }

    private static ShareCardView Invalid(string message) => new() { state = ShareCardState.Invalid, message = message };

    // 待领取的卡显示教练当前的价格（领取时会锁定这个价格）；已领取的显示快照
    private async Task FillBenefitAsync(ShareCardView view, CoachCard card, Coach coach, DateTime now)
    {
        if (card.status == CoachCard.StatusClaimed)
        {
            view.carePrices = CoachCardRules.ParseCarePrices(card.care_price_snapshot);
            view.rentalRate = card.rental_rate_snapshot;
            view.validUntil = card.end_date;
            return;
        }
        List<CoachCarePrice> prices = await db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coach.id).ToListAsync();
        view.carePrices = CoachCardRules.ParseCarePrices(CoachCardRules.SerializeCarePrices(prices));
        view.rentalRate = coach.rental_rate;
        view.validUntil = CoachCardRules.SeasonEnd(now, config);
    }

    public sealed class ClaimResult
    {
        public string state { get; set; } = ShareCardState.Invalid;
        public string message { get; set; } = "";
        public CoachCard? card { get; set; }
    }

    /// <summary>
    /// 学员领卡。主保证是原子 CAS：UPDATE … WHERE status='issued' 影响 1 行才算领到；
    /// 同一学员同一教练同一雪季的过滤唯一索引兜底；整个过程在 Serializable 事务 + 全局发放锁里。
    /// 领取时把教练当时的价格写进卡的快照，之后教练改价不影响这张卡。
    /// </summary>
    public Task<ClaimResult> ClaimAsync(string? token, int memberId, DateTime now)
    {
        return MiniTicketTransaction.Run(db, async () =>
        {
            CoachCard? card = await FindByTokenAsync(token);
            if (card == null) return Fail(ShareCardState.Invalid, "这张学员卡不存在或已失效");
            Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == card.coach_id);
            if (coach == null) return Fail(ShareCardState.Invalid, "这张学员卡不存在或已失效");
            if (coach.member_id == memberId) return Fail(ShareCardState.Self, "教练本人不能领取自己发出的学员卡");
            if (card.status == CoachCard.StatusClaimed)
            {
                return card.member_id == memberId
                    ? new ClaimResult { state = ShareCardState.ClaimedByMe, message = "你已领取这张学员卡", card = card }
                    : Fail(ShareCardState.Taken, "这张学员卡已被领取");
            }
            if (card.status != CoachCard.StatusIssued || !CoachCardRules.IsClaimable(card, now) || coach.valid != 1)
                return Fail(ShareCardState.Invalid, "这张学员卡已失效，请联系教练重新发送");

            DateTime end = CoachCardRules.SeasonEnd(now, config);
            if (await HoldsCardAsync(coach.id, memberId, end.Year))
                return Fail(ShareCardState.AlreadyHas, "本雪季你已经领过" + coach.display_name + "的学员卡了");

            List<CoachCarePrice> prices = await db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coach.id).ToListAsync();
            string snapshot = CoachCardRules.SerializeCarePrices(prices);
            double? rentalRate = coach.rental_rate;
            int rows;
            try
            {
                rows = await db.coachCard
                    .Where(c => c.id == card.id && c.status == CoachCard.StatusIssued && (c.claim_deadline == null || c.claim_deadline >= now))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.status, CoachCard.StatusClaimed)
                        .SetProperty(c => c.member_id, (int?)memberId)
                        .SetProperty(c => c.claimed_at, (DateTime?)now)
                        .SetProperty(c => c.start_date, (DateTime?)now)
                        .SetProperty(c => c.end_date, (DateTime?)end)
                        .SetProperty(c => c.season_end_year, (int?)end.Year)
                        .SetProperty(c => c.care_price_snapshot, snapshot)
                        .SetProperty(c => c.rental_rate_snapshot, rentalRate)
                        .SetProperty(c => c.update_date, (DateTime?)now));
            }
            catch (DbUpdateException)
            {
                // 过滤唯一索引兜底：并发下同一学员领了同一位教练的两张卡
                return Fail(ShareCardState.AlreadyHas, "本雪季你已经领过" + coach.display_name + "的学员卡了");
            }
            if (rows != 1) return Fail(ShareCardState.Taken, "这张学员卡已被领取");
            CoachCard claimed = (await db.coachCard.AsNoTracking().FirstAsync(c => c.id == card.id));
            return new ClaimResult { state = ShareCardState.ClaimedByMe, message = "", card = claimed };
        });
    }

    private static ClaimResult Fail(string state, string message) => new() { state = state, message = message };

    private Task<bool> HoldsCardAsync(int coachId, int memberId, int seasonEndYear) =>
        db.coachCard.AsNoTracking().AnyAsync(c => c.coach_id == coachId && c.member_id == memberId
            && c.status == CoachCard.StatusClaimed && c.season_end_year == seasonEndYear);

    private async Task<CoachCard?> FindByTokenAsync(string? token)
    {
        string t = (token ?? "").Trim();
        if (t.Length == 0 || t.Length > 32) return null;
        return await db.coachCard.AsNoTracking().FirstOrDefaultAsync(c => c.token == t);
    }

    /// <summary>
    /// 撤销。教练只能撤销自己名下待领取的卡；管理员（coachId 为空）可撤销待领取或已领取的卡。
    /// 撤销后不再用于新订单，已有的使用记录保留。
    /// </summary>
    public async Task<CoachCard> RevokeAsync(int cardId, int? ownerCoachId, DateTime now)
    {
        CoachCard? card = await db.coachCard.AsNoTracking().FirstOrDefaultAsync(c => c.id == cardId);
        if (card == null || (ownerCoachId != null && card.coach_id != ownerCoachId)) throw new CoachException("没有找到这张学员卡");
        if (card.status == CoachCard.StatusRevoked) return card;
        if (card.status == CoachCard.StatusMerged) throw new CoachException("这张学员卡已因会员合并作废");
        if (ownerCoachId != null && card.status != CoachCard.StatusIssued) throw new CoachException("学员已领取的卡不能自行撤销，请联系管理员");
        int rows = await db.coachCard.Where(c => c.id == cardId && c.status == card.status).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.status, CoachCard.StatusRevoked)
            .SetProperty(c => c.revoked_at, (DateTime?)now)
            .SetProperty(c => c.update_date, (DateTime?)now));
        if (rows != 1) throw new CoachException("这张学员卡刚刚被领取或修改，请刷新后再试");
        return (await db.coachCard.AsNoTracking().FirstAsync(c => c.id == cardId));
    }
}

/// <summary>MemberLogin 返回给小程序的教练简要信息。</summary>
public sealed class CoachBrief
{
    public int id { get; set; }
    public string display_name { get; set; } = "";
    public bool valid { get; set; }

    public static async Task<CoachBrief?> ForMemberAsync(ApplicationDBContext db, int memberId)
    {
        Coach? coach = await db.coach.AsNoTracking().Where(c => c.member_id == memberId)
            .OrderByDescending(c => c.valid).ThenByDescending(c => c.id).FirstOrDefaultAsync();
        return coach == null ? null : new CoachBrief { id = coach.id, display_name = coach.display_name, valid = coach.valid == 1 };
    }
}

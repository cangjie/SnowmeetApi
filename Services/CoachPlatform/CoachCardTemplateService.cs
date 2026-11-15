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

// 每个机构可维护多种卡。分享的是卡种，领取时创建独立的卡并锁定价格。
public sealed class CoachCardTemplateService(ApplicationDBContext db, IConfiguration config)
{
    private async Task CheckOwner(Coach coach, Introducer intro)
    {
        if (intro.type != IntroducerRules.TypeOrg || coach.id != intro.coach_id || coach.member_id != intro.member_id
            || coach.valid != 1 || !await DistributorAccess.CanIssueAsync(db, coach))
            throw new CoachException("只有启用的机构可以管理自己的卡种", 3);
    }

    private async Task ValidatePrices(int coachId, List<CarePriceInput>? care, double? rental)
    {
        if (!CoachCardRules.TryValidateRentalRate(rental, out string rentalError)) throw new CoachException(rentalError);
        var floors = await db.coachPriceFloor.AsNoTracking().Where(f => f.coach_id == coachId).ToListAsync();
        var seen = new HashSet<(string, string)>();
        var problems = new List<string>();
        foreach (var p in care ?? new())
        {
            string shop = (p.shop ?? "").Trim(), tier = (p.tier ?? "").Trim();
            if (!CoachCardRules.CareShops.Contains(shop) || !CoachCardRules.IsValidTier(tier) || !seen.Add((shop, tier)))
                throw new CoachException("养护项目无效或重复");
            if (!CoachCardRules.TryValidateCarePrice(p.mode, p.value, out string err)) throw new CoachException(shop + tier + "：" + err);
            var floor = floors.FirstOrDefault(f => f.biz_type == CoachCardRules.BizCare && f.shop == shop && f.tier == tier);
            string? bad = CoachFloorRules.CheckCare(floor, p.mode, p.value, shop + " " + tier);
            if (bad != null) problems.Add(bad);
        }
        string? rentBad = CoachFloorRules.CheckRental(floors.FirstOrDefault(f => f.biz_type == CoachCardRules.BizRental), rental);
        if (rentBad != null) problems.Add(rentBad);
        if (problems.Count > 0) throw new CoachException(string.Join("；", problems));
        if (!(care ?? new()).Any(p => p.mode != CoachCardRules.ModeNone) && rental == null) throw new CoachException("请至少设置一项优惠");
    }

    public async Task<object> ListAsync(Coach coach, Introducer intro)
    {
        await CheckOwner(coach, intro);
        var rows = await db.coachCardTemplate.AsNoTracking().Where(t => t.coach_id == coach.id && t.valid == 1).OrderByDescending(t => t.id).ToListAsync();
        return new { templates = rows.Select(t => new { t.id, t.name, carePrices = CoachCardRules.ParseCarePrices(t.care_prices), rentalRate = t.rental_rate,
            sharePath = "/pages/coach/claim/claim?template=" + t.token }).ToList() };
    }

    public async Task<object> EditorAsync(Coach coach, Introducer intro, int? id)
    {
        await CheckOwner(coach, intro);
        CoachCardTemplate? t = id == null ? null : await db.coachCardTemplate.AsNoTracking().FirstOrDefaultAsync(x => x.id == id && x.coach_id == coach.id && x.valid == 1)
            ?? throw new CoachException("没有找到这个卡种");
        var prices = CoachCardRules.ParseCarePrices(t?.care_prices);
        var floors = await db.coachPriceFloor.AsNoTracking().Where(f => f.coach_id == coach.id).ToListAsync();
        var cells = new List<object>();
        foreach (string shop in CoachCardRules.CareShops)
        foreach (string tier in new[] { CoachCardRules.TierOne, CoachCardRules.TierTwo })
        {
            var p = prices.FirstOrDefault(x => x.shop == shop && x.tier == tier);
            var f = floors.FirstOrDefault(x => x.biz_type == CoachCardRules.BizCare && x.shop == shop && x.tier == tier);
            string mode = p?.mode ?? CoachCardRules.ModeNone; double value = p?.value ?? 0;
            cells.Add(new { shop, tier, mode, value, open = CoachFloorRules.IsOpen(f), minRate = f?.min_rate, minFixed = f?.min_fixed,
                belowFloor = CoachFloorRules.CheckCare(f, mode, value, "") != null });
        }
        var rf = floors.FirstOrDefault(f => f.biz_type == CoachCardRules.BizRental);
        return new { canEdit = true, id = t?.id, name = t?.name ?? "", cells, validUntil = CoachCardRules.SeasonEnd(DateTime.Now, config),
            rental = new { rate = t?.rental_rate, open = rf?.min_rate != null, minRate = rf?.min_rate, belowFloor = CoachFloorRules.CheckRental(rf, t?.rental_rate) != null } };
    }

    public async Task<CoachCardTemplate> SaveAsync(Coach coach, Introducer intro, int? id, string? name, List<CarePriceInput>? care, double? rental)
    {
        await CheckOwner(coach, intro);
        string n = (name ?? "").Trim();
        if (n.Length == 0 || n.Length > 50) throw new CoachException("卡种名称请填写 1～50 个字符");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await ValidatePrices(coach.id, care, rental);
        var t = id == null ? new CoachCardTemplate { coach_id = coach.id, token = CoachCardRules.NewToken() }
            : await db.coachCardTemplate.AsTracking().FirstOrDefaultAsync(x => x.id == id && x.coach_id == coach.id && x.valid == 1) ?? throw new CoachException("没有找到这个卡种");
        string before = id == null ? "" : t.name + " | " + t.care_prices + " | 租赁:" + (t.rental_rate?.ToString("0.##") ?? "无");
        t.name = n;
        t.care_prices = CoachCardRules.SerializeCarePrices((care ?? new()).Select(p => new CoachCarePrice { shop = p.shop!, tier = p.tier!, mode = p.mode!, value = p.value }));
        t.rental_rate = rental; t.update_date = DateTime.Now;
        if (id == null) db.coachCardTemplate.Add(t);
        await db.SaveChangesAsync();
        string after = t.name + " | " + t.care_prices + " | 租赁:" + (t.rental_rate?.ToString("0.##") ?? "无");
        if (before != after)
        {
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("coach_card_template", "template", t.id, "机构设置会员卡种", intro.member_id, null, before, after, intro.name));
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return t;
    }

    private Task<CoachCardTemplate?> FindAsync(string? token) => db.coachCardTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.token == (token ?? "") && t.valid == 1);
    private static List<CarePriceInput> Prices(CoachCardTemplate t) => CoachCardRules.ParseCarePrices(t.care_prices).Select(p => new CarePriceInput(p.shop, p.tier, p.mode, p.value)).ToList();

    public async Task<ShareCardView> ViewAsync(string? token, int? memberId, DateTime now)
    {
        var t = await FindAsync(token);
        var coach = t == null ? null : await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == t.coach_id);
        if (t == null || coach == null) return new ShareCardView { message = "卡种已失效" };
        var v = new ShareCardView { coachName = coach.display_name, cardName = t.name, carePrices = CoachCardRules.ParseCarePrices(t.care_prices), rentalRate = t.rental_rate, validUntil = CoachCardRules.SeasonEnd(now, config) };
        if (memberId == coach.member_id) { v.state = ShareCardState.Self; return v; }
        var owned = memberId == null ? null : await db.coachCard.AsNoTracking().FirstOrDefaultAsync(c => c.template_id == t.id && c.member_id == memberId && c.status == CoachCard.StatusClaimed && c.season_end_year == v.validUntil.Value.Year);
        if (owned != null) { v.state = ShareCardState.ClaimedByMe; v.cardId = owned.id; v.cardName = owned.card_name_snapshot ?? t.name; v.carePrices = CoachCardRules.ParseCarePrices(owned.care_price_snapshot); v.rentalRate = owned.rental_rate_snapshot; v.validUntil = owned.end_date; return v; }
        if (coach.valid != 1 || !await DistributorAccess.CanIssueAsync(db, coach)) { v.message = "卡种已失效"; return v; }
        try { await ValidatePrices(coach.id, Prices(t), t.rental_rate); }
        catch (CoachException) { v.message = "该卡种权益需要机构调整后才能领取"; return v; }
        v.state = ShareCardState.Claimable; return v;
    }

    public Task<CoachCard> ClaimAsync(string token, int memberId, DateTime now) => MiniTicketTransaction.Run(db, async () =>
    {
        var t = await FindAsync(token) ?? throw new CoachException("卡种已失效");
        var coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == t.coach_id) ?? throw new CoachException("卡种已失效");
        if (coach.member_id == memberId) throw new CoachException("不能领取自己发出的卡");
        var end = CoachCardRules.SeasonEnd(now, config);
        var existing = await db.coachCard.AsNoTracking().FirstOrDefaultAsync(c => c.template_id == t.id && c.member_id == memberId && c.status == CoachCard.StatusClaimed && c.season_end_year == end.Year);
        if (existing != null) return existing;
        if (coach.valid != 1 || !await DistributorAccess.CanIssueAsync(db, coach)) throw new CoachException("卡种已失效");
        await ValidatePrices(coach.id, Prices(t), t.rental_rate);
        var card = new CoachCard { coach_id = coach.id, template_id = t.id, card_name_snapshot = t.name, token = CoachCardRules.NewToken(), status = CoachCard.StatusClaimed,
            member_id = memberId, claimed_at = now, start_date = now, end_date = end, season_end_year = end.Year, care_price_snapshot = t.care_prices, rental_rate_snapshot = t.rental_rate, create_date = now };
        db.coachCard.Add(card); await db.SaveChangesAsync();
        await new DistributorCustomerService(db).TryOnCardClaimedAsync(coach.id, memberId, card.id, now);
        return card;
    });
}

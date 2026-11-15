using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

public sealed record CareFloorInput(string? shop, string? tier, double? min_rate, double? min_fixed);
public sealed record FloorInput(List<CareFloorInput>? care, double? rental_min_rate);

/// <summary>
/// 机构自己制作会员卡：管理员给机构设底价（下限），机构在底价之内自己设养护 / 租赁优惠，保存即生效，只影响之后领取的新卡。
/// 「指定商品和服务」就是有优惠的那些格子：门店 × 单项 / 双项的养护，和租赁；没设优惠的格子不享受会员卡价。
/// </summary>
public sealed class CoachFloorService(ApplicationDBContext db, IConfiguration config)
{
    private static readonly string[] Tiers = { CoachCardRules.TierOne, CoachCardRules.TierTwo };

    public async Task<List<CoachPriceFloor>> GetFloorsAsync(int coachId) =>
        await db.coachPriceFloor.AsNoTracking().Where(f => f.coach_id == coachId).ToListAsync();

    private static CoachPriceFloor? CareFloor(List<CoachPriceFloor> floors, string shop, string tier) =>
        floors.FirstOrDefault(f => f.biz_type == CoachCardRules.BizCare && f.shop == shop && f.tier == tier);

    private static CoachPriceFloor? RentalFloor(List<CoachPriceFloor> floors) =>
        floors.FirstOrDefault(f => f.biz_type == CoachCardRules.BizRental);

    /// <summary>管理员的底价视图：养护 4 格 + 租赁，没设的格子 minRate / minFixed 为空（= 未开放）。</summary>
    public async Task<object> GetFloorViewAsync(int coachId)
    {
        List<CoachPriceFloor> floors = await GetFloorsAsync(coachId);
        var care = new List<object>();
        foreach (string shop in CoachCardRules.CareShops)
            foreach (string tier in Tiers)
            {
                CoachPriceFloor? f = CareFloor(floors, shop, tier);
                care.Add(new { shop, tier, minRate = f?.min_rate, minFixed = f?.min_fixed });
            }
        return new { care, rentalMinRate = RentalFloor(floors)?.min_rate };
    }

    /// <summary>管理员设底价。每个养护格和租赁都 upsert 一行（两个值都空 = 不开放）。写审计。</summary>
    public async Task SetFloorsAsync(int coachId, FloorInput input, Staff op)
    {
        Coach coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId) ?? throw new CoachException("没有找到这位分销商");
        var wanted = new Dictionary<(string, string), CareFloorInput>();
        foreach (CareFloorInput p in input.care ?? new())
        {
            string shop = (p.shop ?? "").Trim(), tier = (p.tier ?? "").Trim();
            if (!CoachCardRules.CareShops.Contains(shop)) throw new CoachException("底价只能设置万龙服务中心和崇礼旗舰店");
            if (!CoachCardRules.IsValidTier(tier)) throw new CoachException("养护档位只能是单项或双项");
            string? bad = CoachFloorRules.ValidateFloorInput(p.min_rate, p.min_fixed, shop + " " + tier);
            if (bad != null) throw new CoachException(bad);
            wanted[(shop, tier)] = p;
        }
        string? rentalBad = CoachFloorRules.ValidateFloorInput(input.rental_min_rate, null, "租赁");
        if (rentalBad != null) throw new CoachException(rentalBad);

        string before = Describe(await GetFloorsAsync(coachId));
        List<CoachPriceFloor> existing = await GetFloorsAsync(coachId);
        DateTime now = DateTime.Now;
        void Upsert(string biz, string shop, string tier, double? minRate, double? minFixed)
        {
            CoachPriceFloor? row = existing.FirstOrDefault(f => f.biz_type == biz && f.shop == shop && f.tier == tier);
            if (row == null)
                db.coachPriceFloor.Add(new CoachPriceFloor { coach_id = coachId, biz_type = biz, shop = shop, tier = tier, min_rate = minRate, min_fixed = minFixed, updated_by_staff_id = op.id, update_date = now });
            else if (row.min_rate != minRate || row.min_fixed != minFixed)
            {
                row.min_rate = minRate; row.min_fixed = minFixed; row.updated_by_staff_id = op.id; row.update_date = now;
                db.Entry(row).State = EntityState.Modified;
            }
        }
        foreach (string shop in CoachCardRules.CareShops)
            foreach (string tier in Tiers)
            {
                wanted.TryGetValue((shop, tier), out CareFloorInput? p);
                Upsert(CoachCardRules.BizCare, shop, tier, p?.min_rate, p?.min_fixed);
            }
        Upsert(CoachCardRules.BizRental, "", "", input.rental_min_rate, null);
        await db.SaveChangesAsync();
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("coach_price_floor", "floor", coach.id, "管理员设置机构会员卡底价", coach.member_id, op.id,
            before, Describe(await GetFloorsAsync(coachId)), coach.display_name));
        await db.SaveChangesAsync();
    }

    private static string Describe(List<CoachPriceFloor> floors) => string.Join("；", floors
        .OrderBy(f => f.biz_type).ThenBy(f => f.shop).ThenBy(f => f.tier)
        .Select(f => (f.biz_type + f.shop + f.tier) + ":" + (f.min_rate?.ToString("0.##") ?? "-") + "/" + (f.min_fixed?.ToString("0.##") ?? "-")));

    /// <summary>机构「制作会员卡」页的数据：当前设置 + 每一格的底价和是否开放。</summary>
    public async Task<object> GetMyStandardAsync(Coach coach, Introducer intro)
    {
        List<CoachPriceFloor> floors = await GetFloorsAsync(coach.id);
        List<CoachCarePrice> prices = await db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coach.id).ToListAsync();
        string? blocked = Blocked(coach, intro) ?? (await DistributorAccess.CanIssueAsync(db, coach) ? null : "你的分销商身份已停用或存在冲突，不能修改会员卡");
        var cells = new List<object>();
        foreach (string shop in CoachCardRules.CareShops)
            foreach (string tier in Tiers)
            {
                CoachCarePrice? p = prices.FirstOrDefault(x => x.shop == shop && x.tier == tier);
                CoachPriceFloor? f = CareFloor(floors, shop, tier);
                string mode = p?.mode ?? CoachCardRules.ModeNone;
                double value = p?.value ?? 0;
                cells.Add(new
                {
                    shop, tier, mode, value, open = CoachFloorRules.IsOpen(f), minRate = f?.min_rate, minFixed = f?.min_fixed,
                    // 管理员事后把底价调高时，机构原来的设置可能低于底价：标出来，保存前要改过来
                    belowFloor = CoachFloorRules.CheckCare(f, mode, value, "") != null,
                });
            }
        CoachPriceFloor? rf = RentalFloor(floors);
        return new
        {
            canEdit = blocked == null, reason = blocked ?? "", name = coach.display_name,
            validUntil = CoachCardRules.SeasonEnd(DateTime.Now, config),
            cells,
            rental = new { rate = coach.rental_rate, open = rf?.min_rate != null, minRate = rf?.min_rate, belowFloor = CoachFloorRules.CheckRental(rf, coach.rental_rate) != null },
        };
    }

    private static string? Blocked(Coach coach, Introducer intro)
    {
        if (intro.type != IntroducerRules.TypeOrg) return "只有机构可以自己制作会员卡，教练的会员卡价格由管理员设置";
        if (coach.valid != 1) return "你的分销商身份已停用，不能修改会员卡";
        return null;
    }

    /// <summary>机构保存自己的会员卡标准：逐格校验底价，通过后按现有规则保存（只影响之后领取的新卡）。写审计。</summary>
    public async Task SaveMyStandardAsync(Coach coach, Introducer intro, PricingInput input, int memberId)
    {
        string? blocked = Blocked(coach, intro) ?? (await DistributorAccess.CanIssueAsync(db, coach) ? null : "你的分销商身份已停用或存在冲突，不能修改会员卡");
        if (blocked != null) throw new CoachException(blocked, 3);
        List<CoachPriceFloor> floors = await GetFloorsAsync(coach.id);
        var problems = new List<string>();
        foreach (CarePriceInput p in input.care ?? new())
        {
            string shop = (p.shop ?? "").Trim(), tier = (p.tier ?? "").Trim();
            string? bad = CoachFloorRules.CheckCare(CareFloor(floors, shop, tier), p.mode, p.value, shop + " " + tier);
            if (bad != null) problems.Add(bad);
        }
        string? rentalBad = CoachFloorRules.CheckRental(RentalFloor(floors), input.rental_rate);
        if (rentalBad != null) problems.Add(rentalBad);
        if (problems.Count > 0) throw new CoachException(string.Join("；", problems));

        string before = Snapshot(await db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coach.id).ToListAsync(), coach.rental_rate);
        Coach saved = await new CoachAdminService(db, config).SetPricingAsync(coach.id, input);
        string after = Snapshot(await db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coach.id).ToListAsync(), saved.rental_rate);
        if (before != after)
        {
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("coach_care_price", "price", coach.id, "机构自助设置会员卡", memberId, null, before, after, intro.name));
            await db.SaveChangesAsync();
        }
    }

    private static string Snapshot(List<CoachCarePrice> prices, double? rentalRate) =>
        CoachCardRules.SerializeCarePrices(prices) + " | 租赁:" + (rentalRate?.ToString("0.##") ?? "无");
}

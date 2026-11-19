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

// 每个机构可维护多种卡。每次发卡创建独立分享卡，已领卡的权益跟随卡种更新。
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
        // 每个卡种有多少人领取过（有领取人的卡，含领取后被撤销的）和多少条还没人领取的分享：无人领取过的卡种可以删除（2026-11-18）
        var ids = rows.Select(t => t.id).ToList();
        var counts = await db.coachCard.AsNoTracking().Where(c => c.coach_id == coach.id && c.template_id != null && ids.Contains(c.template_id.Value))
            .GroupBy(c => c.template_id!.Value)
            .Select(g => new { id = g.Key, claimed = g.Count(c => c.member_id != null), pending = g.Count(c => c.status == CoachCard.StatusIssued) }).ToListAsync();
        return new { templates = rows.Select(t =>
        {
            var n = counts.FirstOrDefault(x => x.id == t.id);
            int claimed = n?.claimed ?? 0;
            return new { t.id, t.name, carePrices = CoachCardRules.ParseCarePrices(t.care_prices), rentalRate = t.rental_rate,
                sharePath = (string?)null, claimedCount = claimed, pendingCount = n?.pending ?? 0, canDelete = claimed == 0 };
        }).ToList() };
    }

    /// <summary>
    /// 删除卡种（2026-11-18 用户：会员卡如果无人领取过，应该可以删除）。只有从来没人领取过的卡种能删；
    /// 领取过的（含领取后被撤销的）不能删，已发出的卡需要继续关联该卡种。
    /// 删除 = 卡种 valid=0（软删除，留审计），同时把它还没人领取的分享一并撤销（这些记录对机构来说就是删了：二维码、小程序卡片立即失效）。
    /// </summary>
    public async Task<object> DeleteAsync(Coach coach, Introducer intro, int id)
    {
        await CheckOwner(coach, intro);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var t = await db.coachCardTemplate.AsTracking().FirstOrDefaultAsync(x => x.id == id && x.coach_id == coach.id && x.valid == 1)
            ?? throw new CoachException("没有找到这个卡种");
        int claimed = await db.coachCard.AsNoTracking().CountAsync(c => c.template_id == id && c.member_id != null);
        if (claimed > 0) throw new CoachException("这个卡种已经有 " + claimed + " 位顾客领取过，不能删除");
        DateTime now = DateTime.Now;
        int revoked = await db.coachCard.Where(c => c.template_id == id && c.coach_id == coach.id && c.status == CoachCard.StatusIssued)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.status, CoachCard.StatusRevoked)
                .SetProperty(c => c.revoked_at, (DateTime?)now).SetProperty(c => c.update_date, (DateTime?)now));
        string before = t.name + " | " + t.care_prices + " | 租赁:" + (t.rental_rate?.ToString("0.##") ?? "无");
        t.valid = 0; t.update_date = now;
        await db.SaveChangesAsync();
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("coach_card_template", "template", t.id, "机构删除会员卡种", intro.member_id, null, before,
            "已删除（同时撤销 " + revoked + " 条没人领取的分享）", intro.name));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new { id, revokedShares = revoked };
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

    private static List<CarePriceInput> Prices(CoachCardTemplate t) => CoachCardRules.ParseCarePrices(t.care_prices).Select(p => new CarePriceInput(p.shop, p.tier, p.mode, p.value)).ToList();

    // 生成和领取均检查底价；已领取卡计价使用当前卡种权益。
    public async Task<CoachCardTemplate> GetAvailableAsync(int id, int coachId)
    {
        var t = await db.coachCardTemplate.AsNoTracking().FirstOrDefaultAsync(x => x.id == id && x.coach_id == coachId && x.valid == 1)
            ?? throw new CoachException("卡种已失效，请联系分销商重新发卡");
        await ValidatePrices(coachId, Prices(t), t.rental_rate);
        return t;
    }

    // 旧版分享的是卡种固定链接，无法辨认每次转发，必须停用以避免多人领取。
    public Task<ShareCardView> ViewAsync(string? token, int? memberId, DateTime now) => Task.FromResult(new ShareCardView
    {
        state = ShareCardState.Invalid, message = "此旧分享链接已失效，请联系分销商重新发送单张卡"
    });
    public Task<CoachCard> ClaimAsync(string token, int memberId, DateTime now) =>
        Task.FromException<CoachCard>(new CoachException("此旧分享链接已失效，请联系分销商重新发送单张卡"));
}

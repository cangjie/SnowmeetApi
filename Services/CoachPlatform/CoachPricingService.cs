using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

/// <summary>开单选卡弹层里的一张学员卡，附带本单预计优惠。</summary>
public sealed class UsableCoachCard
{
    public int cardId { get; set; }
    public int coachId { get; set; }
    public string coachName { get; set; } = "";
    public string bizType { get; set; } = "";
    /// <summary>这张卡在本单的门店/档位（养护）或租赁上有没有优惠。没有时前端灰显不可选。</summary>
    public bool applicable { get; set; }
    public string mode { get; set; } = CoachCardRules.ModeNone;
    public double value { get; set; }
    public double estimatedDiscount { get; set; }
    public string benefitText { get; set; } = "";
    public DateTime? validUntil { get; set; }
}

/// <summary>养护开单时某张学员卡对某件装备的报价。</summary>
public sealed class CoachCareQuote
{
    public int cardId { get; set; }
    public int coachId { get; set; }
    public string coachName { get; set; } = "";
    public string mode { get; set; } = CoachCardRules.ModeNone;
    public double value { get; set; }
    /// <summary>单项/双项的基础价（不含加急加价），教练价只作用于它。</summary>
    public double baseAmount { get; set; }
    public double discount { get; set; }
}

/// <summary>
/// 学员卡的计价与使用记录。定价口径见 CoachCardRules；这里只负责：
/// 校验卡能不能用、按卡内快照报价、写使用记录（每笔只记一次）、取消时反向。
/// </summary>
public sealed class CoachPricingService(ApplicationDBContext db)
{
    /// <summary>卡对该会员现在是否可用：已领取、属于该会员、在有效期内。</summary>
    public async Task<CoachCard?> GetEffectiveCardAsync(int cardId, int memberId, DateTime now)
    {
        CoachCard? card = await db.coachCard.AsNoTracking().FirstOrDefaultAsync(c => c.id == cardId);
        return CoachCardRules.IsEffective(card, memberId, now) ? card : null;
    }

    /// <summary>
    /// 养护报价。shop 用开单门店，care 用来判断单项/双项；commonCharge 是该件装备当前的服务费（含加急），
    /// basePrice 是不含加急加价的单/双项价。没有对应那一格（门店不在价格设置里、没有计费项、该格为「无」）返回 discount 为 0 的报价。
    /// </summary>
    public async Task<CoachCareQuote> QuoteCareAsync(CoachCard card, string? shop, Care care, double commonCharge, double basePrice)
    {
        string coachName = await db.coach.AsNoTracking().Where(c => c.id == card.coach_id).Select(c => c.display_name).FirstOrDefaultAsync() ?? "";
        CoachCardRules.CarePriceCell? cell = CoachCardRules.FindCell(CoachCardRules.ParseCarePrices(card.care_price_snapshot),
            shop, CoachCardRules.ResolveTier(care));
        double bas = basePrice > 0 ? Math.Min(basePrice, commonCharge > 0 ? commonCharge : basePrice) : 0;
        CoachCareQuote quote = new() { cardId = card.id, coachId = card.coach_id, coachName = coachName, baseAmount = bas };
        if (cell != null)
        {
            quote.mode = cell.mode; quote.value = cell.value;
            // 优惠不超过本件服务费，不倒找钱
            quote.discount = Math.Min(CoachCardRules.CareDiscount(cell.mode, cell.value, bas), CoachCardRules.Money(Math.Max(commonCharge, 0)));
        }
        return quote;
    }

    /// <summary>租赁优惠：按卡内快照的折扣率。</summary>
    public static double QuoteRentalDiscount(CoachCard card, double amount) => CoachCardRules.RentalDiscount(card.rental_rate_snapshot, amount);

    /// <summary>
    /// 开单选卡弹层的数据：该会员所有可用学员卡。养护传 shop、care（算档位）、commonCharge/basePrice 给出预计优惠；
    /// 租赁传 rentalAmount 给出预计优惠。没有优惠的卡也返回（applicable=false），前端灰显。
    /// </summary>
    public async Task<List<UsableCoachCard>> ListUsableAsync(int memberId, string bizType, string? shop, Care? care,
        double commonCharge, double basePrice, double rentalAmount, DateTime now)
    {
        List<CoachCard> cards = await db.coachCard.AsNoTracking()
            .Where(c => c.member_id == memberId && c.status == CoachCard.StatusClaimed && c.end_date >= now && c.start_date <= now)
            .OrderBy(c => c.end_date).ThenBy(c => c.id).ToListAsync();
        List<int> coachIds = cards.Select(c => c.coach_id).Distinct().ToList();
        Dictionary<int, string> names = await db.coach.AsNoTracking().Where(c => coachIds.Contains(c.id))
            .ToDictionaryAsync(c => c.id, c => c.display_name);
        List<UsableCoachCard> list = new();
        foreach (CoachCard card in cards)
        {
            UsableCoachCard item = new()
            {
                cardId = card.id, coachId = card.coach_id, bizType = bizType, validUntil = card.end_date,
                coachName = names.TryGetValue(card.coach_id, out string? n) ? n : "",
            };
            if (bizType == CoachCardRules.BizRental)
            {
                item.mode = card.rental_rate_snapshot == null ? CoachCardRules.ModeNone : CoachCardRules.ModeRate;
                item.value = card.rental_rate_snapshot ?? 0;
                item.applicable = card.rental_rate_snapshot != null;
                item.estimatedDiscount = QuoteRentalDiscount(card, rentalAmount);
                item.benefitText = item.applicable ? "租赁 " + FormatRate(item.value) : "租赁无优惠";
            }
            else
            {
                CoachCareQuote q = care == null
                    ? new CoachCareQuote()
                    : await QuoteCareAsync(card, shop, care, commonCharge, basePrice);
                CoachCardRules.CarePriceCell? cell = care == null ? null
                    : CoachCardRules.FindCell(CoachCardRules.ParseCarePrices(card.care_price_snapshot), shop, CoachCardRules.ResolveTier(care));
                item.applicable = cell != null;
                item.mode = cell?.mode ?? CoachCardRules.ModeNone;
                item.value = cell?.value ?? 0;
                item.estimatedDiscount = q.discount;
                string where = CoachCardRules.ResolveCareShop(shop) ?? (shop ?? "");
                string tier = (care == null ? null : CoachCardRules.ResolveTier(care)) ?? "";
                item.benefitText = cell == null ? "本单" + where + tier + "无优惠" : where + " · " + tier + " " + FormatCell(cell);
            }
            list.Add(item);
        }
        return list;
    }

    public static string FormatRate(double rate) => (Math.Round(rate * 10, 2)).ToString("0.##") + " 折";

    public static string FormatCell(CoachCardRules.CarePriceCell cell) =>
        cell.mode == CoachCardRules.ModeRate ? FormatRate(cell.value) : "一口价 ¥" + cell.value.ToString("0.##");

    // ---------- 使用记录 ----------

    /// <summary>
    /// 记一笔使用（幂等：同一 biz_type + biz_id + sub_biz_id 已有记录就跳过，支付回调重放安全）。
    /// 返回 true = 本次新记了一条。
    /// </summary>
    public async Task<bool> RecordUseAsync(CoachCard card, int? orderId, string bizType, int bizId, int subBizId,
        string mode, double modeValue, double baseAmount, double discountAmount, DateTime now)
    {
        CoachCardUse? use = await NewUseAsync(card, orderId, bizType, bizId, subBizId, mode, modeValue, baseAmount, discountAmount, now);
        if (use == null) return false;
        db.coachCardUse.Add(use);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// 构造一条使用记录但不保存（调用方并入自己的事务一起 SaveChanges）。该笔已有记录返回 null。
    /// </summary>
    public async Task<CoachCardUse?> NewUseAsync(CoachCard card, int? orderId, string bizType, int bizId, int subBizId,
        string mode, double modeValue, double baseAmount, double discountAmount, DateTime now)
    {
        bool exists = await db.coachCardUse.AsNoTracking()
            .AnyAsync(u => u.biz_type == bizType && u.biz_id == bizId && u.sub_biz_id == subBizId);
        if (exists) return null;
        return new CoachCardUse
        {
            card_id = card.id, coach_id = card.coach_id, member_id = card.member_id ?? 0, order_id = orderId,
            biz_type = bizType, biz_id = bizId, sub_biz_id = subBizId, mode = mode, mode_value = modeValue,
            base_amount = CoachCardRules.Money(baseAmount), discount_amount = CoachCardRules.Money(discountAmount),
            status = CoachCardUse.StatusEffective, create_date = now,
        };
    }

    /// <summary>
    /// 反向：把某笔业务（养护单件 / 租赁某天）的使用记录置为 reversed，例如取消养护。幂等。
    /// subBizId 为空 = 该业务下全部记录（租赁整单取消）。返回被反向的条数。
    /// </summary>
    public async Task<int> ReverseUseAsync(string bizType, int bizId, int? subBizId, string reason, DateTime now)
    {
        return await db.coachCardUse
            .Where(u => u.biz_type == bizType && u.biz_id == bizId && (subBizId == null || u.sub_biz_id == subBizId)
                && u.status == CoachCardUse.StatusEffective)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.status, CoachCardUse.StatusReversed)
                .SetProperty(u => u.reverse_date, (DateTime?)now)
                .SetProperty(u => u.reverse_reason, reason));
    }
}

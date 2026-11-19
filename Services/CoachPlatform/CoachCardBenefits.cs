using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

/// <summary>卡种权益更新后已领卡跟随最新设置；历史无卡种的卡保留领取快照。</summary>
public sealed class CoachCardBenefits(ApplicationDBContext db)
{
    public async Task ApplyCurrentAsync(CoachCard card) => await ApplyCurrentAsync(new[] { card });

    public async Task ApplyCurrentAsync(IEnumerable<CoachCard> cards)
    {
        var eligible = cards.Where(c => c.status == CoachCard.StatusClaimed && c.template_id != null).ToList();
        var ids = eligible.Select(c => c.template_id!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var templates = await db.coachCardTemplate.AsNoTracking().Where(t => ids.Contains(t.id))
            .Select(t => new { t.id, t.coach_id, t.care_prices, t.rental_rate }).ToListAsync();
        var byId = templates.ToDictionary(t => t.id);
        foreach (var card in eligible)
        {
            if (!byId.TryGetValue(card.template_id!.Value, out var template) || template.coach_id != card.coach_id) continue;
            // 所有调用方都传 AsNoTracking 的卡，不写回领取快照；历史追溯仍可读取原记录。
            card.care_price_snapshot = template.care_prices;
            card.rental_rate_snapshot = template.rental_rate;
        }
    }
}

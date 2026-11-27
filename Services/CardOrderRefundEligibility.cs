using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

/// <summary>
/// 判断订单售出的卡是否均已无有效使用。实际退款必须在持有订单锁和卡锁时重新调用。
/// </summary>
public static class CardOrderRefundEligibility
{
    public sealed record Result(bool CanRefund, string? Reason, int[] CardIds);

    public static async Task<Result> EvaluateAsync(ApplicationDBContext db, int orderId)
    {
        var saleRows = await db.retail.AsNoTracking()
            .Where(r => r.order_id == orderId && r.valid == 1 && (r.punch_card_id != null
                || db.product.Any(p => p.id == r.product_id && db.category.Any(c => c.code == p.category_code
                    && (c.name == "次卡" || c.name == "季卡")))))
            .Select(r => new { r.id, r.punch_card_id }).ToListAsync();

        if (saleRows.Count == 0)
            return new Result(false, "未找到订单售卡记录", Array.Empty<int>());
        if (saleRows.Any(r => r.punch_card_id == null))
            return new Result(false, "订单售卡记录不完整，请联系管理员核对", Array.Empty<int>());

        int[] cardIds = saleRows.Select(r => r.punch_card_id!.Value).OrderBy(id => id).ToArray();
        if (cardIds.Distinct().Count() != cardIds.Length)
            return new Result(false, "订单售卡记录重复，请联系管理员核对", cardIds);

        var cards = await db.punchCard.AsNoTracking().Where(c => cardIds.Contains(c.id))
            .Select(c => new { c.id, c.source_retail_id, c.punches, c.is_refund, c.refund_pending }).ToListAsync();
        if (cards.Count != cardIds.Length || saleRows.Any(r => !cards.Any(c =>
                c.id == r.punch_card_id && c.source_retail_id == r.id)))
            return new Result(false, "订单售卡与会员卡不一致，请联系管理员核对", cardIds);
        if (cards.Any(c => c.is_refund))
            return new Result(false, "订单中的卡已退款，请联系管理员核对", cardIds);
        if (cards.Any(c => c.refund_pending))
            return new Result(false, "订单中的卡正在退款处理中", cardIds);
        if (cards.Any(c => (c.punches ?? 0) != 0)
            || await db.punchCardUsed.AsNoTracking().AnyAsync(u => cardIds.Contains(u.card_id) && u.valid))
            return new Result(false, "订单中的卡仍有使用记录，请先取消并返还次数", cardIds);

        return new Result(true, null, cardIds);
    }
}

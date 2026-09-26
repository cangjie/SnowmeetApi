using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Fnb;

// 用量预警（2026-09-26）：可用量（未开封 + 已开封 + 散装 + 自制，不含过期、已报损、已处理）降到预警线及以下时提醒。
// 预警线默认 = 最近一次入库或制作的数量 × 10%；每种食材可改比例（Ratio），或直接填数量（FixedQuantity，基本单位）。
// LastBatchQuantity 为空表示没有入库或制作记录（如只有盘盈），按比例算不出预警线，不提醒
public sealed record LowStockRow(int ItemId, string ItemName, int CategoryId, string BaseUnitCode, string DefaultInputUnitCode,
    decimal AvailableQuantity, decimal? LastBatchQuantity, decimal? Ratio, decimal? FixedQuantity, decimal? Threshold, bool Low);

public static class FnbLowStockRules
{
    public const decimal DefaultRatio = 0.1m;

    // 填了数量就按数量；否则按比例 × 最近一次入库或制作的数量，没有这个数量就算不出（null）
    public static decimal? Threshold(decimal? ratio, decimal? fixedQuantity, decimal? lastBatchQuantity)
    {
        if (fixedQuantity.HasValue) return fixedQuantity.Value;
        if (lastBatchQuantity is not > 0) return null;
        return Math.Round((ratio ?? DefaultRatio) * lastBatchQuantity.Value, 6, MidpointRounding.AwayFromZero);
    }

    public static bool IsLow(decimal available, decimal? threshold) => threshold.HasValue && available <= threshold.Value;

    // 设置校验：比例、数量只能填一个；都不填 = 恢复默认 10%
    public static string? Validate(decimal? ratio, decimal? fixedQuantity)
    {
        if (ratio.HasValue && fixedQuantity.HasValue) return "比例和数量只能填一个";
        if (ratio.HasValue && (ratio.Value <= 0 || ratio.Value > 1 || ratio.Value != decimal.Round(ratio.Value, 4)))
            return "预警比例须在 0.01% 到 100% 之间";
        if (fixedQuantity.HasValue && (fixedQuantity.Value < 0 || fixedQuantity.Value >= 1_000_000_000_000m
            || fixedQuantity.Value != decimal.Round(fixedQuantity.Value, 6)))
            return "预警数量须为不小于 0 的数";
        return null;
    }
}

public sealed class FnbLowStockService(ApplicationDBContext db)
{
    // 本店有可用量、或入库 / 制作过的有效食材；预警的排在前面
    public async Task<List<LowStockRow>> ListAsync(int shopId)
    {
        DateTime today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var available = await (from s in db.fnbMaterialBatchStock.AsNoTracking()
                               join b in db.fnbMaterialBatch.AsNoTracking() on s.batch_id equals b.id
                               where s.shop_id == shopId && s.quantity > 0 && !s.is_destroyed && b.valid && b.dispose_status == null
                                   && b.expire_date >= today
                               group s by s.item_id into g
                               select new { ItemId = g.Key, Quantity = g.Sum(x => x.quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity);
        // 最近一次入库或制作：入库单、制作单的入库行（direction = 1），行号越大越新
        var lastLineIds = await (from l in db.fnbStockDocumentLine.AsNoTracking()
                                 join d in db.fnbStockDocument.AsNoTracking() on l.document_id equals d.id
                                 where d.shop_id == shopId && d.status == "posted" && l.direction == 1
                                     && (d.document_type == "receipt" || d.document_type == "prep")
                                 group l by l.item_id into g
                                 select g.Max(x => x.id)).ToListAsync();
        var lastBatch = await db.fnbStockDocumentLine.AsNoTracking().Where(l => lastLineIds.Contains(l.id))
            .ToDictionaryAsync(l => l.item_id, l => l.actual_qty);
        var ids = available.Keys.Union(lastBatch.Keys).ToList();
        var items = await db.fnbMaterialItem.AsNoTracking().Where(i => ids.Contains(i.id) && i.valid).ToListAsync();
        return items.Select(i =>
        {
            decimal qty = available.GetValueOrDefault(i.id);
            decimal? last = lastBatch.TryGetValue(i.id, out decimal v) ? v : null;
            decimal? threshold = FnbLowStockRules.Threshold(i.low_stock_ratio, i.low_stock_qty, last);
            return new LowStockRow(i.id, i.name, i.category_id, i.base_unit_code, i.default_input_unit_code, qty, last,
                i.low_stock_ratio, i.low_stock_qty, threshold, FnbLowStockRules.IsLow(qty, threshold));
        }).OrderByDescending(r => r.Low).ThenBy(r => r.ItemId).ToList();
    }
}

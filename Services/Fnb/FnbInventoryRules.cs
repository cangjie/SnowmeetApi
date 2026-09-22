using System;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Services.Fnb;

public sealed record AvailableBatch(
    int BatchId, DateOnly ExpireDate, string StockForm, decimal Quantity,
    decimal StockAmount, bool IsValid, bool IsDestroyed, DateTime ReceivedAtUtc = default);

public sealed record AllocationLine(int BatchId, decimal Quantity, decimal Amount);

public sealed record StockAllocation(IReadOnlyList<AllocationLine> Lines, decimal ActualQuantity, decimal ShortageQuantity);

public static class FnbInventoryRules
{
    public static DateOnly CalculateExpiry(DateOnly productionDate, int value, string unit)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return unit switch
        {
            "day" => productionDate.AddDays(value),
            "month" => productionDate.AddMonths(value),
            _ => throw new ArgumentException("保质期单位只能是 day 或 month", nameof(unit))
        };
    }

    public static DateOnly OpenedExpiry(DateOnly originalExpiry, DateOnly openedDate, int openShelfLifeDays)
    {
        if (openShelfLifeDays < 0) throw new ArgumentOutOfRangeException(nameof(openShelfLifeDays));
        DateOnly openedExpiry = openedDate.AddDays(openShelfLifeDays);
        return originalExpiry <= openedExpiry ? originalExpiry : openedExpiry;
    }

    public static StockAllocation AllocateFefo(IEnumerable<AvailableBatch> batches, decimal requestedQuantity, DateOnly businessDate)
    {
        if (requestedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        var lines = new List<AllocationLine>();
        decimal remaining = requestedQuantity;
        foreach (AvailableBatch batch in batches
            .Where(b => b.IsValid && !b.IsDestroyed && b.Quantity > 0 && b.ExpireDate >= businessDate
                && b.StockForm is "bulk" or "opened" or "prepared")
            .OrderBy(b => b.ExpireDate).ThenBy(b => b.ReceivedAtUtc).ThenBy(b => b.BatchId))
        {
            if (remaining == 0) break;
            decimal used = Math.Min(remaining, batch.Quantity);
            decimal amount = used == batch.Quantity
                ? batch.StockAmount
                : Math.Round(batch.StockAmount * used / batch.Quantity, 6, MidpointRounding.AwayFromZero);
            lines.Add(new AllocationLine(batch.BatchId, used, amount));
            remaining -= used;
        }
        return new StockAllocation(lines, requestedQuantity - remaining, remaining);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SnowmeetApi.Services.Fnb;

public sealed record StocktakeBatch(int BatchId, string StockForm, decimal Quantity, decimal Amount,
    DateOnly ExpireDate, bool Valid, bool Destroyed, string? DisposeStatus, byte[] RowVersion);

public static class FnbStocktakeRules
{
    public static byte[] Fingerprint(int itemId, DateOnly businessDate, IEnumerable<StocktakeBatch> batches)
    {
        var text = new StringBuilder().Append(itemId).Append('|').Append(businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var batch in batches.OrderBy(x => x.BatchId))
            text.Append('\n').Append(batch.BatchId).Append('|').Append(batch.StockForm).Append('|')
                .Append(batch.Quantity.ToString("0.000000", CultureInfo.InvariantCulture)).Append('|')
                .Append(batch.Amount.ToString("0.000000", CultureInfo.InvariantCulture)).Append('|')
                .Append(batch.ExpireDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
                .Append(batch.Valid ? '1' : '0').Append(batch.Destroyed ? '1' : '0').Append('|')
                .Append(batch.DisposeStatus ?? "").Append('|').Append(Convert.ToHexString(batch.RowVersion));
        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }
}

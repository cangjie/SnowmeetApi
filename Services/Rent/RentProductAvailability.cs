using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Rent;

public class RentProductAvailability
{
    private readonly ApplicationDBContext db;
    public RentProductAvailability(ApplicationDBContext db) => this.db = db;

    public static bool IsActive(DateTime? start, DateTime? end, DateTime now) =>
        (!start.HasValue || start.Value <= now) && (!end.HasValue || now < end.Value);

    public async Task<HashSet<int>> BlockedIdsAsync(IEnumerable<int> ids, DateTime now)
    {
        var productIds = ids.Distinct().ToArray();
        if (productIds.Length == 0) return new HashSet<int>();
        return (await db.rentProductUnavailability.AsNoTracking()
            .Where(x => x.valid == 1 && productIds.Contains(x.rent_product_id)
                && (x.start_time == null || x.start_time <= now)
                && (x.end_time == null || now < x.end_time))
            .Select(x => x.rent_product_id).Distinct().ToListAsync()).ToHashSet();
    }

    // 只改变返回值；库存基础状态仍由发放和归还维护。
    public async Task ApplyAsync(IEnumerable<RentProduct> products, DateTime now)
    {
        var list = products.ToList();
        var blocked = await BlockedIdsAsync(list.Select(p => p.id), now);
        foreach (var product in list)
            if ((string.IsNullOrWhiteSpace(product.status) || product.status.Trim() == "正常") && blocked.Contains(product.id))
                product.status = "不可用";
    }

    public async Task<bool> IsBlockedAsync(int? id, string? barcode, DateTime now)
    {
        var ids = new List<int>();
        if (id.HasValue) ids.Add(id.Value);
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var barcodeId = await db.rentProduct.AsNoTracking().Where(p => p.valid == 1 && p.barcode.Trim() == barcode.Trim())
                .OrderByDescending(p => p.id).Select(p => (int?)p.id).FirstOrDefaultAsync();
            if (barcodeId.HasValue) ids.Add(barcodeId.Value);
        }
        return (await BlockedIdsAsync(ids, now)).Count > 0;
    }
}

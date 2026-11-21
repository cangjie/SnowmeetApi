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
        var blocked = (await db.rentProductUnavailability.AsNoTracking()
            .Where(x => x.valid == 1 && productIds.Contains(x.rent_product_id)
                && (x.start_time == null || x.start_time <= now)
                && (x.end_time == null || now < x.end_time))
            .Select(x => x.rent_product_id).Distinct().ToListAsync()).ToHashSet();
        var trials = await db.trialReservation.AsNoTracking()
            .Where(x => productIds.Contains(x.rent_product_id) &&
                (x.status == TrialReservation.InUse ||
                 (x.status == TrialReservation.Pending && x.reservation_date == now.Date)))
            .Select(x => x.rent_product_id).Distinct().ToListAsync();
        blocked.UnionWith(trials);
        return blocked;
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

    public async Task<bool> IsUnavailableForNewRentalAsync(int? id, string? barcode, DateTime now)
    {
        var code = barcode?.Trim();
        var product = id.HasValue
            ? await db.rentProduct.AsNoTracking().FirstOrDefaultAsync(p => p.id == id.Value)
            : string.IsNullOrEmpty(code) ? null
            : await db.rentProduct.AsNoTracking()
                .Where(p => p.valid == 1 && p.barcode != null && p.barcode.Trim() == code)
                .OrderByDescending(p => p.id).FirstOrDefaultAsync();
        if (id.HasValue && (product == null || product.valid != 1)) return true;
        if (product != null && !string.IsNullOrWhiteSpace(product.status) && product.status.Trim() != "正常")
            return true;
        return await IsBlockedAsync(id, code, now);
    }

    public async Task<bool> IsBlockedForOpenRentalAsync(int? id, string? barcode, DateTime start, DateTime now)
    {
        var ids = new List<int>();
        if (id != null) ids.Add(id.Value);
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var matches = await db.rentProduct.AsNoTracking().Where(p => p.valid == 1 && p.barcode != null && p.barcode.Trim() == barcode.Trim())
                .Select(p => p.id).ToListAsync();
            ids.AddRange(matches);
        }
        if (ids.Count == 0) return false;
        return await db.trialReservation.AsNoTracking().AnyAsync(r => ids.Contains(r.rent_product_id)
            && (r.status == TrialReservation.InUse || (r.status == TrialReservation.Pending
                && r.reservation_date >= start.Date && r.reservation_date >= now.Date)));
    }
}

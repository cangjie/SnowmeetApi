using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record CountInput(int ShopId, long DocumentId, int ItemId, decimal CountedQuantity, string RowVersion);
public sealed record GainInput(int ItemId, string BatchNo, DateOnly ExpireDate, string StorageType,
    decimal UnitCost, IReadOnlyList<int> ImageIds, string? ExpiryNote);
public sealed record PostStocktakeInput(int ShopId, long DocumentId, IReadOnlyList<GainInput> Gains);
public sealed record StocktakePreview(int ItemId, string ItemName, decimal SystemQuantity,
    decimal? CountedQuantity, decimal? DifferenceQuantity, bool SnapshotChanged);

public sealed class FnbStocktakeService(ApplicationDBContext db)
{
    private static DateTime BusinessDate(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;

    private async Task<(decimal Quantity, byte[] Fingerprint, long? LastMovementId,
        List<FnbMaterialBatchStock> Stocks, Dictionary<int, FnbMaterialBatch> Batches)> SnapshotDataAsync(
        int shopId, int itemId, DateOnly date)
    {
        var stocks = await db.fnbMaterialBatchStock.AsTracking().Where(x => x.shop_id == shopId && x.item_id == itemId).ToListAsync();
        int[] batchIds = stocks.Select(x => x.batch_id).ToArray();
        var batches = await db.fnbMaterialBatch.AsTracking().Where(x => batchIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        var snapshots = stocks.Select(x =>
        {
            var b = batches[x.batch_id];
            return new StocktakeBatch(x.batch_id, x.stock_form, x.quantity, x.stock_amount,
                DateOnly.FromDateTime(b.expire_date), b.valid, x.is_destroyed, b.dispose_status, x.row_version);
        }).ToList();
        decimal available = snapshots.Where(x => x.StockForm != "sealed" && x.Valid && !x.Destroyed &&
            x.DisposeStatus == null && x.ExpireDate >= date).Sum(x => x.Quantity);
        long? lastMovement = await db.fnbStockMovement.Where(x => x.shop_id == shopId && x.item_id == itemId)
            .MaxAsync(x => (long?)x.id);
        return (available, FnbStocktakeRules.Fingerprint(itemId, date, snapshots), lastMovement, stocks, batches);
    }

    public async Task<long> CreateSnapshotAsync(int shopId, Guid requestId, IReadOnlyList<int> itemIds, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, shopId, true)) throw new UnauthorizedAccessException();
        if (requestId == Guid.Empty || itemIds == null || itemIds.Count == 0 || itemIds.Any(x => x <= 0) ||
            itemIds.Distinct().Count() != itemIds.Count) throw new ArgumentException("盘点食材或请求号无效");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.document_type == "stocktake" && x.request_id == requestId);
        if (previous != null) { await tx.CommitAsync(); return previous.id; }
        if (await db.fnbMaterialItem.CountAsync(x => itemIds.Contains(x.id) && x.valid) != itemIds.Count)
            throw new ArgumentException("盘点食材不存在或已停用");
        DateTime now = DateTime.UtcNow;
        DateTime businessDate = BusinessDate(now);
        var document = new FnbStockDocument
        {
            shop_id = shopId, document_no = "T" + now.ToString("yyyyMMddHHmmssfff") + requestId.ToString("N")[..8],
            document_type = "stocktake", status = "draft", request_id = requestId,
            source_client = actor.SourceClient, business_date = businessDate,
            occurred_at = now, created_at = now, created_by_staff_id = actor.Staff.id
        };
        db.fnbStockDocument.Add(document);
        await db.SaveChangesAsync();
        foreach (int itemId in itemIds)
        {
            var snapshot = await SnapshotDataAsync(shopId, itemId, DateOnly.FromDateTime(businessDate));
            db.fnbStocktakeLine.Add(new FnbStocktakeLine
            {
                document_id = document.id, shop_id = shopId, item_id = itemId, system_qty = snapshot.Quantity,
                snapshot_at = now, snapshot_last_movement_id = snapshot.LastMovementId,
                snapshot_fingerprint = snapshot.Fingerprint
            });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return document.id;
    }

    public async Task<FnbStocktakeLine> SaveCountAsync(CountInput input, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) throw new UnauthorizedAccessException();
        if (input.CountedQuantity < 0 || input.CountedQuantity >= 1_000_000_000_000m ||
            input.CountedQuantity != decimal.Round(input.CountedQuantity, 6))
            throw new ArgumentException("实盘数无效");
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.id == input.DocumentId && x.shop_id == input.ShopId && x.document_type == "stocktake" && x.status == "draft");
        if (document == null) throw new ArgumentException("盘点单不存在或已过账");
        var row = await db.fnbStocktakeLine.AsTracking().FirstOrDefaultAsync(x => x.document_id == input.DocumentId && x.shop_id == input.ShopId && x.item_id == input.ItemId);
        if (row == null) throw new ArgumentException("盘点食材不存在");
        if (string.IsNullOrWhiteSpace(input.RowVersion) || Convert.ToBase64String(row.row_version) != input.RowVersion)
            throw new InvalidOperationException("盘点行已被修改，请刷新");
        row.counted_qty = input.CountedQuantity; row.counted_at = DateTime.UtcNow;
        row.counted_by_staff_id = actor.Staff.id;
        await db.SaveChangesAsync();
        return row;
    }

    public async Task<IReadOnlyList<StocktakePreview>> PreviewAsync(int shopId, long documentId)
    {
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.id == documentId && x.shop_id == shopId && x.document_type == "stocktake");
        if (document == null) throw new ArgumentException("盘点单不存在");
        var rows = await db.fnbStocktakeLine.AsNoTracking().Where(x => x.document_id == documentId && x.shop_id == shopId).ToListAsync();
        var result = new List<StocktakePreview>();
        foreach (var row in rows)
        {
            var item = await db.fnbMaterialItem.AsNoTracking().FirstAsync(x => x.id == row.item_id);
            var current = await SnapshotDataAsync(shopId, row.item_id, DateOnly.FromDateTime(BusinessDate(DateTime.UtcNow)));
            result.Add(new StocktakePreview(row.item_id, item.name, row.system_qty, row.counted_qty,
                row.counted_qty - row.system_qty, !current.Fingerprint.SequenceEqual(row.snapshot_fingerprint)));
        }
        return result;
    }

    public async Task<long> PostAsync(PostStocktakeInput input, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, true)) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var document = await db.fnbStockDocument.AsTracking().FirstOrDefaultAsync(x => x.id == input.DocumentId && x.shop_id == input.ShopId && x.document_type == "stocktake");
        if (document == null) throw new ArgumentException("盘点单不存在");
        if (document.status == "posted") { await tx.CommitAsync(); return document.id; }
        if (document.status != "draft") throw new ArgumentException("盘点单不可过账");
        var rows = await db.fnbStocktakeLine.AsTracking().Where(x => x.document_id == document.id && x.shop_id == input.ShopId).ToListAsync();
        if (rows.Count == 0 || rows.Any(x => x.counted_qty == null)) throw new ArgumentException("仍有未填写的实盘数");
        var gains = input.Gains ?? Array.Empty<GainInput>();
        if (gains.Select(x => x.ItemId).Distinct().Count() != gains.Count) throw new ArgumentException("盘盈食材重复");
        DateTime now = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(BusinessDate(now));
        var snapshotData = new Dictionary<int, (decimal Quantity, byte[] Fingerprint, long? LastMovementId,
            List<FnbMaterialBatchStock> Stocks, Dictionary<int, FnbMaterialBatch> Batches)>();
        foreach (var row in rows)
        {
            var current = await SnapshotDataAsync(input.ShopId, row.item_id, today);
            if (!current.Fingerprint.SequenceEqual(row.snapshot_fingerprint))
                throw new InvalidOperationException("盘点期间库存或营业日期已变化，请重新建立快照");
            snapshotData.Add(row.item_id, current);
        }
        int lineNo = 1;
        foreach (var row in rows)
        {
            decimal difference = row.counted_qty!.Value - row.system_qty;
            if (difference == 0) continue;
            var item = await db.fnbMaterialItem.AsNoTracking().FirstAsync(x => x.id == row.item_id);
            var unit = await db.fnbUnit.AsNoTracking().FirstAsync(x => x.code == item.base_unit_code);
            decimal quantity = Math.Abs(difference);
            decimal amount;
            int? gainBatchId = null;
            StockAllocation? lossAllocation = null;
            if (difference > 0)
            {
                var gain = gains.FirstOrDefault(x => x.ItemId == row.item_id);
                if (gain == null || gain.UnitCost < 0 || gain.UnitCost != decimal.Round(gain.UnitCost, 6) ||
                    gain.ExpireDate < today || gain.StorageType is not ("ambient" or "chilled" or "frozen") ||
                    string.IsNullOrWhiteSpace(gain.BatchNo) || !FnbText.FitsChineseVarchar(gain.BatchNo, 50) ||
                    gain.ImageIds == null || gain.ImageIds.Count == 0 || gain.ImageIds.Distinct().Count() != gain.ImageIds.Count ||
                    !FnbText.FitsChineseVarchar(gain.ExpiryNote, 1000) ||
                    await db.UploadFile.CountAsync(x => gain.ImageIds.Contains(x.id) && x.purpose == "食材批次") != gain.ImageIds.Distinct().Count())
                    throw new ArgumentException("盘盈须确认批次照片、效期、储存方式和成本");
                amount = Math.Round(quantity * gain.UnitCost, 6, MidpointRounding.AwayFromZero);
                var batch = new FnbMaterialBatch
                {
                    name = item.name, batch_no = gain.BatchNo.Trim(), expire_date = gain.ExpireDate.ToDateTime(TimeOnly.MinValue),
                    warn_days = 3, image_ids = string.Join(",", gain.ImageIds), create_userid = actor.AuditUserId,
                    staff_id = actor.Staff.id, valid = true, create_date = DateTime.Now
                };
                db.fnbMaterialBatch.Add(batch);
                await db.SaveChangesAsync();
                gainBatchId = batch.id;
                db.fnbMaterialBatchStock.Add(new FnbMaterialBatchStock
                {
                    batch_id = batch.id, shop_id = input.ShopId, item_id = item.id,
                    stock_form = "bulk", storage_type = gain.StorageType, quantity = quantity, stock_amount = amount,
                    original_expire_date = batch.expire_date, expiry_source = "manual", expiry_note = gain.ExpiryNote,
                    received_at = now
                });
                await db.SaveChangesAsync();
            }
            else
            {
                var current = snapshotData[row.item_id];
                var candidates = current.Stocks.Select(x =>
                {
                    var batch = current.Batches[x.batch_id];
                    return new AvailableBatch(x.batch_id, DateOnly.FromDateTime(batch.expire_date), x.stock_form,
                        x.quantity, x.stock_amount, batch.valid && batch.dispose_status == null, x.is_destroyed, x.received_at);
                });
                lossAllocation = FnbInventoryRules.AllocateFefo(candidates, quantity, today);
                if (lossAllocation.ShortageQuantity != 0) throw new InvalidOperationException("盘亏数量超过可用批次库存");
                amount = lossAllocation.Lines.Sum(x => x.Amount);
                var stockMap = current.Stocks.ToDictionary(x => x.batch_id);
                foreach (var allocation in lossAllocation.Lines)
                {
                    var stock = stockMap[allocation.BatchId];
                    stock.quantity -= allocation.Quantity; stock.stock_amount -= allocation.Amount; stock.updated_at = now;
                    if (stock.quantity == 0)
                    {
                        var batch = current.Batches[allocation.BatchId];
                        batch.dispose_status = "用完"; batch.dispose_userid = actor.AuditUserId;
                        batch.dispose_date = DateTime.Now; batch.update_date = DateTime.Now;
                    }
                }
            }
            var line = new FnbStockDocumentLine
            {
                document_id = document.id, shop_id = input.ShopId, line_no = lineNo++, item_id = item.id,
                item_name = item.name, direction = difference > 0 ? (short)1 : (short)-1,
                input_qty = quantity, input_unit_name = unit.name, input_to_base = 1,
                actual_qty = quantity, actual_amount = amount, specified_batch_id = gainBatchId
            };
            db.fnbStockDocumentLine.Add(line);
            await db.SaveChangesAsync();
            row.adjustment_line_id = line.id;
            if (gainBatchId != null)
                db.fnbStockMovement.Add(new FnbStockMovement
                {
                    document_line_id = line.id, shop_id = input.ShopId, item_id = item.id,
                    batch_id = gainBatchId.Value, direction = 1, quantity = quantity, amount = amount,
                    balance_qty = quantity, balance_amount = amount, created_at = now
                });
            else if (lossAllocation != null)
            {
                var stockMap = snapshotData[row.item_id].Stocks.ToDictionary(x => x.batch_id);
                foreach (var allocation in lossAllocation.Lines)
                {
                    var stock = stockMap[allocation.BatchId];
                    db.fnbStockMovement.Add(new FnbStockMovement
                    {
                        document_line_id = line.id, shop_id = input.ShopId, item_id = item.id,
                        batch_id = allocation.BatchId, direction = -1, quantity = allocation.Quantity,
                        amount = allocation.Amount, balance_qty = stock.quantity,
                        balance_amount = stock.stock_amount, created_at = now
                    });
                }
            }
            await db.SaveChangesAsync();
        }
        document.status = "posted"; document.posted_at = now; document.posted_by_staff_id = actor.Staff.id;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return document.id;
    }
}

using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record ReceiptResult(long DocumentId, int BatchId, decimal Quantity, decimal Amount, bool Replayed);

/// <summary>Creates the legacy expiry row and all stock accounting rows atomically.</summary>
public sealed class FnbReceiptService(ApplicationDBContext db)
{
    public async Task<ReceiptResult> PostAsync(ReceiptInput input, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) throw new UnauthorizedAccessException("无门店权限");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.fnbStockDocument.AsNoTracking()
            .FirstOrDefaultAsync(x => x.shop_id == input.ShopId && x.document_type == "receipt" && x.request_id == input.RequestId);
        if (previous != null)
        {
            var previousLine = await db.fnbStockDocumentLine.AsNoTracking().FirstAsync(x => x.document_id == previous.id);
            await tx.CommitAsync();
            return new ReceiptResult(previous.id, previousLine.specified_batch_id!.Value, previousLine.actual_qty, previousLine.actual_amount, true);
        }
        var item = await db.fnbMaterialItem.AsNoTracking().FirstOrDefaultAsync(x => x.id == input.ItemId);
        if (item == null) throw new ArgumentException("食材不存在");
        if (!await db.fnbMaterialCategory.AnyAsync(x => x.id == item.category_id && x.level == 2 && x.valid))
            throw new ArgumentException("食材分类已停用");
        var inputUnit = await db.fnbUnit.AsNoTracking().FirstOrDefaultAsync(x => x.code == input.InputUnitCode);
        var baseUnit = await db.fnbUnit.AsNoTracking().FirstOrDefaultAsync(x => x.code == item.base_unit_code);
        if (inputUnit == null || baseUnit == null) throw new ArgumentException("计量单位不存在");
        var plan = FnbReceiptRules.Plan(input, item, inputUnit, baseUnit);
        // expiry_source 的取值 "category" 沿用建表时的约束，2026-09-24 起含义为「按食材保质期规则计算」
        if (input.ExpirySource == "category")
        {
            var rule = await db.fnbShelfLifeRule.AsNoTracking().FirstOrDefaultAsync(x => x.id == input.ShelfLifeRuleId && x.valid
                && x.item_id == item.id && x.storage_type == input.StorageType);
            if (rule == null || input.ProductionDate == null || rule.production_month != input.ProductionDate.Value.Month ||
                input.ShelfLifeValue != rule.shelf_life_value || input.ShelfLifeUnit != rule.shelf_life_unit || plan.CalculatedExpiry != input.ExpireDate)
                throw new ArgumentException("食材保质期规则与批次数据不一致");
        }
        else if (input.ShelfLifeRuleId != null) throw new ArgumentException("手动效期不能绑定保质期规则");
        var imageIds = input.ImageIds ?? Array.Empty<int>();
        int photoCount = imageIds.Count == 0 ? 0 : await db.UploadFile.CountAsync(x => imageIds.Contains(x.id) && x.purpose == "食材批次");
        if (photoCount != imageIds.Count) throw new ArgumentException("批次照片不存在或用途不符");

        DateTime now = DateTime.UtcNow;
        DateTime businessDate = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var batch = new FnbMaterialBatch
        {
            name = item.name, batch_no = input.BatchNo.Trim(),
            produce_date = input.ProductionDate?.ToDateTime(TimeOnly.MinValue),
            shelf_life_value = input.ShelfLifeValue,
            shelf_life_unit = input.ShelfLifeUnit == "day" ? "天" : input.ShelfLifeUnit == "month" ? "月" : null,
            expire_date = input.ExpireDate.ToDateTime(TimeOnly.MinValue), warn_days = input.WarnDays,
            image_ids = imageIds.Count == 0 ? null : string.Join(",", imageIds), create_userid = actor.AuditUserId,
            staff_id = actor.Staff.id, create_date = DateTime.Now, valid = true
        };
        db.fnbMaterialBatch.Add(batch);
        await db.SaveChangesAsync();

        var stock = new FnbMaterialBatchStock
        {
            batch_id = batch.id, shop_id = input.ShopId, item_id = item.id,
            stock_form = input.StockForm, storage_type = input.StorageType,
            storage_location = input.StorageLocation, quantity = plan.BaseQuantity, stock_amount = plan.Amount,
            pack_size = input.PackSize, pack_unit_name = input.PackUnitName,
            original_expire_date = batch.expire_date, open_storage_type = input.OpenStorageType,
            open_shelf_life_days = input.OpenShelfLifeDays, shelf_life_rule_id = input.ShelfLifeRuleId,
            calculated_expire_date = plan.CalculatedExpiry?.ToDateTime(TimeOnly.MinValue),
            expiry_source = input.ExpirySource, expiry_note = input.ExpiryNote, received_at = now
        };
        db.fnbMaterialBatchStock.Add(stock);

        var document = new FnbStockDocument
        {
            shop_id = input.ShopId, document_no = "RK" + now.ToString("yyyyMMddHHmmssfff") + input.RequestId.ToString("N")[..8],
            document_type = "receipt", status = "posted", request_id = input.RequestId,
            source_client = actor.SourceClient, business_date = businessDate, occurred_at = now,
            created_by_staff_id = actor.Staff.id, posted_by_staff_id = actor.Staff.id,
            created_at = now, posted_at = now,
            remark = input.UnitPrice == 0 ? "零成本入库" : null
        };
        db.fnbStockDocument.Add(document);
        await db.SaveChangesAsync();
        var line = new FnbStockDocumentLine
        {
            document_id = document.id, shop_id = input.ShopId, line_no = 1, item_id = item.id,
            item_name = item.name, direction = 1, input_qty = input.Quantity,
            input_unit_name = input.StockForm == "sealed" ? input.PackUnitName! : inputUnit.name,
            input_to_base = plan.InputToBase, actual_qty = plan.BaseQuantity,
            input_unit_price = input.UnitPrice, actual_amount = plan.Amount, specified_batch_id = batch.id
        };
        db.fnbStockDocumentLine.Add(line);
        await db.SaveChangesAsync();
        db.fnbStockMovement.Add(new FnbStockMovement
        {
            document_line_id = line.id, shop_id = input.ShopId, item_id = item.id, batch_id = batch.id,
            direction = 1, quantity = plan.BaseQuantity, amount = plan.Amount,
            balance_qty = plan.BaseQuantity, balance_amount = plan.Amount, created_at = now
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new ReceiptResult(document.id, batch.id, plan.BaseQuantity, plan.Amount, false);
    }

    /// <summary>入库后可删除的时限：给误触、录错留的撤回窗口。</summary>
    public static readonly TimeSpan DeleteWindow = TimeSpan.FromMinutes(10);

    private sealed record ReceiptRows(FnbMaterialBatch Batch, FnbMaterialBatchStock Stock, FnbStockDocument Document,
        FnbStockDocumentLine Line, FnbStockMovement Movement);

    /// <summary>入库 10 分钟内、批次还没有开封、使用、报损、盘点等任何后续操作时，本人或店长可删除。
    /// 删除是物理删除本次入库写入的批次、库存、单据和流水，就像没入过库；自动建档的食材保留。</summary>
    public async Task DeleteAsync(int shopId, int batchId, FnbAccess.Actor actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var (rows, error) = await LoadDeletableAsync(shopId, batchId, actor.Staff, DateTime.UtcNow);
        if (rows == null) throw new ArgumentException(error);
        db.fnbStockMovement.Remove(rows.Movement);
        await db.SaveChangesAsync();
        db.fnbStockDocumentLine.Remove(rows.Line);
        await db.SaveChangesAsync();
        db.fnbStockDocument.Remove(rows.Document);
        db.fnbMaterialBatchStock.Remove(rows.Stock);
        await db.SaveChangesAsync();
        db.fnbMaterialBatch.Remove(rows.Batch);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>还能删除的秒数；不能删除（超时、已有后续操作、无权）时为 null。</summary>
    public async Task<int?> DeleteSecondsLeftAsync(int shopId, int batchId, Staff staff)
    {
        DateTime now = DateTime.UtcNow;
        var (rows, _) = await LoadDeletableAsync(shopId, batchId, staff, now);
        return rows == null ? null : (int)(rows.Document.posted_at!.Value + DeleteWindow - now).TotalSeconds;
    }

    private async Task<(ReceiptRows? Rows, string? Error)> LoadDeletableAsync(int shopId, int batchId, Staff staff, DateTime now)
    {
        const string used = "该批次已有开封、使用等后续操作，不能删除";
        var stock = await db.fnbMaterialBatchStock.AsTracking().FirstOrDefaultAsync(x => x.batch_id == batchId && x.shop_id == shopId);
        if (stock == null) return (null, "批次不存在");
        var line = await db.fnbStockDocumentLine.AsTracking().FirstOrDefaultAsync(x => x.specified_batch_id == batchId && x.shop_id == shopId
            && x.direction == 1 && db.fnbStockDocument.Any(d => d.id == x.document_id && d.document_type == "receipt"));
        if (line == null) return (null, "只能删除入库的批次");
        var document = await db.fnbStockDocument.AsTracking().FirstAsync(x => x.id == line.document_id);
        if (document.status != "posted" || document.posted_at == null || now - document.posted_at.Value > DeleteWindow)
            return (null, "入库已超过 10 分钟，不能删除");
        if (document.posted_by_staff_id != staff.id && staff.title_level < 200) return (null, "只能删除自己的入库，或请店长删除");
        var movements = await db.fnbStockMovement.AsTracking().Where(x => x.batch_id == batchId).ToListAsync();
        if (movements.Count != 1 || movements[0].document_line_id != line.id || stock.is_destroyed || stock.quantity != line.actual_qty)
            return (null, used);
        if (await db.fnbStockDocumentLine.CountAsync(x => x.document_id == document.id) != 1
            || await db.fnbStockDocumentLine.AnyAsync(x => x.specified_batch_id == batchId && x.id != line.id)
            || await db.fnbMaterialBatchStock.AnyAsync(x => x.parent_batch_id == batchId)
            || await db.fnbStocktakeLine.AnyAsync(x => x.snapshot_last_movement_id == movements[0].id))
            return (null, used);
        var batch = await db.fnbMaterialBatch.AsTracking().FirstAsync(x => x.id == batchId);
        if (!batch.valid || batch.dispose_status != null) return (null, used);
        return (new ReceiptRows(batch, stock, document, line, movements[0]), null);
    }
}

using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record OpenInput(int ShopId, Guid RequestId, int ParentBatchId, int PackCount, string? StorageLocation);
public sealed record WasteInput(int ShopId, Guid RequestId, int BatchId, decimal Quantity, string ReasonCode, string? Remark);
public sealed record StockPostResult(long DocumentId, int BatchId, decimal Quantity, decimal Amount, bool Replayed);

public sealed class FnbStockPostingService(ApplicationDBContext db)
{
    private static DateTime BusinessDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;

    private static FnbStockDocument Document(int shopId, Guid requestId, string type, FnbAccess.Actor actor, DateTime now) => new()
    {
        shop_id = shopId, document_no = type[..1].ToUpperInvariant() + now.ToString("yyyyMMddHHmmssfff") + requestId.ToString("N")[..8],
        document_type = type, status = "posted", request_id = requestId, source_client = actor.SourceClient,
        business_date = BusinessDate(now), occurred_at = now, created_at = now, posted_at = now,
        created_by_staff_id = actor.Staff.id, posted_by_staff_id = actor.Staff.id
    };

    /// <summary>未开封批次能否开封（件数够不够另判）：有效、未处置、未销毁、未过期，且有包装规格与开封后保质期。</summary>
    internal static bool CanOpen(FnbMaterialBatchStock stock, FnbMaterialBatch batch, DateTime businessDate) =>
        stock.stock_form == "sealed" && !stock.is_destroyed && batch.valid && batch.dispose_status == null &&
        batch.expire_date.Date >= businessDate && stock.pack_size is > 0 && stock.open_shelf_life_days is >= 0 &&
        stock.open_storage_type != null;

    public async Task<StockPostResult> PostOpenAsync(OpenInput input, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) throw new UnauthorizedAccessException();
        if (input.RequestId == Guid.Empty || input.PackCount <= 0) throw new ArgumentException("开封件数或请求号无效");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == input.ShopId && x.document_type == "open" && x.request_id == input.RequestId);
        if (previous != null)
        {
            var line = await db.fnbStockDocumentLine.AsNoTracking().FirstAsync(x => x.document_id == previous.id && x.direction == 1);
            await tx.CommitAsync();
            return new StockPostResult(previous.id, line.specified_batch_id!.Value, line.actual_qty, line.actual_amount, true);
        }
        var parent = await db.fnbMaterialBatchStock.AsTracking().FirstOrDefaultAsync(x => x.batch_id == input.ParentBatchId && x.shop_id == input.ShopId);
        var parentBatch = await db.fnbMaterialBatch.AsTracking().FirstOrDefaultAsync(x => x.id == input.ParentBatchId);
        DateTime now = DateTime.UtcNow;
        DateOnly openDate = DateOnly.FromDateTime(BusinessDate(now));
        if (parent == null || parentBatch == null || !CanOpen(parent, parentBatch, BusinessDate(now)))
            throw new ArgumentException("未开封批次不可开封");
        decimal quantity = input.PackCount * parent.pack_size.Value;
        if (quantity > parent.quantity) throw new InvalidOperationException("未开封库存不足");
        decimal amount = quantity == parent.quantity ? parent.stock_amount :
            Math.Round(parent.stock_amount * quantity / parent.quantity, 6, MidpointRounding.AwayFromZero);
        var expiryByOpening = openDate.AddDays(parent.open_shelf_life_days.Value);
        var expiry = FnbInventoryRules.OpenedExpiry(DateOnly.FromDateTime(parent.original_expire_date), openDate, parent.open_shelf_life_days.Value);
        var child = await db.fnbMaterialBatchStock.AsTracking().FirstOrDefaultAsync(x => x.parent_batch_id == parent.batch_id &&
            x.opened_date == openDate.ToDateTime(TimeOnly.MinValue) && x.storage_type == parent.open_storage_type && !x.is_destroyed);
        FnbMaterialBatch? childBatch = null;
        if (child != null)
        {
            childBatch = await db.fnbMaterialBatch.AsTracking().FirstAsync(x => x.id == child.batch_id);
            if (child.item_id != parent.item_id || child.shop_id != parent.shop_id || childBatch.expire_date.Date != expiry.ToDateTime(TimeOnly.MinValue))
                throw new InvalidOperationException("同日开封批次数据冲突");
            child.quantity += quantity; child.stock_amount += amount; child.updated_at = now;
            childBatch.dispose_status = null; childBatch.dispose_date = null; childBatch.dispose_userid = null;
        }
        else
        {
            childBatch = new FnbMaterialBatch
            {
                name = parentBatch.name, batch_no = "O" + parent.batch_id + "-" + openDate.ToString("yyMMdd"),
                produce_date = parentBatch.produce_date, shelf_life_value = parent.open_shelf_life_days,
                shelf_life_unit = "天", expire_date = expiry.ToDateTime(TimeOnly.MinValue),
                warn_days = parentBatch.warn_days, image_ids = parentBatch.image_ids,
                create_userid = actor.AuditUserId, staff_id = actor.Staff.id, valid = true, create_date = DateTime.Now
            };
            db.fnbMaterialBatch.Add(childBatch);
            await db.SaveChangesAsync();
            child = new FnbMaterialBatchStock
            {
                batch_id = childBatch.id, shop_id = input.ShopId, item_id = parent.item_id,
                stock_form = "opened", storage_type = parent.open_storage_type,
                storage_location = input.StorageLocation ?? parent.storage_location,
                quantity = quantity, stock_amount = amount, parent_batch_id = parent.batch_id,
                opened_date = openDate.ToDateTime(TimeOnly.MinValue),
                original_expire_date = parent.original_expire_date,
                opened_expire_date = expiryByOpening.ToDateTime(TimeOnly.MinValue),
                open_storage_type = parent.open_storage_type, open_shelf_life_days = parent.open_shelf_life_days,
                expiry_source = "opened", received_at = now
            };
            db.fnbMaterialBatchStock.Add(child);
        }
        parent.quantity -= quantity; parent.stock_amount -= amount; parent.updated_at = now;
        if (parent.quantity == 0)
        {
            parentBatch.dispose_status = "用完"; parentBatch.dispose_userid = actor.AuditUserId;
            parentBatch.dispose_date = DateTime.Now; parentBatch.update_date = DateTime.Now;
        }
        await db.SaveChangesAsync();
        var document = Document(input.ShopId, input.RequestId, "open", actor, now);
        db.fnbStockDocument.Add(document);
        await db.SaveChangesAsync();
        var outLine = new FnbStockDocumentLine
        {
            document_id = document.id, shop_id = input.ShopId, line_no = 1, item_id = parent.item_id,
            item_name = parentBatch.name, direction = -1, input_qty = input.PackCount,
            input_unit_name = parent.pack_unit_name!, input_to_base = parent.pack_size.Value,
            actual_qty = quantity, actual_amount = amount, specified_batch_id = parent.batch_id
        };
        var inLine = new FnbStockDocumentLine
        {
            document_id = document.id, shop_id = input.ShopId, line_no = 2, item_id = parent.item_id,
            item_name = parentBatch.name, direction = 1, input_qty = input.PackCount,
            input_unit_name = parent.pack_unit_name!, input_to_base = parent.pack_size.Value,
            actual_qty = quantity, actual_amount = amount, specified_batch_id = child.batch_id
        };
        db.fnbStockDocumentLine.AddRange(outLine, inLine);
        await db.SaveChangesAsync();
        db.fnbStockMovement.AddRange(
            new FnbStockMovement { document_line_id = outLine.id, shop_id = input.ShopId, item_id = parent.item_id,
                batch_id = parent.batch_id, direction = -1, quantity = quantity, amount = amount,
                balance_qty = parent.quantity, balance_amount = parent.stock_amount, created_at = now },
            new FnbStockMovement { document_line_id = inLine.id, shop_id = input.ShopId, item_id = parent.item_id,
                batch_id = child.batch_id, direction = 1, quantity = quantity, amount = amount,
                balance_qty = child.quantity, balance_amount = child.stock_amount, created_at = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new StockPostResult(document.id, child.batch_id, quantity, amount, false);
    }

    public async Task<StockPostResult> PostWasteAsync(WasteInput input, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, true)) throw new UnauthorizedAccessException();
        if (input.RequestId == Guid.Empty || input.Quantity <= 0 || input.Quantity != decimal.Round(input.Quantity, 6) ||
            input.ReasonCode is not ("expiry" or "near_expiry" or "damage" or "other")) throw new ArgumentException("报损参数无效");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == input.ShopId && x.document_type == "waste" && x.request_id == input.RequestId);
        if (previous != null)
        {
            var previousLine = await db.fnbStockDocumentLine.AsNoTracking().FirstAsync(x => x.document_id == previous.id);
            if (previousLine.specified_batch_id != input.BatchId || previousLine.actual_qty != input.Quantity ||
                previous.reason_code != input.ReasonCode || previous.remark != input.Remark)
                throw new InvalidOperationException("请求号已用于其他报损内容");
            await tx.CommitAsync();
            return new StockPostResult(previous.id, previousLine.specified_batch_id!.Value,
                previousLine.actual_qty, previousLine.actual_amount, true);
        }
        var stock = await db.fnbMaterialBatchStock.AsTracking().FirstOrDefaultAsync(x => x.batch_id == input.BatchId && x.shop_id == input.ShopId);
        var batch = await db.fnbMaterialBatch.AsTracking().FirstOrDefaultAsync(x => x.id == input.BatchId);
        if (stock == null || batch == null || stock.is_destroyed || !batch.valid || stock.quantity < input.Quantity ||
            (stock.stock_form == "sealed" && (stock.pack_size == null || input.Quantity % stock.pack_size.Value != 0)))
            throw new InvalidOperationException("报损批次或数量无效");
        decimal amount = input.Quantity == stock.quantity ? stock.stock_amount :
            Math.Round(stock.stock_amount * input.Quantity / stock.quantity, 6, MidpointRounding.AwayFromZero);
        stock.quantity -= input.Quantity; stock.stock_amount -= amount; stock.updated_at = DateTime.UtcNow;
        if (stock.quantity == 0)
        {
            stock.is_destroyed = true; batch.dispose_status = "报废"; batch.dispose_userid = actor.AuditUserId;
            batch.dispose_date = DateTime.Now; batch.update_date = DateTime.Now;
        }
        DateTime now = DateTime.UtcNow;
        var document = Document(input.ShopId, input.RequestId, "waste", actor, now);
        document.reason_code = input.ReasonCode; document.remark = input.Remark;
        db.fnbStockDocument.Add(document);
        await db.SaveChangesAsync();
        var item = await db.fnbMaterialItem.AsNoTracking().FirstAsync(x => x.id == stock.item_id);
        var unit = await db.fnbUnit.AsNoTracking().FirstAsync(x => x.code == item.base_unit_code);
        var line = new FnbStockDocumentLine
        {
            document_id = document.id, shop_id = input.ShopId, line_no = 1, item_id = item.id,
            item_name = item.name, direction = -1, input_qty = input.Quantity,
            input_unit_name = unit.name, input_to_base = 1, actual_qty = input.Quantity,
            actual_amount = amount, specified_batch_id = input.BatchId
        };
        db.fnbStockDocumentLine.Add(line);
        await db.SaveChangesAsync();
        db.fnbStockMovement.Add(new FnbStockMovement
        {
            document_line_id = line.id, shop_id = input.ShopId, item_id = item.id, batch_id = input.BatchId,
            direction = -1, quantity = input.Quantity, amount = amount,
            balance_qty = stock.quantity, balance_amount = stock.stock_amount, created_at = now
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new StockPostResult(document.id, input.BatchId, input.Quantity, amount, false);
    }
}

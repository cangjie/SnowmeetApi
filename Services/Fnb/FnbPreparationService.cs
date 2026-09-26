using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record PreparationInput(int ShopId, Guid RequestId, long RecipeId, decimal OutputQuantity,
    string BatchNo, string StorageType, string? StorageLocation, DateOnly ExpireDate, int WarnDays,
    IReadOnlyList<int> ImageIds, string? ExpiryNote);

public sealed class FnbPreparationService(ApplicationDBContext db)
{
    public async Task<StockPostResult> PostAsync(PreparationInput input, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) throw new UnauthorizedAccessException();
        // 产出照片选填（2026-09-26 起，同入库）；传了就须有效且不重复
        var imageIds = input.ImageIds ?? Array.Empty<int>();
        if (input.RequestId == Guid.Empty || input.OutputQuantity <= 0 || input.OutputQuantity != decimal.Round(input.OutputQuantity, 6) ||
            string.IsNullOrWhiteSpace(input.BatchNo) || !FnbText.FitsChineseVarchar(input.BatchNo, 50) ||
            input.StorageType is not ("ambient" or "chilled" or "frozen") || input.WarnDays < 0 ||
            !FnbText.FitsChineseVarchar(input.StorageLocation, 200) || !FnbText.FitsChineseVarchar(input.ExpiryNote, 1000) ||
            imageIds.Any(x => x <= 0) || imageIds.Distinct().Count() != imageIds.Count)
            throw new ArgumentException("半成品批次参数无效");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == input.ShopId && x.document_type == "prep" && x.request_id == input.RequestId);
        if (previous != null)
        {
            var line = await db.fnbStockDocumentLine.AsNoTracking().FirstAsync(x => x.document_id == previous.id && x.direction == 1);
            await tx.CommitAsync();
            return new StockPostResult(previous.id, line.specified_batch_id!.Value, line.actual_qty, line.actual_amount, true);
        }
        var recipe = await db.fnbRecipe.AsNoTracking().FirstOrDefaultAsync(x => x.id == input.RecipeId && x.shop_id == input.ShopId && x.recipe_type == "prep" && x.status == "published");
        if (recipe == null || recipe.output_item_id == null) throw new ArgumentException("当前未发布半成品配方");
        var outputItem = await db.fnbMaterialItem.AsNoTracking().FirstOrDefaultAsync(x => x.id == recipe.output_item_id && x.valid && x.item_type == "prepared");
        if (outputItem == null) throw new ArgumentException("半成品食材已停用");
        var lines = await db.fnbRecipeLine.AsNoTracking().Where(x => x.recipe_id == recipe.id).ToListAsync();
        if (lines.Count == 0) throw new ArgumentException("配方没有用料");
        int[] itemIds = lines.Select(x => x.item_id).ToArray();
        var itemMap = await db.fnbMaterialItem.AsNoTracking().Where(x => itemIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        if (itemMap.Count != lines.Count || itemMap.Values.Any(x => !x.valid)) throw new ArgumentException("配方含无效原料");
        if (imageIds.Count > 0 && await db.UploadFile.CountAsync(x => imageIds.Contains(x.id) && x.purpose == "食材批次") != imageIds.Count)
            throw new ArgumentException("批次照片不存在");
        DateTime now = DateTime.UtcNow;
        DateTime businessDate = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        if (input.ExpireDate < DateOnly.FromDateTime(businessDate)) throw new ArgumentException("半成品到期日不能早于制作日");
        var stockRows = await db.fnbMaterialBatchStock.AsTracking().Where(x => x.shop_id == input.ShopId && itemIds.Contains(x.item_id) && x.quantity > 0 && !x.is_destroyed).ToListAsync();
        int[] batchIds = stockRows.Select(x => x.batch_id).ToArray();
        var batchMap = await db.fnbMaterialBatch.AsTracking().Where(x => batchIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        var stockMap = stockRows.ToDictionary(x => x.batch_id);
        var allocations = new Dictionary<int, StockAllocation>();
        decimal totalCost = 0;
        foreach (var line in lines)
        {
            decimal needed = FnbRecipeRules.RequiredQuantity(line.quantity, recipe.output_qty, input.OutputQuantity);
            var candidates = stockRows.Where(x => x.item_id == line.item_id).Select(x =>
            {
                var batch = batchMap[x.batch_id];
                return new AvailableBatch(x.batch_id, DateOnly.FromDateTime(batch.expire_date), x.stock_form,
                    x.quantity, x.stock_amount, batch.valid && batch.dispose_status == null, x.is_destroyed, x.received_at);
            });
            var allocation = FnbInventoryRules.AllocateFefo(candidates, needed, DateOnly.FromDateTime(businessDate));
            if (allocation.ShortageQuantity > 0) throw new InvalidOperationException($"原料 {itemMap[line.item_id].name} 库存不足");
            allocations.Add(line.item_id, allocation);
            totalCost += allocation.Lines.Sum(x => x.Amount);
        }
        foreach (var allocation in allocations.Values.SelectMany(x => x.Lines))
        {
            var stock = stockMap[allocation.BatchId];
            stock.quantity -= allocation.Quantity; stock.stock_amount -= allocation.Amount; stock.updated_at = now;
            if (stock.quantity == 0)
            {
                var oldBatch = batchMap[allocation.BatchId];
                oldBatch.dispose_status = "用完"; oldBatch.dispose_userid = actor.AuditUserId;
                oldBatch.dispose_date = DateTime.Now; oldBatch.update_date = DateTime.Now;
            }
        }
        var outputBatch = new FnbMaterialBatch
        {
            name = outputItem.name, batch_no = input.BatchNo.Trim(), produce_date = businessDate,
            expire_date = input.ExpireDate.ToDateTime(TimeOnly.MinValue), warn_days = input.WarnDays,
            image_ids = imageIds.Count == 0 ? null : string.Join(",", imageIds), create_userid = actor.AuditUserId,
            staff_id = actor.Staff.id, valid = true, create_date = DateTime.Now
        };
        db.fnbMaterialBatch.Add(outputBatch);
        await db.SaveChangesAsync();
        db.fnbMaterialBatchStock.Add(new FnbMaterialBatchStock
        {
            batch_id = outputBatch.id, shop_id = input.ShopId, item_id = outputItem.id,
            stock_form = "prepared", storage_type = input.StorageType, storage_location = input.StorageLocation,
            quantity = input.OutputQuantity, stock_amount = totalCost,
            original_expire_date = outputBatch.expire_date, expiry_source = "manual",
            expiry_note = input.ExpiryNote, received_at = now
        });
        var document = new FnbStockDocument
        {
            shop_id = input.ShopId, document_no = "P" + now.ToString("yyyyMMddHHmmssfff") + input.RequestId.ToString("N")[..8],
            document_type = "prep", status = "posted", request_id = input.RequestId,
            source_client = actor.SourceClient, business_date = businessDate, occurred_at = now,
            recipe_id = recipe.id, created_by_staff_id = actor.Staff.id,
            posted_by_staff_id = actor.Staff.id, created_at = now, posted_at = now
        };
        db.fnbStockDocument.Add(document);
        await db.SaveChangesAsync();
        var documentLines = new List<FnbStockDocumentLine>();
        int lineNo = 1;
        foreach (var recipeLine in lines.OrderBy(x => x.sort))
        {
            var allocation = allocations[recipeLine.item_id];
            var item = itemMap[recipeLine.item_id];
            var unit = await db.fnbUnit.AsNoTracking().FirstAsync(x => x.code == item.base_unit_code);
            documentLines.Add(new FnbStockDocumentLine
            {
                document_id = document.id, shop_id = input.ShopId, line_no = lineNo++, item_id = item.id,
                item_name = item.name, direction = -1, input_qty = allocation.ActualQuantity,
                input_unit_name = unit.name, input_to_base = 1, actual_qty = allocation.ActualQuantity,
                actual_amount = allocation.Lines.Sum(x => x.Amount)
            });
        }
        var outputUnit = await db.fnbUnit.AsNoTracking().FirstAsync(x => x.code == outputItem.base_unit_code);
        documentLines.Add(new FnbStockDocumentLine
        {
            document_id = document.id, shop_id = input.ShopId, line_no = lineNo,
            item_id = outputItem.id, item_name = outputItem.name, direction = 1,
            input_qty = input.OutputQuantity, input_unit_name = outputUnit.name, input_to_base = 1,
            actual_qty = input.OutputQuantity, actual_amount = totalCost, specified_batch_id = outputBatch.id
        });
        db.fnbStockDocumentLine.AddRange(documentLines);
        await db.SaveChangesAsync();
        var movements = new List<FnbStockMovement>();
        foreach (var documentLine in documentLines.Where(x => x.direction == -1))
            foreach (var allocation in allocations[documentLine.item_id].Lines)
            {
                var stock = stockMap[allocation.BatchId];
                movements.Add(new FnbStockMovement
                {
                    document_line_id = documentLine.id, shop_id = input.ShopId, item_id = documentLine.item_id,
                    batch_id = allocation.BatchId, direction = -1, quantity = allocation.Quantity, amount = allocation.Amount,
                    balance_qty = stock.quantity, balance_amount = stock.stock_amount, created_at = now
                });
            }
        movements.Add(new FnbStockMovement
        {
            document_line_id = documentLines[^1].id, shop_id = input.ShopId, item_id = outputItem.id,
            batch_id = outputBatch.id, direction = 1, quantity = input.OutputQuantity, amount = totalCost,
            balance_qty = input.OutputQuantity, balance_amount = totalCost, created_at = now
        });
        db.fnbStockMovement.AddRange(movements);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new StockPostResult(document.id, outputBatch.id, input.OutputQuantity, totalCost, false);
    }
}

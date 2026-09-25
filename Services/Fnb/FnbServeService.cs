using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record ServeNeed(int ItemId, string ItemName, decimal PlannedQuantity, decimal ActualQuantity, decimal ShortageQuantity);
public sealed record ServeResult(long DocumentId, bool Replayed, IReadOnlyList<ServeNeed> Needs);

// 建单即扣料：一张厨房单一道菜（将来由外部订单自动导入），按该菜已发布配方 × 份数扣料
public sealed record KitchenOrderServeInput(int ShopId, Guid RequestId, string? TableNo, string? Remark,
    IReadOnlyList<ManualKitchenLineInput> Lines);
public sealed record KitchenOrderServeResult(long OrderId, string DisplayNo, long DocumentId, bool Replayed, IReadOnlyList<ServeNeed> Needs);
// 扣料 10 分钟内编辑：换菜、份数、桌号、备注
public sealed record KitchenOrderUpdateInput(int ShopId, long OrderId, string? TableNo, string? Remark,
    IReadOnlyList<ManualKitchenLineInput> Lines);

public sealed class FnbServeService(ApplicationDBContext db)
{
    /// <summary>出餐扣料后可编辑、删除（回滚配料）的时限，与入库删除一致；从第一次扣料算起，编辑不顺延。</summary>
    public static readonly TimeSpan ChangeWindow = FnbReceiptService.DeleteWindow;

    private async Task<(FnbOrder Order, List<FnbOrderLine> Lines, Dictionary<int, decimal> Needs)> DemandAsync(int shopId, long orderId)
    {
        var order = await db.fnbOrder.AsNoTracking().FirstOrDefaultAsync(x => x.id == orderId && x.shop_id == shopId);
        if (order == null || order.review_status != "verified" || order.order_status == "cancelled")
            throw new ArgumentException("订单不存在、已取消或尚未核对");
        var lines = await db.fnbOrderLine.AsTracking().Where(x => x.order_id == orderId && x.shop_id == shopId).ToListAsync();
        if (lines.Count == 0 || lines.All(x => !x.is_inventory_line || x.cancelled_qty >= x.quantity))
            throw new ArgumentException("订单没有待出餐菜品");
        var needs = new Dictionary<int, decimal>();
        foreach (var line in lines.Where(x => x.is_inventory_line && x.cancelled_qty < x.quantity))
        {
            if (line.dish_spec_id == null) throw new ArgumentException("存在未匹配的菜品规格");
            var recipe = await db.fnbRecipe.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId &&
                x.dish_spec_id == line.dish_spec_id && x.recipe_type == "dish" && x.status == "published");
            if (recipe == null) throw new ArgumentException($"菜品 {line.item_name} 没有已发布配方");
            var ingredients = await db.fnbRecipeLine.AsNoTracking().Where(x => x.recipe_id == recipe.id).ToListAsync();
            if (ingredients.Count == 0) throw new ArgumentException($"菜品 {line.item_name} 配方为空");
            line.recipe_id = recipe.id;
            foreach (var ingredient in ingredients)
            {
                decimal needed = FnbRecipeRules.RequiredQuantity(ingredient.quantity, recipe.output_qty, line.quantity - line.cancelled_qty);
                needs[ingredient.item_id] = needs.GetValueOrDefault(ingredient.item_id) + needed;
            }
        }
        return (order, lines, needs);
    }

    private async Task<(List<FnbMaterialBatchStock> Stocks, Dictionary<int, FnbMaterialBatch> Batches,
        Dictionary<int, StockAllocation> Allocations, Dictionary<int, FnbMaterialItem> Items)> AllocateAsync(
        int shopId, Dictionary<int, decimal> needs, DateOnly businessDate)
    {
        int[] itemIds = needs.Keys.ToArray();
        var items = await db.fnbMaterialItem.AsNoTracking().Where(x => itemIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        if (items.Count != needs.Count) throw new ArgumentException("配方食材不存在");
        var stocks = await db.fnbMaterialBatchStock.AsTracking().Where(x => x.shop_id == shopId && itemIds.Contains(x.item_id)
            && x.quantity > 0 && !x.is_destroyed).ToListAsync();
        int[] batchIds = stocks.Select(x => x.batch_id).ToArray();
        var batches = await db.fnbMaterialBatch.AsTracking().Where(x => batchIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        var allocations = new Dictionary<int, StockAllocation>();
        foreach (var pair in needs)
        {
            var candidates = stocks.Where(x => x.item_id == pair.Key).Select(x =>
            {
                var batch = batches[x.batch_id];
                return new AvailableBatch(x.batch_id, DateOnly.FromDateTime(batch.expire_date), x.stock_form,
                    x.quantity, x.stock_amount, batch.valid && batch.dispose_status == null, x.is_destroyed, x.received_at);
            });
            allocations[pair.Key] = FnbInventoryRules.AllocateFefo(candidates, pair.Value, businessDate);
        }
        return (stocks, batches, allocations, items);
    }

    public async Task<IReadOnlyList<ServeNeed>> PreviewAsync(int shopId, long orderId)
    {
        var (_, _, needs) = await DemandAsync(shopId, orderId);
        DateTime now = DateTime.UtcNow;
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")));
        var (_, _, allocations, items) = await AllocateAsync(shopId, needs, businessDate);
        return needs.Select(x => new ServeNeed(x.Key, items[x.Key].name, x.Value,
            allocations[x.Key].ActualQuantity, allocations[x.Key].ShortageQuantity)).ToList();
    }

    public async Task<ServeResult> PostAsync(int shopId, long orderId, Guid requestId, FnbAccess.Actor actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await PostCoreAsync(shopId, orderId, requestId, actor);
        await tx.CommitAsync();
        return result;
    }

    private static void RequireOneDish(IReadOnlyList<ManualKitchenLineInput>? lines)
    {
        if (lines == null || lines.Count != 1) throw new ArgumentException("一张厨房单只能有一道菜");
    }

    // 建单时菜品须已有发布配方才能扣料；没有就整单回滚，不留下待核对的单
    private async Task RequireRecipeAsync(long orderId, string reviewStatus)
    {
        if (reviewStatus == "verified") return;
        string name = await db.fnbOrderLine.Where(x => x.order_id == orderId).Select(x => x.item_name).FirstAsync();
        throw new ArgumentException($"「{name}」还没有已发布的配方，先到「配方」里配好用料再建单");
    }

    /// <summary>建厨房单并按菜品已发布配方立即扣料，同一事务；同一请求号重试返回首次结果。</summary>
    public async Task<KitchenOrderServeResult> CreateAndServeAsync(KitchenOrderServeInput input, FnbAccess.Actor actor)
    {
        RequireOneDish(input.Lines);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var created = await new FnbManualOrderService(db).CreateCoreAsync(new ManualKitchenOrderInput(input.ShopId, input.RequestId,
            null, input.TableNo, input.Remark, input.Lines), actor, verified: false);
        await RequireRecipeAsync(created.OrderId, created.ReviewStatus);
        var served = await PostCoreAsync(input.ShopId, created.OrderId, input.RequestId, actor);
        await tx.CommitAsync();
        return new KitchenOrderServeResult(created.OrderId, created.DisplayNo, served.DocumentId, created.Replayed, served.Needs);
    }

    private async Task<ServeResult> PostCoreAsync(int shopId, long orderId, Guid requestId, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, shopId, false)) throw new UnauthorizedAccessException();
        if (requestId == Guid.Empty) throw new ArgumentException("请求号必填");
        var previous = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.document_type == "serve" && x.request_id == requestId);
        if (previous != null)
        {
            if (previous.order_id != orderId) throw new InvalidOperationException("请求号已用于其他订单");
            var oldLines = await db.fnbStockDocumentLine.AsNoTracking().Where(x => x.document_id == previous.id).ToListAsync();
            return new ServeResult(previous.id, true, oldLines.Select(x => new ServeNeed(x.item_id, x.item_name,
                x.planned_qty, x.actual_qty, x.shortage_qty)).ToList());
        }
        if (await db.fnbStockDocument.AnyAsync(x => x.order_id == orderId && x.status == "posted"))
            throw new InvalidOperationException("订单已出餐，不可重复扣料");
        var (_, _, needs) = await DemandAsync(shopId, orderId);
        DateTime now = DateTime.UtcNow;
        DateTime businessDate = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var document = new FnbStockDocument
        {
            shop_id = shopId, document_no = "S" + now.ToString("yyyyMMddHHmmssfff") + requestId.ToString("N")[..8],
            document_type = "serve", status = "posted", request_id = requestId,
            source_client = actor.SourceClient, business_date = businessDate, occurred_at = now,
            order_id = orderId, created_by_staff_id = actor.Staff.id,
            posted_by_staff_id = actor.Staff.id, created_at = now, posted_at = now
        };
        db.fnbStockDocument.Add(document);
        await db.SaveChangesAsync();
        var result = await DeductAsync(document, needs, actor, now);
        return new ServeResult(document.id, false, result);
    }

    /// <summary>按用量 FEFO 扣料，写出餐单据行与流水（单据已存在）；库存不足按实际可用量扣并记欠料。</summary>
    private async Task<IReadOnlyList<ServeNeed>> DeductAsync(FnbStockDocument document, Dictionary<int, decimal> needs,
        FnbAccess.Actor actor, DateTime now)
    {
        int shopId = document.shop_id;
        var (stocks, batches, allocations, items) = await AllocateAsync(shopId, needs, DateOnly.FromDateTime(document.business_date));
        var stockMap = stocks.ToDictionary(x => x.batch_id);
        foreach (var allocation in allocations.Values.SelectMany(x => x.Lines))
        {
            var stock = stockMap[allocation.BatchId];
            stock.quantity -= allocation.Quantity; stock.stock_amount -= allocation.Amount; stock.updated_at = now;
            if (stock.quantity == 0)
            {
                var batch = batches[allocation.BatchId];
                batch.dispose_status = "用完"; batch.dispose_userid = actor.AuditUserId;
                batch.dispose_date = DateTime.Now; batch.update_date = DateTime.Now;
            }
        }
        await db.SaveChangesAsync();
        var documentLines = new List<FnbStockDocumentLine>();
        int lineNo = 1;
        foreach (var pair in needs.OrderBy(x => x.Key))
        {
            var item = items[pair.Key];
            var unit = await db.fnbUnit.AsNoTracking().FirstAsync(x => x.code == item.base_unit_code);
            var allocation = allocations[pair.Key];
            documentLines.Add(new FnbStockDocumentLine
            {
                document_id = document.id, shop_id = shopId, line_no = lineNo++, item_id = item.id,
                item_name = item.name, direction = -1, input_qty = pair.Value, input_unit_name = unit.name,
                input_to_base = 1, actual_qty = allocation.ActualQuantity,
                actual_amount = allocation.Lines.Sum(x => x.Amount)
            });
        }
        db.fnbStockDocumentLine.AddRange(documentLines);
        await db.SaveChangesAsync();
        foreach (var line in documentLines)
            foreach (var allocation in allocations[line.item_id].Lines)
            {
                var stock = stockMap[allocation.BatchId];
                db.fnbStockMovement.Add(new FnbStockMovement
                {
                    document_line_id = line.id, shop_id = shopId, item_id = line.item_id,
                    batch_id = allocation.BatchId, direction = -1, quantity = allocation.Quantity,
                    amount = allocation.Amount, balance_qty = stock.quantity, balance_amount = stock.stock_amount,
                    created_at = now
                });
            }
        await db.SaveChangesAsync();
        return needs.Select(x => new ServeNeed(x.Key, items[x.Key].name,
            x.Value, allocations[x.Key].ActualQuantity, allocations[x.Key].ShortageQuantity)).ToList();
    }

    /// <summary>已扣配料（出餐单据行），厨房单详情展示用；未出餐时为空。</summary>
    public async Task<IReadOnlyList<ServeNeed>> ServedNeedsAsync(int shopId, long orderId)
    {
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.order_id == orderId &&
            x.document_type == "serve" && x.status == "posted");
        if (document == null) return [];
        return await db.fnbStockDocumentLine.AsNoTracking().Where(x => x.document_id == document.id).OrderBy(x => x.line_no)
            .Select(x => new ServeNeed(x.item_id, x.item_name, x.planned_qty, x.actual_qty, x.shortage_qty)).ToListAsync();
    }

    private sealed record ServedRows(FnbOrder Order, FnbStockDocument Document, List<FnbStockDocumentLine> Lines,
        List<FnbStockMovement> Movements, Dictionary<int, FnbMaterialBatchStock> Stocks, Dictionary<int, FnbMaterialBatch> Batches);

    /// <summary>扣掉的配料原样退回当时的批次（被扣光标为「用完」的恢复），同批次之后流水的结存补回；删除这次扣料的流水与单据行，单据保留。</summary>
    private async Task ReverseAsync(ServedRows rows, DateTime now)
    {
        foreach (var m in rows.Movements)
        {
            var stock = rows.Stocks[m.batch_id];
            stock.quantity += m.quantity; stock.stock_amount += m.amount; stock.updated_at = now;
            var batch = rows.Batches[m.batch_id];
            if (batch.dispose_status == "用完")
            {
                batch.dispose_status = null; batch.dispose_date = null; batch.dispose_userid = null; batch.update_date = DateTime.Now;
            }
            // 之后同批次流水的结存都少算了这一笔，补回去保持台账连续
            await db.fnbStockMovement.Where(x => x.batch_id == m.batch_id && x.id > m.id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.balance_qty, x => x.balance_qty + m.quantity)
                    .SetProperty(x => x.balance_amount, x => x.balance_amount + m.amount));
        }
        db.fnbStockMovement.RemoveRange(rows.Movements);
        await db.SaveChangesAsync();
        db.fnbStockDocumentLine.RemoveRange(rows.Lines);
        await db.SaveChangesAsync();
    }

    /// <summary>扣料 10 分钟内，本人或店长可删除手动厨房单：配料退回后删除扣料单据与厨房单。</summary>
    public async Task DeleteAsync(int shopId, long orderId, FnbAccess.Actor actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        DateTime now = DateTime.UtcNow;
        var (rows, error) = await LoadDeletableAsync(shopId, orderId, actor.Staff, now);
        if (rows == null) throw new ArgumentException(error);
        await ReverseAsync(rows, now);
        db.fnbStockDocument.Remove(rows.Document);
        await db.SaveChangesAsync();
        await db.fnbOrderLine.Where(x => x.order_id == orderId && x.shop_id == shopId).ExecuteDeleteAsync();
        await db.fnbOrderImport.Where(x => x.order_id == orderId && x.shop_id == shopId).ExecuteDeleteAsync();
        db.fnbOrder.Remove(rows.Order);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>扣料 10 分钟内，本人或店长可编辑手动厨房单：先退回原配料，再换菜品、份数、桌号、备注，按新菜品配方重新扣料。
    /// 沿用原扣料单据，可编辑时限仍从第一次扣料算起。</summary>
    public async Task<KitchenOrderServeResult> UpdateAsync(KitchenOrderUpdateInput input, FnbAccess.Actor actor)
    {
        RequireOneDish(input.Lines);
        FnbManualOrderService.ValidateOrder(input.Lines, input.TableNo, input.Remark);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        DateTime now = DateTime.UtcNow;
        var (rows, error) = await LoadDeletableAsync(input.ShopId, input.OrderId, actor.Staff, now);
        if (rows == null) throw new ArgumentException(error);
        await ReverseAsync(rows, now);
        var (lines, allPublished) = await new FnbManualOrderService(db).BuildLinesAsync(input.ShopId, input.Lines);
        if (!allPublished) throw new ArgumentException($"「{lines[0].item_name}」还没有已发布的配方，先到「配方」里配好用料");
        await db.fnbOrderLine.Where(x => x.order_id == input.OrderId && x.shop_id == input.ShopId).ExecuteDeleteAsync();
        lines.ForEach(x => x.order_id = input.OrderId);
        db.fnbOrderLine.AddRange(lines);
        rows.Order.table_no = input.TableNo?.Trim();
        rows.Order.remark = input.Remark;
        rows.Order.updated_at = now;
        await db.SaveChangesAsync();
        var (_, _, needs) = await DemandAsync(input.ShopId, input.OrderId);
        var served = await DeductAsync(rows.Document, needs, actor, now);
        await tx.CommitAsync();
        return new KitchenOrderServeResult(rows.Order.id, rows.Order.display_no, rows.Document.id, false, served);
    }

    /// <summary>还能编辑、删除的秒数；不能时为 null。</summary>
    public async Task<int?> ChangeSecondsLeftAsync(int shopId, long orderId, Staff staff)
    {
        DateTime now = DateTime.UtcNow;
        var (rows, _) = await LoadDeletableAsync(shopId, orderId, staff, now);
        return rows == null ? null : (int)(rows.Document.posted_at!.Value + ChangeWindow - now).TotalSeconds;
    }

    private async Task<(ServedRows? Rows, string? Error)> LoadDeletableAsync(int shopId, long orderId, Staff staff, DateTime now)
    {
        var order = await db.fnbOrder.AsTracking().FirstOrDefaultAsync(x => x.id == orderId && x.shop_id == shopId && x.source_type == "manual");
        if (order == null) return (null, "厨房单不存在");
        var document = await db.fnbStockDocument.AsTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.order_id == orderId &&
            x.document_type == "serve" && x.status == "posted");
        if (document == null || document.posted_at == null) return (null, "这张厨房单还没有扣料");
        if (now - document.posted_at.Value > ChangeWindow) return (null, "扣料已超过 10 分钟，不能修改或删除");
        if (document.posted_by_staff_id != staff.id && staff.title_level < 200) return (null, "只能修改、删除自己的厨房单，或请店长操作");
        var lines = await db.fnbStockDocumentLine.AsTracking().Where(x => x.document_id == document.id).ToListAsync();
        long[] lineIds = lines.Select(x => x.id).ToArray();
        var movements = await db.fnbStockMovement.AsTracking().Where(x => lineIds.Contains(x.document_line_id)).ToListAsync();
        long[] movementIds = movements.Select(x => x.id).ToArray();
        if (await db.fnbStocktakeLine.AnyAsync(x => x.snapshot_last_movement_id != null && movementIds.Contains(x.snapshot_last_movement_id.Value)))
            return (null, "扣料已被盘点引用，不能修改或删除");
        int[] batchIds = movements.Select(x => x.batch_id).Distinct().ToArray();
        var stocks = await db.fnbMaterialBatchStock.AsTracking().Where(x => batchIds.Contains(x.batch_id)).ToDictionaryAsync(x => x.batch_id);
        var batches = await db.fnbMaterialBatch.AsTracking().Where(x => batchIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        if (stocks.Values.Any(x => x.is_destroyed) || batches.Values.Any(x => !x.valid))
            return (null, "扣料的批次已销毁或作废，不能回滚");
        return (new ServedRows(order, document, lines, movements, stocks, batches), null);
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record ServeNeed(int ItemId, string ItemName, decimal PlannedQuantity, decimal ActualQuantity, decimal ShortageQuantity);
public sealed record ServeResult(long DocumentId, bool Replayed, IReadOnlyList<ServeNeed> Needs);

public sealed class FnbServeService(ApplicationDBContext db)
{
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
        if (!FnbAccess.CanAccess(actor.Staff, shopId, false)) throw new UnauthorizedAccessException();
        if (requestId == Guid.Empty) throw new ArgumentException("请求号必填");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.document_type == "serve" && x.request_id == requestId);
        if (previous != null)
        {
            if (previous.order_id != orderId) throw new InvalidOperationException("请求号已用于其他订单");
            var oldLines = await db.fnbStockDocumentLine.AsNoTracking().Where(x => x.document_id == previous.id).ToListAsync();
            await tx.CommitAsync();
            return new ServeResult(previous.id, true, oldLines.Select(x => new ServeNeed(x.item_id, x.item_name,
                x.planned_qty, x.actual_qty, x.shortage_qty)).ToList());
        }
        if (await db.fnbStockDocument.AnyAsync(x => x.order_id == orderId && x.status == "posted"))
            throw new InvalidOperationException("订单已出餐，不可重复扣料");
        var (_, lines, needs) = await DemandAsync(shopId, orderId);
        DateTime now = DateTime.UtcNow;
        DateTime businessDate = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var (stocks, batches, allocations, items) = await AllocateAsync(shopId, needs, DateOnly.FromDateTime(businessDate));
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
        await tx.CommitAsync();
        return new ServeResult(document.id, false, needs.Select(x => new ServeNeed(x.Key, items[x.Key].name,
            x.Value, allocations[x.Key].ActualQuantity, allocations[x.Key].ShortageQuantity)).ToList());
    }
}

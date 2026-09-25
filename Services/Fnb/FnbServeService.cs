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

// SettledByStocktake：欠料的食材在出餐后已盘点，差异已由盘点调整，不能再补扣
public sealed record ServeNeed(int ItemId, string ItemName, decimal PlannedQuantity, decimal ActualQuantity, decimal ShortageQuantity,
    bool SettledByStocktake = false);
public sealed record ServeResult(long DocumentId, bool Replayed, IReadOnlyList<ServeNeed> Needs);
// 建单前查配料库存：Available 为出餐能扣的量（散装、已开封、自制，未过期），Sealed 为能开封的整包；
// OpenBatch 为最早到期、可开封 1 件的未开封批次，给「开封」按钮用
public sealed record DeductStock(int ItemId, decimal AvailableQuantity, decimal SealedQuantity, int SealedPacks,
    int? OpenBatchId, string? OpenBatchNo, decimal? OpenPackSize, string? PackUnitName);
public sealed record FillShortageResult(IReadOnlyList<ServeNeed> Needs, int FilledItems, IReadOnlyList<string> SettledByStocktake);

// 建单即扣料：一张厨房单一道菜（将来由外部订单自动导入），按该菜已发布配方 × 份数扣料；
// Ingredients 为单上微调的用量（基本单位），只能是配方里的食材，0 表示这单不扣这一项，没传的按配方
public sealed record KitchenIngredientInput(int ItemId, decimal Quantity);
public sealed record KitchenOrderServeInput(int ShopId, Guid RequestId, string? TableNo, string? Remark,
    IReadOnlyList<ManualKitchenLineInput> Lines, IReadOnlyList<KitchenIngredientInput>? Ingredients = null);
public sealed record KitchenOrderServeResult(long OrderId, string DisplayNo, long DocumentId, bool Replayed, IReadOnlyList<ServeNeed> Needs);
// 扣料 10 分钟内编辑：换菜、份数、微调用量、桌号、备注
public sealed record KitchenOrderUpdateInput(int ShopId, long OrderId, string? TableNo, string? Remark,
    IReadOnlyList<ManualKitchenLineInput> Lines, IReadOnlyList<KitchenIngredientInput>? Ingredients = null);

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

    private static DateTime BusinessDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;

    // excludedBatches：补扣时跳过这一行已扣过的批次（同一单据行同一批次只能有一条流水）
    private async Task<(List<FnbMaterialBatchStock> Stocks, Dictionary<int, FnbMaterialBatch> Batches,
        Dictionary<int, StockAllocation> Allocations, Dictionary<int, FnbMaterialItem> Items)> AllocateAsync(
        int shopId, Dictionary<int, decimal> needs, DateOnly businessDate, Dictionary<int, HashSet<int>>? excludedBatches = null)
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
            var skip = excludedBatches?.GetValueOrDefault(pair.Key);
            var candidates = stocks.Where(x => x.item_id == pair.Key && (skip == null || !skip.Contains(x.batch_id))).Select(x =>
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
        var result = await PostCoreAsync(shopId, orderId, requestId, actor, null);
        await tx.CommitAsync();
        return result;
    }

    /// <summary>配方 × 份数的用量按单上微调覆盖：只能改配方里的食材，0 表示不扣这一项，至少扣一种。</summary>
    public static Dictionary<int, decimal> Adjust(Dictionary<int, decimal> recipeNeeds, IReadOnlyList<KitchenIngredientInput>? adjustments)
    {
        if (adjustments == null || adjustments.Count == 0) return recipeNeeds;
        if (adjustments.Select(x => x.ItemId).Distinct().Count() != adjustments.Count ||
            adjustments.Any(x => x.Quantity < 0 || x.Quantity >= 1_000_000_000_000m || x.Quantity != decimal.Round(x.Quantity, 6)))
            throw new ArgumentException("配料用量无效");
        var result = new Dictionary<int, decimal>(recipeNeeds);
        foreach (var a in adjustments)
        {
            if (!result.ContainsKey(a.ItemId)) throw new ArgumentException("只能微调配方里的配料用量");
            if (a.Quantity == 0) result.Remove(a.ItemId);
            else result[a.ItemId] = a.Quantity;
        }
        if (result.Count == 0) throw new ArgumentException("至少要扣一种配料");
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
        var served = await PostCoreAsync(input.ShopId, created.OrderId, input.RequestId, actor, input.Ingredients);
        await tx.CommitAsync();
        return new KitchenOrderServeResult(created.OrderId, created.DisplayNo, served.DocumentId, created.Replayed, served.Needs);
    }

    private async Task<ServeResult> PostCoreAsync(int shopId, long orderId, Guid requestId, FnbAccess.Actor actor,
        IReadOnlyList<KitchenIngredientInput>? adjustments)
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
        var (_, _, recipeNeeds) = await DemandAsync(shopId, orderId);
        var needs = Adjust(recipeNeeds, adjustments);
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
        var stockMap = await TakeStockAsync(stocks, batches, allocations, actor, now);
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
        await WriteMovementsAsync(documentLines, allocations, stockMap, now);
        return needs.Select(x => new ServeNeed(x.Key, items[x.Key].name,
            x.Value, allocations[x.Key].ActualQuantity, allocations[x.Key].ShortageQuantity)).ToList();
    }

    // 按分配结果扣批次库存，扣光的批次标「用完」
    private async Task<Dictionary<int, FnbMaterialBatchStock>> TakeStockAsync(List<FnbMaterialBatchStock> stocks,
        Dictionary<int, FnbMaterialBatch> batches, Dictionary<int, StockAllocation> allocations, FnbAccess.Actor actor, DateTime now)
    {
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
        return stockMap;
    }

    private async Task WriteMovementsAsync(IEnumerable<FnbStockDocumentLine> lines, Dictionary<int, StockAllocation> allocations,
        Dictionary<int, FnbMaterialBatchStock> stockMap, DateTime now)
    {
        foreach (var line in lines)
            foreach (var allocation in allocations[line.item_id].Lines)
            {
                var stock = stockMap[allocation.BatchId];
                db.fnbStockMovement.Add(new FnbStockMovement
                {
                    document_line_id = line.id, shop_id = line.shop_id, item_id = line.item_id,
                    batch_id = allocation.BatchId, direction = -1, quantity = allocation.Quantity,
                    amount = allocation.Amount, balance_qty = stock.quantity, balance_amount = stock.stock_amount,
                    created_at = now
                });
            }
        await db.SaveChangesAsync();
    }

    /// <summary>建单前查配料库存，口径与扣料一致；开封按钮取最早到期、还够 1 件的未开封批次。</summary>
    public async Task<IReadOnlyList<DeductStock>> DeductStockAsync(int shopId, IReadOnlyList<int> itemIds)
    {
        int[] ids = itemIds.Distinct().ToArray();
        DateTime today = BusinessDate(DateTime.UtcNow);
        var stocks = await db.fnbMaterialBatchStock.AsNoTracking().Where(x => x.shop_id == shopId && ids.Contains(x.item_id)
            && x.quantity > 0 && !x.is_destroyed).ToListAsync();
        int[] batchIds = stocks.Select(x => x.batch_id).ToArray();
        var batches = await db.fnbMaterialBatch.AsNoTracking().Where(x => batchIds.Contains(x.id)).ToDictionaryAsync(x => x.id);
        return ids.Select(id =>
        {
            var own = stocks.Where(x => x.item_id == id).ToList();
            decimal available = own.Where(x =>
            {
                var b = batches[x.batch_id];
                return FnbInventoryRules.IsDeductible(new AvailableBatch(x.batch_id, DateOnly.FromDateTime(b.expire_date), x.stock_form,
                    x.quantity, x.stock_amount, b.valid && b.dispose_status == null, x.is_destroyed, x.received_at), DateOnly.FromDateTime(today));
            }).Sum(x => x.quantity);
            var openable = own.Where(x => FnbStockPostingService.CanOpen(x, batches[x.batch_id], today)).ToList();
            var first = openable.Where(x => x.quantity >= x.pack_size!.Value)
                .OrderBy(x => batches[x.batch_id].expire_date).ThenBy(x => x.received_at).ThenBy(x => x.batch_id).FirstOrDefault();
            return new DeductStock(id, available, openable.Sum(x => x.quantity),
                openable.Sum(x => (int)Math.Floor(x.quantity / x.pack_size!.Value)),
                first?.batch_id, first == null ? null : batches[first.batch_id].batch_no, first?.pack_size, first?.pack_unit_name);
        }).ToList();
    }

    // 出餐后已盘点（盘点单过账时间晚于扣料）的食材：欠料差异已由盘点调整
    private async Task<HashSet<int>> SettledByStocktakeAsync(int shopId, DateTime servedAt, IEnumerable<int> itemIds)
    {
        int[] ids = itemIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var settled = await (from l in db.fnbStocktakeLine
                             join d in db.fnbStockDocument on l.document_id equals d.id
                             where l.shop_id == shopId && ids.Contains(l.item_id) && l.counted_qty != null &&
                                 d.document_type == "stocktake" && d.status == "posted" && d.posted_at > servedAt
                             select l.item_id).Distinct().ToListAsync();
        return settled.ToHashSet();
    }

    /// <summary>扣料时间在 since 之后、还有未补欠料（且没被之后的盘点调整）的厨房单 ID，新的在前。</summary>
    public async Task<List<long>> ShortageOrderIdsAsync(int shopId, DateTime since, int take)
    {
        var rows = await (from d in db.fnbStockDocument
                          join l in db.fnbStockDocumentLine on d.id equals l.document_id
                          where d.shop_id == shopId && d.document_type == "serve" && d.status == "posted" && d.order_id != null &&
                              d.posted_at >= since && l.shortage_qty > 0 &&
                              !(from tl in db.fnbStocktakeLine
                                join td in db.fnbStockDocument on tl.document_id equals td.id
                                where tl.shop_id == shopId && tl.item_id == l.item_id && tl.counted_qty != null &&
                                    td.document_type == "stocktake" && td.status == "posted" && td.posted_at > d.posted_at
                                select tl.id).Any()
                          select new { OrderId = d.order_id!.Value, d.posted_at }).ToListAsync();
        return rows.GroupBy(x => x.OrderId).OrderByDescending(g => g.Max(x => x.posted_at)).Take(take).Select(g => g.Key).ToList();
    }

    /// <summary>补扣欠料：补录入库或开封后，按现在的库存把这单欠的配料再扣一次（FEFO，仍不够的继续记欠料）。
    /// 扣的量记在原单据行上、另写流水，不改计划用量；不受 10 分钟限制。出餐后已盘点的食材不补扣。</summary>
    public async Task<FillShortageResult> FillShortageAsync(int shopId, long orderId, FnbAccess.Actor actor)
    {
        if (!FnbAccess.CanAccess(actor.Staff, shopId, false)) throw new UnauthorizedAccessException();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.order_id == orderId &&
            x.document_type == "serve" && x.status == "posted");
        if (document == null || document.posted_at == null) throw new ArgumentException("这张厨房单还没有扣料");
        var shortLines = await db.fnbStockDocumentLine.AsTracking().Where(x => x.document_id == document.id && x.shortage_qty > 0).ToListAsync();
        if (shortLines.Count == 0) throw new ArgumentException("这张厨房单没有欠料");
        var settled = await SettledByStocktakeAsync(shopId, document.posted_at.Value, shortLines.Select(x => x.item_id));
        var open = shortLines.Where(x => !settled.Contains(x.item_id)).ToList();
        if (open.Count == 0) throw new ArgumentException("欠料的食材在出餐后已盘点过，差异已由盘点调整，不用再补扣");
        long[] lineIds = open.Select(x => x.id).ToArray();
        var used = await db.fnbStockMovement.AsNoTracking().Where(x => lineIds.Contains(x.document_line_id))
            .Select(x => new { x.document_line_id, x.batch_id }).ToListAsync();
        var excluded = open.ToDictionary(x => x.item_id, x => used.Where(u => u.document_line_id == x.id).Select(u => u.batch_id).ToHashSet());
        DateTime now = DateTime.UtcNow;
        var (stocks, batches, allocations, _) = await AllocateAsync(shopId, open.ToDictionary(x => x.item_id, x => x.shortage_qty),
            DateOnly.FromDateTime(BusinessDate(now)), excluded);
        var filled = open.Where(x => allocations[x.item_id].ActualQuantity > 0).ToList();
        if (filled.Count == 0) throw new ArgumentException("库存还是不够，先入库或开封再补扣");
        var stockMap = await TakeStockAsync(stocks, batches, allocations, actor, now);
        string when = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).ToString("MM-dd HH:mm");
        foreach (var line in filled)
        {
            var allocation = allocations[line.item_id];
            line.actual_qty += allocation.ActualQuantity;
            line.actual_amount += allocation.Lines.Sum(x => x.Amount);
            string note = $"{when} {actor.Staff.name} 补扣 {allocation.ActualQuantity:0.######}";
            line.remark = string.IsNullOrEmpty(line.remark) ? note : line.remark + "；" + note;
            if (line.remark.Length > 1000) line.remark = line.remark[^1000..];
        }
        await db.SaveChangesAsync();
        await WriteMovementsAsync(filled, allocations, stockMap, now);
        await tx.CommitAsync();
        return new FillShortageResult(await ServedNeedsAsync(shopId, orderId), filled.Count,
            settled.Select(id => shortLines.First(x => x.item_id == id).item_name).ToList());
    }

    /// <summary>已扣配料（出餐单据行），厨房单详情展示用；未出餐时为空。</summary>
    public async Task<IReadOnlyList<ServeNeed>> ServedNeedsAsync(int shopId, long orderId)
    {
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.shop_id == shopId && x.order_id == orderId &&
            x.document_type == "serve" && x.status == "posted");
        if (document == null) return [];
        var lines = await db.fnbStockDocumentLine.AsNoTracking().Where(x => x.document_id == document.id).OrderBy(x => x.line_no).ToListAsync();
        var settled = await SettledByStocktakeAsync(shopId, document.posted_at!.Value, lines.Where(x => x.shortage_qty > 0).Select(x => x.item_id));
        return lines.Select(x => new ServeNeed(x.item_id, x.item_name, x.planned_qty, x.actual_qty, x.shortage_qty,
            x.shortage_qty > 0 && settled.Contains(x.item_id))).ToList();
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

    /// <summary>扣料 10 分钟内，本人或店长可编辑手动厨房单：先退回原配料，再换菜品、份数、桌号、备注，按新菜品配方（及单上微调）重新扣料。
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
        var (_, _, recipeNeeds) = await DemandAsync(input.ShopId, input.OrderId);
        var served = await DeductAsync(rows.Document, Adjust(recipeNeeds, input.Ingredients), actor, now);
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

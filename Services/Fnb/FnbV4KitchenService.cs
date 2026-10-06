#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
namespace SnowmeetApi.Services.Fnb;

public sealed partial class FnbV4Service
{
    private async Task<FnbRecipe> Recipe(int ownerId, bool prep) => await db.fnbRecipe.Where(x => x.shop_id == shopId && x.status == "published" &&
        (prep ? x.output_item_id == ownerId && x.recipe_type == "prep" : x.dish_spec_id == ownerId && x.recipe_type == "dish"))
        .OrderByDescending(x => x.version_no).FirstOrDefaultAsync() ?? throw new ArgumentException("尚未发布配方");
    private async Task<FnbDishSpec> Spec(int id) => await db.fnbDishSpec.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId && x.valid) ?? throw new ArgumentException("本店菜品规格不存在或已停用");
    public async Task<object> CreatePrep(FnbPrepCreateRequest input)
    {
        var category = await db.fnbCategory.SingleOrDefaultAsync(x => x.id == input.CategoryId && x.valid && x.level == 2 && x.is_prepared) ?? throw new ArgumentException("请选择半成品二级分类");
        var item = await new FnbV4CatalogService(db).CreateItemAsync(new(shopId, category.id, input.Name, input.Name, input.BaseUnitCode));
        var form = (await Forms(item.id)).Single(); form.unit_name = Text(input.UnitName, 40, "单位"); Modified(form); await db.SaveChangesAsync();
        var recipe = await SaveRecipe(new(shopId, item.id, input.OutputQuantity, input.Lines ?? []), true, true);
        return new { item, recipe };
    }
    public async Task<object> SaveRecipe(FnbRecipeSaveRequest input, bool prep, bool allowEmpty = false)
    {
        if (input.Lines == null || input.Lines.Length > 100 || (!allowEmpty && input.Lines.Length == 0) || input.Lines.Select(x => x.ItemId).Distinct().Count() != input.Lines.Length) throw new ArgumentException("配方须包含不重复的用料");
        if (prep) { var item = await Item(input.OwnerId); if (item.item_type != "prepared") throw new ArgumentException("不是半成品"); }
        else await Spec(input.OwnerId);
        foreach (var l in input.Lines)
        {
            await Item(l.ItemId); Qty(l.Quantity);
            if (prep && l.ItemId == input.OwnerId) throw new ArgumentException("半成品不能消耗自身");
            if ((await Forms(l.ItemId)).Length == 0) throw new ArgumentException("用料缺少出品态");
        }
        if (prep)
        {
            var recipes = await db.fnbRecipe.Where(x => x.shop_id == shopId && x.recipe_type == "prep" && x.status == "published").ToArrayAsync();
            var ids = recipes.Select(x => x.id).ToArray(); var all = await db.fnbRecipeLine.Where(x => ids.Contains(x.recipe_id)).ToArrayAsync();
            var graph = recipes.Where(x => x.output_item_id != input.OwnerId).GroupBy(x => x.output_item_id!.Value).ToDictionary(g => g.Key, g => all.Where(l => l.recipe_id == g.OrderByDescending(r => r.version_no).First().id).Select(l => l.item_id).ToArray());
            graph[input.OwnerId] = input.Lines.Select(x => x.ItemId).ToArray();
            bool Cycle(int n, HashSet<int> path) { if (!path.Add(n)) return true; bool cycle = graph.TryGetValue(n, out var children) && children.Any(c => Cycle(c, path)); path.Remove(n); return cycle; }
            if (Cycle(input.OwnerId, [])) throw new ArgumentException("半成品配方形成循环依赖");
        }
        var old = await db.fnbRecipe.Where(x => x.shop_id == shopId && (prep ? x.output_item_id == input.OwnerId : x.dish_spec_id == input.OwnerId)).ToArrayAsync();
        foreach (var r in old.Where(x => x.status == "published")) { r.status = "retired"; Modified(r); }
        var recipe = new FnbRecipe { shop_id = shopId, recipe_type = prep ? "prep" : "dish", output_item_id = prep ? input.OwnerId : null,
            dish_spec_id = prep ? null : input.OwnerId, output_qty = prep ? Qty(input.OutputQuantity) : 1, version_no = old.Select(x => x.version_no).DefaultIfEmpty(0).Max() + 1,
            status = input.Lines.Length == 0 ? "draft" : "published", created_by_staff_id = StaffId, created_at = Now, published_at = input.Lines.Length == 0 ? null : Now };
        db.fnbRecipe.Add(recipe); await db.SaveChangesAsync();
        int sort = 0; foreach (var l in input.Lines) db.fnbRecipeLine.Add(new FnbRecipeLine { recipe_id = recipe.id, item_id = l.ItemId, quantity = l.Quantity, sort = sort++ });
        await db.SaveChangesAsync(); return new { recipe, lines = input.Lines };
    }
    public async Task<object> ListPreps()
    {
        var items = await db.fnbItem.Where(x => x.valid && x.item_type == "prepared").ToArrayAsync();
        var recipes = await db.fnbRecipe.Where(x => x.shop_id == shopId && x.recipe_type == "prep" && x.status != "retired").ToArrayAsync();
        var ids = recipes.Select(x => x.id).ToArray(); var lines = await db.fnbRecipeLine.Where(x => ids.Contains(x.recipe_id)).ToArrayAsync();
        return items.Select(x => new { item = x, recipe = recipes.Where(r => r.output_item_id == x.id).OrderByDescending(r => r.version_no).FirstOrDefault(),
            lines = lines.Where(l => recipes.Any(r => r.id == l.recipe_id && r.output_item_id == x.id)).ToArray() }).ToArray();
    }
    public async Task<object> Prepare(FnbPreparationRequest input)
    {
        Qty(input.Batches); await Area(input.AreaId, required: true); var recipe = await Recipe(input.ItemId, true);
        var lines = await db.fnbRecipeLine.Where(x => x.recipe_id == recipe.id).OrderBy(x => x.item_id).ToArrayAsync();
        if (lines.Length == 0) throw new ArgumentException("半成品配方没有用料");
        var item = await Item(input.ItemId); var form = (await Forms(item.id)).Last();
        var d = await Document("prep", input.RequestId); d.recipe_id = recipe.id; Modified(d); decimal cost = 0;
        foreach (var l in lines) cost += await Consume(d, l.item_id, Qty(Round(l.quantity * input.Batches)));
        DateTime expiry;
        if (input.ExpireDate != null) expiry = input.ExpireDate.Value.Date;
        else if (form.shelf_after_op_days != null) expiry = Today.AddDays(form.shelf_after_op_days.Value);
        else expiry = (await Expiry(item.id, form.storage_type, Today, null)).Date;
        if (expiry < Today) throw new ArgumentException("产出到期日期不能在过去");
        // A preparation cannot extend the freshness of its consumed ingredients.
        var inputBatchIds = await (from m in db.fnbStockMovement join l in db.fnbStockDocumentLine on m.document_line_id equals l.id where l.document_id == d.id && m.direction == -1 select m.batch_id).ToArrayAsync();
        var sourceExpiry = await db.fnbBatch.Where(x => inputBatchIds.Contains(x.id)).MinAsync(x => x.effective_expire);
        expiry = expiry < sourceExpiry ? expiry : sourceExpiry;
        decimal output = Qty(Round(recipe.output_qty * input.Batches));
        var b = new FnbBatch { shop_id = shopId, item_id = item.id, form_id = form.id, batch_no = await NewBatchNo(item.id), state = "final", storage_type = form.storage_type,
            production_date = Today, expire_date = expiry, expiry_source = "preparation", received_at = Now };
        db.fnbBatch.Add(b); await db.SaveChangesAsync(); db.fnbBatchDetail.Add(new FnbBatchDetail { batch_id = b.id, area_id = input.AreaId });
        var line = await Line(d, item, 1, output, 1, form.id, b.id); await Move(line, b, 1, output, Round(cost));
        return new { documentId = d.id.ToString(), recipeId = recipe.id.ToString(), batch = await BatchData(b) };
    }
    public async Task<object> CreateDish(FnbDishCreateRequest input)
    {
        string name = Text(input.Name, 200, "菜品名称");
        if (await db.product.AnyAsync(x => x.shop_id == shopId && x.type == "餐饮" && x.valid == 1 && x.name == name)) throw new ArgumentException("本店已有同名菜品");
        decimal price = Qty(input.SalePrice ?? 0, true);
        var product = new Product { name = name, shop_id = shopId, type = "餐饮", valid = 1, sale_price = (double)price, hidden = 1, on_shelves = 0, create_date = Now };
        db.product.Add(product); await db.SaveChangesAsync();
        var spec = await AddSpec(new(shopId, product.id, input.SpecName, input.SalePrice)); return new { productId = product.id, name, spec };
    }
    public async Task<object> AddSpec(FnbDishSpecRequest input)
    {
        if (!await db.product.AnyAsync(x => x.id == input.ProductId && x.shop_id == shopId && x.type == "餐饮" && x.valid == 1)) throw new ArgumentException("本店菜品不存在");
        string name = Text(input.Name, 100, "规格名称");
        if (await db.fnbDishSpec.AnyAsync(x => x.product_id == input.ProductId && x.valid && x.name == name)) throw new ArgumentException("菜品已有同名规格");
        if (input.SalePrice != null) Qty(input.SalePrice.Value, true);
        var spec = new FnbDishSpec { shop_id = shopId, product_id = input.ProductId, name = name, spec_code = "S" + Guid.NewGuid().ToString("N"),
            sale_price = input.SalePrice, is_default = !await db.fnbDishSpec.AnyAsync(x => x.product_id == input.ProductId && x.valid), valid = true, created_at = Now };
        db.fnbDishSpec.Add(spec); await db.SaveChangesAsync(); return spec;
    }
    public async Task<object> DeleteSpec(int specId)
    {
        var spec = await Spec(specId);
        if (await (from l in db.fnbOrderLine join o in db.fnbOrder on l.order_id equals o.id where l.dish_spec_id == specId && o.order_status == "pending" select l.id).AnyAsync()) throw new ArgumentException("规格仍被待出餐订单使用");
        if (await db.fnbDishSpec.CountAsync(x => x.product_id == spec.product_id && x.valid) <= 1) throw new ArgumentException("菜品至少保留一个规格");
        spec.valid = false; spec.updated_at = Now; Modified(spec);
        if (spec.is_default) { var next = await db.fnbDishSpec.Where(x => x.product_id == spec.product_id && x.valid && x.id != spec.id).OrderBy(x => x.id).FirstAsync(); next.is_default = true; Modified(next); }
        await db.SaveChangesAsync(); return spec;
    }
    public async Task<object> ListDishes()
    {
        var specs = await db.fnbDishSpec.Where(x => x.shop_id == shopId && x.valid).ToArrayAsync();
        var ids = specs.Select(x => x.product_id).Distinct().ToArray(); var products = await db.product.Where(x => ids.Contains(x.id) && x.shop_id == shopId && x.type == "餐饮" && x.valid == 1).ToArrayAsync();
        var recipes = await db.fnbRecipe.Where(x => x.shop_id == shopId && x.recipe_type == "dish" && x.status == "published").ToArrayAsync();
        var rids = recipes.Select(x => x.id).ToArray(); var lines = await db.fnbRecipeLine.Where(x => rids.Contains(x.recipe_id)).ToArrayAsync();
        var stock = await Stock();
        return products.Select(p => new { productId = p.id, p.name, specs = specs.Where(s => s.product_id == p.id).Select(s => {
            var r = recipes.Where(r => r.dish_spec_id == s.id).OrderByDescending(r => r.version_no).FirstOrDefault(); var ls = lines.Where(l => l.recipe_id == r?.id).ToArray();
            return new { spec = s, recipe = r, lines = ls, servings = ls.Length == 0 ? 0 : ls.Min(l => decimal.Floor((stock.FirstOrDefault(x => x.ItemId == l.item_id)?.AvailableQuantity ?? 0) / l.quantity)) }; }).ToArray() }).ToArray();
    }
    public async Task<object> CreateOrder(FnbOrderCreateRequest input)
    {
        if (input.Lines == null || input.Lines.Length is < 1 or > 100 || input.Lines.Select(x => x.SpecId).Distinct().Count() != input.Lines.Length) throw new ArgumentException("订单须有不重复的规格行");
        var order = new FnbOrder { shop_id = shopId, source_type = "manual", display_no = "SO" + Today.ToString("yyMMdd") + "-" + input.RequestId.ToString("N")[..8],
            business_date = Today, ordered_at = Now, order_status = "pending", review_status = "confirmed", refund_status = "none", created_at = Now,
            table_no = Optional(input.TableNo, 100, "桌号"), remark = Optional(input.Remark, 2000, "备注") };
        db.fnbOrder.Add(order); await db.SaveChangesAsync(); decimal total = 0; int seq = 0;
        foreach (var l in input.Lines)
        {
            var spec = await Spec(l.SpecId); Qty(l.Quantity); var recipe = await Recipe(spec.id, false);
            var p = await db.product.SingleAsync(x => x.id == spec.product_id && x.shop_id == shopId && x.valid == 1 && x.type == "餐饮");
            db.fnbOrderLine.Add(new FnbOrderLine { shop_id = shopId, order_id = order.id, line_key = (++seq).ToString(), option_key = "", item_name = p.name, spec_name = spec.name,
                quantity = l.Quantity, dish_spec_id = spec.id, recipe_id = recipe.id, is_inventory_line = true }); total += l.Quantity * (spec.sale_price ?? (decimal)p.sale_price);
        }
        order.total_amount = Round(total); Modified(order); await db.SaveChangesAsync(); return await OrderData(order);
    }
    private async Task<FnbOrder> Order(long id) => await db.fnbOrder.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId) ?? throw new ArgumentException("本店订单不存在");
    private async Task<object> OrderData(FnbOrder order) => new { order, lines = await db.fnbOrderLine.Where(x => x.order_id == order.id).OrderBy(x => x.id).ToArrayAsync() };
    public async Task<object> PendingOrders()
    { var orders = await db.fnbOrder.Where(x => x.shop_id == shopId && x.order_status == "pending").OrderBy(x => x.ordered_at).Take(200).ToArrayAsync(); var output = new List<object>(); foreach (var order in orders) output.Add(await OrderData(order)); return output; }
    private async Task<FnbIngredientLine[]> OrderIngredients(long orderId)
    {
        var order = await Order(orderId); if (order.order_status != "pending") throw new ArgumentException("订单已出餐或已取消");
        var ls = await db.fnbOrderLine.Where(x => x.order_id == order.id && x.is_inventory_line).ToArrayAsync();
        var rids = ls.Select(x => x.recipe_id).ToArray(); var recipeLines = await db.fnbRecipeLine.Where(x => rids.Contains(x.recipe_id)).ToArrayAsync();
        if (ls.Length == 0 || ls.Any(l => !recipeLines.Any(r => r.recipe_id == l.recipe_id))) throw new ArgumentException("订单没有完整配方");
        return ls.SelectMany(l => recipeLines.Where(r => r.recipe_id == l.recipe_id).Select(r => new FnbIngredientLine(r.item_id, Round(r.quantity * (l.quantity - l.cancelled_qty)))))
            .GroupBy(x => x.ItemId).Select(g => new FnbIngredientLine(g.Key, Round(g.Sum(x => x.Quantity)))).OrderBy(x => x.ItemId).ToArray();
    }
    public async Task<object> PreviewServe(long orderId)
    {
        var requirements = await OrderIngredients(orderId); var stock = await Stock();
        return new { order = await OrderData(await Order(orderId)), ingredients = requirements.Select(l => {
            var s = stock.Single(x => x.ItemId == l.ItemId); return new { itemId = l.ItemId, name = s.Name, quantity = l.Quantity, baseUnit = s.BaseUnit, availableQuantity = s.AvailableQuantity,
                shortageQuantity = Math.Max(0, l.Quantity - s.AvailableQuantity), upstreamQuantity = s.StagedQuantity + s.SealedQuantity,
                suggestedAction = s.AvailableQuantity >= l.Quantity ? "ready" : s.StagedQuantity + s.SealedQuantity > 0 ? "operate" : "receive" }; }).ToArray() };
    }
    public async Task<object> Serve(FnbServeRequest input)
    {
        var defaults = await OrderIngredients(input.OrderId); var ls = input.Lines ?? defaults;
        if (ls.Length == 0 || ls.Select(x => x.ItemId).Distinct().Count() != ls.Length || ls.Any(x => !defaults.Any(d => d.ItemId == x.ItemId))) throw new ArgumentException("微调用料必须来自订单配方且不能重复");
        var d = await Document("serve", input.RequestId); d.order_id = input.OrderId; Modified(d);
        decimal cost = 0; foreach (var l in ls.OrderBy(x => x.ItemId)) { Qty(l.Quantity, true); if (l.Quantity > 0) cost += await Consume(d, l.ItemId, l.Quantity, true); }
        var order = await Order(input.OrderId); order.order_status = "served"; order.updated_at = Now; Modified(order); await db.SaveChangesAsync();
        return new { documentId = d.id.ToString(), order = await OrderData(order), cost = Round(cost), lines = await db.fnbStockDocumentLine.Where(x => x.document_id == d.id).ToArrayAsync() };
    }
    public async Task<object> Records(string type)
    {
        var docs = await db.fnbStockDocument.Where(x => x.shop_id == shopId && x.document_type == type).OrderByDescending(x => x.id).Take(200).ToArrayAsync();
        var ids = docs.Select(x => x.id).ToArray(); var lines = await db.fnbStockDocumentLine.Where(x => ids.Contains(x.document_id)).ToArrayAsync();
        var lids = lines.Select(x => x.id).ToArray(); var moves = await db.fnbStockMovement.Where(x => lids.Contains(x.document_line_id)).ToArrayAsync();
        var oids = docs.Select(x => x.order_id).ToArray(); var orders = await db.fnbOrder.Where(x => x.shop_id == shopId && oids.Contains(x.id)).ToArrayAsync();
        return docs.Select(d => new { document = d, lines = lines.Where(x => x.document_id == d.id).Select(l => new { line = l, movements = moves.Where(x => x.document_line_id == l.id).ToArray() }).ToArray(), order = orders.FirstOrDefault(x => x.id == d.order_id) }).ToArray();
    }
    public async Task<object> PreviewPreparation(int itemId, decimal batches)
    {
        Qty(batches); var recipe = await Recipe(itemId, true); var lines = await db.fnbRecipeLine.Where(x => x.recipe_id == recipe.id).ToArrayAsync(); var stock = await Stock();
        return new { itemId, batches, outputQuantity = Qty(Round(recipe.output_qty * batches)), ingredients = lines.Select(l => {
            var s = stock.Single(x => x.ItemId == l.item_id); decimal required = Qty(Round(l.quantity * batches)); return new { itemId = l.item_id, name = s.Name, baseUnit = s.BaseUnit, requiredQuantity = required,
                availableQuantity = s.AvailableQuantity, shortageQuantity = Math.Max(0, required - s.AvailableQuantity) }; }).ToArray() };
    }
    private async Task<byte[]> InventoryFingerprint(int itemId)
    {
        var batches = await db.fnbBatch.Where(x => x.shop_id == shopId && x.item_id == itemId && x.state == "final" && x.quantity > 0 && x.dispose_status == null && x.effective_expire >= Today && (x.ready_at == null || x.ready_at <= Now)).OrderBy(x => x.id).ToArrayAsync();
        return SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(batches.Select(b => new { b.id, b.quantity, b.amount, b.row_version }))));
    }
    public async Task<object> Snapshot(FnbSnapshotRequest input)
    {
        var stock = await Stock(); var d = await Document("stocktake", input.RequestId, null, "draft");
        foreach (var s in stock.Where(x => x.BatchCount > 0))
        {
            var last = await db.fnbStockMovement.Where(x => x.shop_id == shopId && x.item_id == s.ItemId).Select(x => (long?)x.id).MaxAsync();
            db.fnbStocktakeLine.Add(new FnbStocktakeLine { document_id = d.id, shop_id = shopId, item_id = s.ItemId, system_qty = s.AvailableQuantity,
                snapshot_at = Now, snapshot_last_movement_id = last, snapshot_fingerprint = await InventoryFingerprint(s.ItemId) });
        }
        await db.SaveChangesAsync(); return await StocktakeData(d.id);
    }
    public async Task<object> StocktakeData(long id)
    {
        var d = await db.fnbStockDocument.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId && x.document_type == "stocktake") ?? throw new ArgumentException("本店盘点单不存在");
        return new { document = d, lines = await db.fnbStocktakeLine.Where(x => x.document_id == id).OrderBy(x => x.item_id).ToArrayAsync() };
    }
    public async Task<object> SaveCount(FnbCountRequest input)
    {
        var d = await db.fnbStockDocument.SingleOrDefaultAsync(x => x.id == input.DocumentId && x.shop_id == shopId && x.document_type == "stocktake" && x.status == "draft") ?? throw new ArgumentException("盘点单不存在或已过账");
        if (input.Lines == null || input.Lines.Select(x => x.ItemId).Distinct().Count() != input.Lines.Length) throw new ArgumentException("盘点行重复");
        foreach (var r in input.Lines)
        {
            var l = await db.fnbStocktakeLine.SingleOrDefaultAsync(x => x.document_id == d.id && x.item_id == r.ItemId) ?? throw new ArgumentException("盘点行不属于快照");
            l.counted_qty = Qty(r.Quantity, true); l.counted_by_staff_id = StaffId; l.counted_at = Now; l.remark = Optional(r.Remark, 1000, "盘点备注"); Modified(l);
        }
        await db.SaveChangesAsync(); return await StocktakeData(d.id);
    }
    public async Task<object> PostStocktake(FnbDocumentRequest input)
    {
        var d = await db.fnbStockDocument.SingleOrDefaultAsync(x => x.id == input.DocumentId && x.shop_id == shopId && x.document_type == "stocktake" && x.status == "draft") ?? throw new ArgumentException("盘点单不存在或已过账");
        var lines = await db.fnbStocktakeLine.Where(x => x.document_id == d.id).OrderBy(x => x.item_id).ToArrayAsync();
        if (lines.Length == 0 || lines.Any(x => x.counted_qty == null)) throw new ArgumentException("请完成所有盘点行");
        foreach (var l in lines)
        {
            if (!(await InventoryFingerprint(l.item_id)).SequenceEqual(l.snapshot_fingerprint)) throw new FnbConflictException("盘点后库存已变更，请重新创建快照");
            decimal diff = l.counted_qty!.Value - l.system_qty;
            if (diff < 0) await Consume(d, l.item_id, -diff);
            if (diff > 0)
            {
                var item = await Item(l.item_id); var form = (await Forms(item.id)).Last();
                var existing = await db.fnbBatch.Where(x => x.shop_id == shopId && x.item_id == item.id && x.state == "final" && x.quantity > 0 && x.effective_expire >= Today && (x.ready_at == null || x.ready_at <= Now)).OrderBy(x => x.effective_expire).FirstOrDefaultAsync();
                var b = new FnbBatch { shop_id = shopId, item_id = item.id, form_id = form.id, batch_no = await NewBatchNo(item.id), state = "final", storage_type = form.storage_type,
                    expire_date = existing?.effective_expire ?? Today, expiry_source = "stocktake", received_at = Now };
                db.fnbBatch.Add(b); await db.SaveChangesAsync();
                var line = await Line(d, item, 1, diff, 1, form.id, b.id); await Move(line, b, 1, diff, existing == null ? 0 : Round(diff * existing.amount / existing.quantity));
            }
        }
        d.status = "posted"; d.posted_at = Now; d.posted_by_staff_id = StaffId; Modified(d); await db.SaveChangesAsync(); return await StocktakeData(d.id);
    }
}

using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public sealed class FnbSqlServerFactAttribute : FactAttribute
{
    public FnbSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNOWMEET_FNB_TEST_SQLSERVER")))
            Skip = "通过 SQL Server 隔离库运行器执行本组集成测试";
    }
}

// Run with SNOWMEET_FNB_TEST_SQLSERVER set to an isolated SQL Server database
// initialized with the applied inventory DDL. Never point this at a business database.
public class FnbSqlServerIntegrationTests
{
    private static int _shopSequence;

    private static ApplicationDBContext OpenTestDatabase()
    {
        string? connection = Environment.GetEnvironmentVariable("SNOWMEET_FNB_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("必须通过 SQL Server 隔离库运行器执行本组集成测试");
        var databaseName = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog;
        if (!databaseName.StartsWith("snowmeet_fnb_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("集成测试只能连接 snowmeet_fnb_test_ 前缀的隔离数据库");
        return new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlServer(connection).Options);
    }

    private sealed record Seed(int ShopId, int StaffId, int PhotoId, int RawItemId, int? PreparedItemId,
        FnbAccess.Actor Actor);

    private static async Task<Seed> SeedAsync(ApplicationDBContext db, bool withPrepared = false)
    {
        string key = Guid.NewGuid().ToString("N")[..12];
        var shop = new Shop { name = "测试店" + key,
            code = "T" + System.Threading.Interlocked.Increment(ref _shopSequence).ToString("D2") };
        db.shop.Add(shop);
        await db.SaveChangesAsync();
        var staff = new Staff { name = "测试员工" + key, gender = "男", title_level = 200,
            valid = 1, base_shop_id = shop.id };
        db.staff.Add(staff);
        var photo = new UploadFile { file_path_name = "/test/" + key + ".jpg", purpose = "食材批次",
            staff_id = staff.id, file_type = "jpg" };
        await db.SaveChangesAsync();
        photo.staff_id = staff.id;
        db.UploadFile.Add(photo);
        await db.SaveChangesAsync();
        var parent = new FnbMaterialCategory { level = 1, name = "测试大类" + key, valid = true };
        db.fnbMaterialCategory.Add(parent);
        await db.SaveChangesAsync();
        var child = new FnbMaterialCategory { parent_id = parent.id, level = 2, name = "测试小类" + key,
            default_storage = "ambient", valid = true };
        db.fnbMaterialCategory.Add(child);
        await db.SaveChangesAsync();
        var raw = new FnbMaterialItem { code = "raw" + key, name = "面粉", category_id = child.id,
            item_type = "raw", base_unit_code = "g", default_input_unit_code = "g", valid = true };
        db.fnbMaterialItem.Add(raw);
        FnbMaterialItem? prepared = null;
        if (withPrepared)
        {
            prepared = new FnbMaterialItem { code = "prep" + key, name = "面团", category_id = child.id,
                item_type = "prepared", base_unit_code = "g", default_input_unit_code = "g", valid = true };
            db.fnbMaterialItem.Add(prepared);
        }
        await db.SaveChangesAsync();
        return new Seed(shop.id, staff.id, photo.id, raw.id, prepared?.id,
            new FnbAccess.Actor(staff, "mini", "mini#" + staff.id + ":员工"));
    }

    private static ReceiptInput Receipt(Seed seed, decimal quantity, decimal unitPrice, string form = "bulk",
        decimal? packSize = null, string? packUnitName = null, string? openStorage = null, int? openDays = null)
    {
        return new ReceiptInput(seed.ShopId, Guid.NewGuid(), seed.RawItemId, "LOT" + Guid.NewGuid().ToString("N")[..8],
            form, "ambient", null, quantity, "g", unitPrice, null, null, null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 3, [seed.PhotoId], packSize, packUnitName,
            openStorage, openDays, "manual", null);
    }

    [FnbSqlServerFact]
    public async Task ManualOrderCreatesLocalKitchenOrderWithoutSkuAndCanServeAfterRecipeIsReady()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        string key = Guid.NewGuid().ToString("N")[..8];
        var category = new Category { biz_type = "餐饮", name = "测试餐饮" + key, valid = 1 };
        db.category.Add(category);
        await db.SaveChangesAsync();
        var product = new Product { category_id = category.id, shop_id = seed.ShopId,
            name = "测试菜" + key, sale_price = 20, valid = 1 };
        db.product.Add(product);
        await db.SaveChangesAsync();
        var service = new FnbManualOrderService(db);
        var input = new ManualKitchenOrderInput(seed.ShopId, Guid.NewGuid(), null, "A1", "少盐",
            [new ManualKitchenLineInput(product.id, 1, null)]);
        var first = await service.CreateAsync(input, seed.Actor);
        var replay = await service.CreateAsync(input, seed.Actor);
        Assert.Equal(first.OrderId, replay.OrderId);
        Assert.True(replay.Replayed);
        Assert.Equal("pending", first.ReviewStatus);
        var spec = await db.fnbDishSpec.SingleAsync(x => x.product_id == product.id);
        Assert.Equal("default", spec.spec_code);
        Assert.Equal(1, await db.fnbOrderImport.CountAsync(x => x.order_id == first.OrderId && x.source_method == "internal"));
        await service.CancelAsync(seed.ShopId, first.OrderId);
        Assert.Equal("cancelled", (await db.fnbOrder.SingleAsync(x => x.id == first.OrderId)).order_status);

        await new FnbReceiptService(db).PostAsync(Receipt(seed, 100m, 0.01m), seed.Actor);
        var recipe = new FnbRecipe { shop_id = seed.ShopId, recipe_type = "dish", dish_spec_id = spec.id,
            output_qty = 1, version_no = 1, status = "published", published_at = DateTime.UtcNow };
        db.fnbRecipe.Add(recipe);
        await db.SaveChangesAsync();
        db.fnbRecipeLine.Add(new FnbRecipeLine { recipe_id = recipe.id, item_id = seed.RawItemId, quantity = 100m });
        await db.SaveChangesAsync();
        var second = await service.CreateAsync(input with { RequestId = Guid.NewGuid() }, seed.Actor);
        Assert.Equal("verified", second.ReviewStatus);
        var served = await new FnbServeService(db).PostAsync(seed.ShopId, second.OrderId, Guid.NewGuid(), seed.Actor);
        Assert.Equal(100m, Assert.Single(served.Needs).ActualQuantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(seed.ShopId, second.OrderId));
    }

    [FnbSqlServerFact]
    public async Task ReceiptCreatesLegacyBatchAndStockLedgerExactlyOnce()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var input = Receipt(seed, 200m, 0.02m);
        var service = new FnbReceiptService(db);
        var first = await service.PostAsync(input, seed.Actor);
        var replay = await service.PostAsync(input, seed.Actor);
        Assert.True(replay.Replayed);
        Assert.Equal(first.DocumentId, replay.DocumentId);
        Assert.Equal(200m, first.Quantity);
        Assert.Equal(4m, first.Amount);
        Assert.Single(await db.fnbStockMovement.Where(x => x.batch_id == first.BatchId).ToListAsync());
        Assert.Equal(200m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == first.BatchId)).quantity);
    }

    // 餐饮菜品；gramsPerPortion 不为空时带「标准份」规格和已发布配方（每份用 seed 面粉若干克）
    private static async Task<Product> RestaurantDishAsync(ApplicationDBContext db, Seed seed, string name, decimal? gramsPerPortion)
    {
        var category = new Category { biz_type = "餐饮", name = "测试餐饮" + Guid.NewGuid().ToString("N")[..8], valid = 1 };
        db.category.Add(category);
        await db.SaveChangesAsync();
        var product = new Product { category_id = category.id, shop_id = seed.ShopId, name = name, sale_price = 0, valid = 1 };
        db.product.Add(product);
        await db.SaveChangesAsync();
        if (gramsPerPortion == null) return product;
        var spec = new FnbDishSpec { shop_id = seed.ShopId, product_id = product.id, spec_code = "default", name = "标准份",
            is_default = true, valid = true, created_at = DateTime.UtcNow };
        db.fnbDishSpec.Add(spec);
        await db.SaveChangesAsync();
        var recipe = new FnbRecipe { shop_id = seed.ShopId, recipe_type = "dish", dish_spec_id = spec.id,
            output_qty = 1, version_no = 1, status = "published", published_at = DateTime.UtcNow };
        db.fnbRecipe.Add(recipe);
        await db.SaveChangesAsync();
        db.fnbRecipeLine.Add(new FnbRecipeLine { recipe_id = recipe.id, item_id = seed.RawItemId, quantity = gramsPerPortion.Value });
        await db.SaveChangesAsync();
        return product;
    }

    private static KitchenOrderServeInput OneDishOrder(Seed seed, int productId, decimal portions, Guid? requestId = null) =>
        new(seed.ShopId, requestId ?? Guid.NewGuid(), "A1", null, [new ManualKitchenLineInput(productId, portions, null)]);

    [FnbSqlServerFact]
    public async Task CreateAndServeDeductsByTheDishRecipeAndNeedsExactlyOneDishWithRecipe()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        await new FnbReceiptService(db).PostAsync(Receipt(seed, 1000m, 0.01m), seed.Actor);
        var dish = await RestaurantDishAsync(db, seed, "榛果饮", 150m);
        var service = new FnbServeService(db);
        var input = OneDishOrder(seed, dish.id, 2);
        var first = await service.CreateAndServeAsync(input, seed.Actor);
        db.ChangeTracker.Clear();
        var replay = await service.CreateAndServeAsync(input, seed.Actor);
        Assert.True(replay.Replayed);
        Assert.Equal(first.OrderId, replay.OrderId);
        Assert.Equal(300m, Assert.Single(first.Needs).ActualQuantity);
        Assert.Equal(700m, (await db.fnbMaterialBatchStock.AsNoTracking().SingleAsync(x => x.item_id == seed.RawItemId)).quantity);
        var shown = Assert.Single(await service.ServedNeedsAsync(seed.ShopId, first.OrderId));
        Assert.Equal((300m, 300m), (shown.PlannedQuantity, shown.ActualQuantity));

        // 单上微调用量：配方 2 份 300 g，这单只用 250 g
        db.ChangeTracker.Clear();
        var tuned = await service.CreateAndServeAsync(input with { RequestId = Guid.NewGuid(),
            Ingredients = [new KitchenIngredientInput(seed.RawItemId, 250m)] }, seed.Actor);
        Assert.Equal(250m, Assert.Single(tuned.Needs).PlannedQuantity);
        Assert.Equal(450m, (await db.fnbMaterialBatchStock.AsNoTracking().SingleAsync(x => x.item_id == seed.RawItemId)).quantity);

        // 没有已发布配方：整单回滚，不留下厨房单
        var noRecipe = await RestaurantDishAsync(db, seed, "无配方菜", null);
        int orders = await db.fnbOrder.CountAsync(x => x.shop_id == seed.ShopId);
        db.ChangeTracker.Clear();
        Assert.Contains("还没有已发布的配方", (await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAndServeAsync(OneDishOrder(seed, noRecipe.id, 1), seed.Actor))).Message);
        db.ChangeTracker.Clear();
        Assert.Equal(orders, await db.fnbOrder.CountAsync(x => x.shop_id == seed.ShopId));
        Assert.Contains("只能有一道菜", (await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAndServeAsync(input with {
            RequestId = Guid.NewGuid(), Lines = [new ManualKitchenLineInput(dish.id, 1, null), new ManualKitchenLineInput(noRecipe.id, 1, null)] }, seed.Actor))).Message);
    }

    [FnbSqlServerFact]
    public async Task EditingServedOrderWithinTenMinutesSwapsTheDishAndReDeductsByItsRecipe()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var received = await new FnbReceiptService(db).PostAsync(Receipt(seed, 1000m, 0.01m), seed.Actor);
        var latte = await RestaurantDishAsync(db, seed, "拿铁", 300m);
        var mocha = await RestaurantDishAsync(db, seed, "摩卡", 150m);
        var service = new FnbServeService(db);
        var created = await service.CreateAndServeAsync(OneDishOrder(seed, latte.id, 1), seed.Actor);
        db.ChangeTracker.Clear();
        var postedAt = (await db.fnbStockDocument.AsNoTracking().SingleAsync(x => x.id == created.DocumentId)).posted_at;

        var edited = await service.UpdateAsync(new KitchenOrderUpdateInput(seed.ShopId, created.OrderId, "B2", "少糖",
            [new ManualKitchenLineInput(mocha.id, 3, null)]), seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Equal(created.DocumentId, edited.DocumentId);
        Assert.Equal(550m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);
        var line = Assert.Single(await db.fnbOrderLine.Where(x => x.order_id == created.OrderId).ToListAsync());
        Assert.Equal(("摩卡", 3m), (line.item_name, line.quantity));
        var order = await db.fnbOrder.SingleAsync(x => x.id == created.OrderId);
        Assert.Equal(("B2", "少糖"), (order.table_no, order.remark));
        Assert.Equal(postedAt, (await db.fnbStockDocument.SingleAsync(x => x.id == created.DocumentId)).posted_at);
        Assert.Equal(450m, Assert.Single(await service.ServedNeedsAsync(seed.ShopId, created.OrderId)).PlannedQuantity);
        Assert.Single(await db.fnbStockMovement.Where(x => x.batch_id == received.BatchId && x.direction == -1).ToListAsync());

        // 换成没有配方的菜：拒绝，原扣料不变
        var noRecipe = await RestaurantDishAsync(db, seed, "无配方菜", null);
        db.ChangeTracker.Clear();
        Assert.Contains("还没有已发布的配方", (await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new KitchenOrderUpdateInput(
            seed.ShopId, created.OrderId, null, null, [new ManualKitchenLineInput(noRecipe.id, 1, null)]), seed.Actor))).Message);
        db.ChangeTracker.Clear();
        Assert.Equal(550m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);

        // 编辑后仍可在原时限内删除，配料全部退回
        await service.DeleteAsync(seed.ShopId, created.OrderId, seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Equal(1000m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);

        var late = await service.CreateAndServeAsync(OneDishOrder(seed, latte.id, 1), seed.Actor);
        await db.fnbStockDocument.Where(x => x.id == late.DocumentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.posted_at, DateTime.UtcNow.AddMinutes(-11)));
        db.ChangeTracker.Clear();
        Assert.Contains("超过 10 分钟", (await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new KitchenOrderUpdateInput(
            seed.ShopId, late.OrderId, null, null, [new ManualKitchenLineInput(mocha.id, 1, null)]), seed.Actor))).Message);
    }

    [FnbSqlServerFact]
    public async Task DeletingServedOrderWithinTenMinutesReturnsIngredientsAndFixesLaterBalances()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var received = await new FnbReceiptService(db).PostAsync(Receipt(seed, 1000m, 0.01m), seed.Actor);
        var dish = await RestaurantDishAsync(db, seed, "榛果饮", 100m);
        var service = new FnbServeService(db);
        var first = await service.CreateAndServeAsync(OneDishOrder(seed, dish.id, 3), seed.Actor);
        var second = await service.CreateAndServeAsync(OneDishOrder(seed, dish.id, 2), seed.Actor);
        db.ChangeTracker.Clear();
        Assert.InRange((await service.ChangeSecondsLeftAsync(seed.ShopId, first.OrderId, seed.Actor.Staff))!.Value, 590, 600);

        var cook = new Staff { name = "厨师" + seed.ShopId, gender = "男", title_level = 100, valid = 1, base_shop_id = seed.ShopId };
        db.staff.Add(cook);
        await db.SaveChangesAsync();
        Assert.Contains("自己的厨房单", (await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync(seed.ShopId, first.OrderId, new FnbAccess.Actor(cook, "mini", "mini#" + cook.id)))).Message);
        db.ChangeTracker.Clear();

        await service.DeleteAsync(seed.ShopId, first.OrderId, seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Equal(800m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);
        Assert.False(await db.fnbOrder.AnyAsync(x => x.id == first.OrderId));
        Assert.False(await db.fnbStockDocument.AnyAsync(x => x.id == first.DocumentId));
        Assert.False(await db.fnbOrderLine.AnyAsync(x => x.order_id == first.OrderId));
        var later = await db.fnbStockMovement.SingleAsync(x => x.batch_id == received.BatchId && x.direction == -1);
        Assert.Equal(800m, later.balance_qty);

        // 扣光的批次（用完）退回后恢复可用
        var all = await service.CreateAndServeAsync(OneDishOrder(seed, dish.id, 8), seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Equal("用完", (await db.fnbMaterialBatch.SingleAsync(x => x.id == received.BatchId)).dispose_status);
        await service.DeleteAsync(seed.ShopId, all.OrderId, seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Null((await db.fnbMaterialBatch.SingleAsync(x => x.id == received.BatchId)).dispose_status);
        Assert.Equal(800m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);

        // 超过 10 分钟不能删
        await db.fnbStockDocument.Where(x => x.id == second.DocumentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.posted_at, DateTime.UtcNow.AddMinutes(-11)));
        db.ChangeTracker.Clear();
        Assert.Contains("超过 10 分钟", (await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(seed.ShopId, second.OrderId, seed.Actor))).Message);
    }

    [FnbSqlServerFact]
    public async Task ReceiptCanBeDeletedWithinTenMinutesBeforeAnyOtherOperation()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var service = new FnbReceiptService(db);
        var received = await service.PostAsync(Receipt(seed, 100m, 0.01m), seed.Actor);
        int? left = await service.DeleteSecondsLeftAsync(seed.ShopId, received.BatchId, seed.Actor.Staff);
        Assert.InRange(left!.Value, 590, 600);

        // 同店其他普通员工不能删别人的入库
        var cook = new Staff { name = "厨师" + seed.ShopId, gender = "男", title_level = 100, valid = 1, base_shop_id = seed.ShopId };
        db.staff.Add(cook);
        await db.SaveChangesAsync();
        var other = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync(seed.ShopId, received.BatchId, new FnbAccess.Actor(cook, "mini", "mini#" + cook.id)));
        Assert.Contains("自己的入库", other.Message);

        db.ChangeTracker.Clear();
        await service.DeleteAsync(seed.ShopId, received.BatchId, seed.Actor);
        Assert.False(await db.fnbMaterialBatch.AnyAsync(x => x.id == received.BatchId));
        Assert.False(await db.fnbMaterialBatchStock.AnyAsync(x => x.batch_id == received.BatchId));
        Assert.False(await db.fnbStockMovement.AnyAsync(x => x.batch_id == received.BatchId));
        Assert.False(await db.fnbStockDocument.AnyAsync(x => x.id == received.DocumentId));
        Assert.False(await db.fnbStockDocumentLine.AnyAsync(x => x.document_id == received.DocumentId));
        Assert.True(await db.fnbMaterialItem.AnyAsync(x => x.id == seed.RawItemId), "食材档案保留");
    }

    [FnbSqlServerFact]
    public async Task ReceiptCannotBeDeletedAfterTenMinutesOrAfterOpening()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var service = new FnbReceiptService(db);
        var old = await service.PostAsync(Receipt(seed, 100m, 0.01m), seed.Actor);
        await db.fnbStockDocument.Where(x => x.id == old.DocumentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.posted_at, DateTime.UtcNow.AddMinutes(-11)));
        db.ChangeTracker.Clear();  // 批量更新绕过跟踪器；清掉入库时跟踪的旧单据，才能读到新的入库时间
        Assert.Null(await service.DeleteSecondsLeftAsync(seed.ShopId, old.BatchId, seed.Actor.Staff));
        Assert.Contains("超过 10 分钟", (await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(seed.ShopId, old.BatchId, seed.Actor))).Message);

        db.ChangeTracker.Clear();
        var sealedBatch = await service.PostAsync(Receipt(seed, 2m, 10m, "sealed", 500m, "袋", "ambient", 2), seed.Actor);
        await new FnbStockPostingService(db).PostOpenAsync(new OpenInput(seed.ShopId, Guid.NewGuid(), sealedBatch.BatchId, 1, null), seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Null(await service.DeleteSecondsLeftAsync(seed.ShopId, sealedBatch.BatchId, seed.Actor.Staff));
        Assert.Contains("后续操作", (await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(seed.ShopId, sealedBatch.BatchId, seed.Actor))).Message);
        Assert.True(await db.fnbMaterialBatchStock.AnyAsync(x => x.batch_id == sealedBatch.BatchId));
    }

    [FnbSqlServerFact]
    public async Task ReceiptWithoutPhotosStoresNoImageIds()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var posted = await new FnbReceiptService(db).PostAsync(Receipt(seed, 100m, 0.01m) with { ImageIds = [] }, seed.Actor);
        Assert.Null((await db.fnbMaterialBatch.AsNoTracking().SingleAsync(x => x.id == posted.BatchId)).image_ids);
    }

    [FnbSqlServerFact]
    public async Task ReceiptByShelfLifeRuleAcceptsOnlyTheItemsOwnRule()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db, withPrepared: true);
        var produced = DateOnly.FromDateTime(DateTime.UtcNow);
        FnbShelfLifeRule Rule(int itemId) => new() { item_id = itemId, storage_type = "ambient",
            production_month = (byte)produced.Month, shelf_life_value = 10, shelf_life_unit = "day", valid = true };
        var own = Rule(seed.RawItemId);
        var sibling = Rule(seed.PreparedItemId!.Value);
        db.fnbShelfLifeRule.AddRange(own, sibling);
        await db.SaveChangesAsync();
        var expire = FnbInventoryRules.CalculateExpiry(produced, 10, "day");
        ReceiptInput ByRule(int ruleId) => Receipt(seed, 100m, 0.01m) with { ProductionDate = produced, ShelfLifeValue = 10,
            ShelfLifeUnit = "day", ExpireDate = expire, ExpirySource = "category", ShelfLifeRuleId = ruleId };
        var service = new FnbReceiptService(db);

        // 同一分类下另一个食材的规则不能拿来算本食材的效期
        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.PostAsync(ByRule(sibling.id), seed.Actor));
        Assert.Contains("食材保质期规则", error.Message);
        var posted = await service.PostAsync(ByRule(own.id), seed.Actor);
        var stock = await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == posted.BatchId);
        Assert.Equal(own.id, stock.shelf_life_rule_id);
        Assert.Equal("category", stock.expiry_source);
    }

    [FnbSqlServerFact]
    public async Task OpeningAndWastePreserveCostAndRejectDuplicatePosting()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var receipt = Receipt(seed, 2m, 10m, "sealed", 500m, "袋", "ambient", 2);
        var received = await new FnbReceiptService(db).PostAsync(receipt, seed.Actor);
        var posting = new FnbStockPostingService(db);
        var opened = await posting.PostOpenAsync(new OpenInput(seed.ShopId, Guid.NewGuid(), received.BatchId, 1, null), seed.Actor);
        Assert.Equal(500m, opened.Quantity);
        Assert.Equal(10m, opened.Amount);
        Assert.Equal(500m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);
        var requestId = Guid.NewGuid();
        var wasted = await posting.PostWasteAsync(new WasteInput(seed.ShopId, requestId, opened.BatchId, 500m, "damage", "破损"), seed.Actor);
        var replay = await posting.PostWasteAsync(new WasteInput(seed.ShopId, requestId, opened.BatchId, 500m, "damage", "破损"), seed.Actor);
        Assert.True(replay.Replayed);
        Assert.Equal(wasted.DocumentId, replay.DocumentId);
        Assert.True((await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == opened.BatchId)).is_destroyed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => posting.PostWasteAsync(
            new WasteInput(seed.ShopId, requestId, received.BatchId, 500m, "damage", "破损"), seed.Actor));
    }

    [FnbSqlServerFact]
    public async Task OpeningWithKeepExpiryDaysKeepsSealedExpiryDate()
    {
        // 小程序「开封后保质期不变」记为 36500 天：开封到期取 min(原到期, 开封日 + 天数)，即原到期日
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var received = await new FnbReceiptService(db).PostAsync(Receipt(seed, 2m, 10m, "sealed", 500m, "袋", "frozen", 36500), seed.Actor);
        var opened = await new FnbStockPostingService(db).PostOpenAsync(new OpenInput(seed.ShopId, Guid.NewGuid(), received.BatchId, 1, null), seed.Actor);
        var sealedExpiry = (await db.fnbMaterialBatch.AsNoTracking().SingleAsync(x => x.id == received.BatchId)).expire_date;
        Assert.Equal(sealedExpiry, (await db.fnbMaterialBatch.AsNoTracking().SingleAsync(x => x.id == opened.BatchId)).expire_date);
    }

    [FnbSqlServerFact]
    public async Task PreparationConsumesIngredientAndTransfersActualCost()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db, withPrepared: true);
        var received = await new FnbReceiptService(db).PostAsync(Receipt(seed, 1000m, 0.01m), seed.Actor);
        var recipe = new FnbRecipe { shop_id = seed.ShopId, recipe_type = "prep", output_item_id = seed.PreparedItemId,
            output_qty = 1000m, version_no = 1, status = "published", published_at = DateTime.UtcNow };
        db.fnbRecipe.Add(recipe);
        await db.SaveChangesAsync();
        db.fnbRecipeLine.Add(new FnbRecipeLine { recipe_id = recipe.id, item_id = seed.RawItemId, quantity = 500m });
        await db.SaveChangesAsync();
        var made = await new FnbPreparationService(db).PostAsync(new PreparationInput(seed.ShopId, Guid.NewGuid(), recipe.id,
            500m, "DOUGH" + Guid.NewGuid().ToString("N")[..8], "ambient", null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), 3, [seed.PhotoId], "人工确认"), seed.Actor);
        Assert.Equal(500m, made.Quantity);
        Assert.Equal(2.5m, made.Amount);
        Assert.Equal(750m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);

        // 产出照片选填：不拍照也能制作，批次不记照片
        var noPhoto = await new FnbPreparationService(db).PostAsync(new PreparationInput(seed.ShopId, Guid.NewGuid(), recipe.id,
            100m, "DOUGH" + Guid.NewGuid().ToString("N")[..8], "ambient", null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), 3, [], null), seed.Actor);
        Assert.Null((await db.fnbMaterialBatch.AsNoTracking().SingleAsync(x => x.id == noPhoto.BatchId)).image_ids);
        Assert.Equal(700m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);
    }

    [FnbSqlServerFact]
    public async Task ServingRecordsShortageWithoutNegativeStock()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var received = await new FnbReceiptService(db).PostAsync(Receipt(seed, 150m, 0.01m), seed.Actor);
        var product = new Product { name = "测试菜", shop_id = seed.ShopId, sale_price = 10 };
        db.product.Add(product);
        await db.SaveChangesAsync();
        var spec = new FnbDishSpec { shop_id = seed.ShopId, product_id = product.id, spec_code = "std",
            name = "标准份", valid = true };
        db.fnbDishSpec.Add(spec);
        await db.SaveChangesAsync();
        var recipe = new FnbRecipe { shop_id = seed.ShopId, recipe_type = "dish", dish_spec_id = spec.id,
            output_qty = 1, version_no = 1, status = "published", published_at = DateTime.UtcNow };
        db.fnbRecipe.Add(recipe);
        await db.SaveChangesAsync();
        db.fnbRecipeLine.Add(new FnbRecipeLine { recipe_id = recipe.id, item_id = seed.RawItemId, quantity = 100m });
        var order = new FnbOrder { shop_id = seed.ShopId, source_type = "manual", display_no = "K1",
            business_date = DateTime.Today, ordered_at = DateTime.UtcNow, order_status = "pending",
            review_status = "verified" };
        db.fnbOrder.Add(order);
        await db.SaveChangesAsync();
        db.fnbOrderLine.Add(new FnbOrderLine { order_id = order.id, shop_id = seed.ShopId, line_key = "1",
            item_name = "测试菜", quantity = 2, is_inventory_line = true, dish_spec_id = spec.id });
        await db.SaveChangesAsync();
        var service = new FnbServeService(db);
        var requestId = Guid.NewGuid();
        var served = await service.PostAsync(seed.ShopId, order.id, requestId, seed.Actor);
        Assert.True((await service.PostAsync(seed.ShopId, order.id, requestId, seed.Actor)).Replayed);
        Assert.Equal(50m, Assert.Single(served.Needs).ShortageQuantity);
        Assert.Equal(0m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(seed.ShopId, order.id, Guid.NewGuid(), seed.Actor));
    }

    [FnbSqlServerFact]
    public async Task DeductStockCountsOnlyWhatServingCanTakeAndPointsToTheSealedPackToOpen()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var receipts = new FnbReceiptService(db);
        await receipts.PostAsync(Receipt(seed, 100m, 0.01m), seed.Actor);
        var expired = await receipts.PostAsync(Receipt(seed, 40m, 0.01m), seed.Actor);
        await db.fnbMaterialBatch.Where(x => x.id == expired.BatchId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.expire_date, DateTime.Today.AddDays(-1)));
        var later = await receipts.PostAsync(Receipt(seed, 1m, 10m, "sealed", 500m, "袋", "ambient", 2), seed.Actor);
        await db.fnbMaterialBatch.Where(x => x.id == later.BatchId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.expire_date, DateTime.Today.AddDays(60)));
        var sooner = await receipts.PostAsync(Receipt(seed, 2m, 10m, "sealed", 500m, "袋", "ambient", 2), seed.Actor);
        db.ChangeTracker.Clear();

        var stock = Assert.Single(await new FnbServeService(db).DeductStockAsync(seed.ShopId, [seed.RawItemId]));
        Assert.Equal((100m, 1500m, 3), (stock.AvailableQuantity, stock.SealedQuantity, stock.SealedPacks));
        Assert.Equal((sooner.BatchId, 500m, "袋"), (stock.OpenBatchId!.Value, stock.OpenPackSize!.Value, stock.PackUnitName));
    }

    [FnbSqlServerFact]
    public async Task FillShortageDeductsFromNewStockKeepsThePlanAndRollsBackWithTheOrder()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var receipts = new FnbReceiptService(db);
        var first = await receipts.PostAsync(Receipt(seed, 150m, 0.01m), seed.Actor);
        var dish = await RestaurantDishAsync(db, seed, "蛋饼", 100m);
        var service = new FnbServeService(db);
        var order = await service.CreateAndServeAsync(OneDishOrder(seed, dish.id, 2), seed.Actor);
        Assert.Equal(50m, Assert.Single(order.Needs).ShortageQuantity);
        db.ChangeTracker.Clear();
        Assert.Contains(order.OrderId, await service.ShortageOrderIdsAsync(seed.ShopId, DateTime.UtcNow.AddDays(-7), 100));
        Assert.Contains("库存还是不够", (await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FillShortageAsync(seed.ShopId, order.OrderId, seed.Actor))).Message);

        // 补录 30 g：补扣 30，还欠 20；计划用量不变
        db.ChangeTracker.Clear();
        var second = await receipts.PostAsync(Receipt(seed, 30m, 0.02m), seed.Actor);
        db.ChangeTracker.Clear();
        var partly = await service.FillShortageAsync(seed.ShopId, order.OrderId, seed.Actor);
        var need = Assert.Single(partly.Needs);
        Assert.Equal((200m, 180m, 20m, 1), (need.PlannedQuantity, need.ActualQuantity, need.ShortageQuantity, partly.FilledItems));
        db.ChangeTracker.Clear();
        Assert.Equal("用完", (await db.fnbMaterialBatch.SingleAsync(x => x.id == second.BatchId)).dispose_status);

        var third = await receipts.PostAsync(Receipt(seed, 100m, 0.01m), seed.Actor);
        db.ChangeTracker.Clear();
        need = Assert.Single((await service.FillShortageAsync(seed.ShopId, order.OrderId, seed.Actor)).Needs);
        Assert.Equal((200m, 0m), (need.ActualQuantity, need.ShortageQuantity));
        db.ChangeTracker.Clear();
        Assert.Equal(80m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == third.BatchId)).quantity);
        var line = await db.fnbStockDocumentLine.SingleAsync(x => x.document_id == order.DocumentId);
        Assert.Equal(1.5m + 0.6m + 0.2m, line.actual_amount);
        Assert.Contains("补扣", line.remark);
        Assert.Equal(3, await db.fnbStockMovement.CountAsync(x => x.document_line_id == line.id));
        Assert.DoesNotContain(order.OrderId, await service.ShortageOrderIdsAsync(seed.ShopId, DateTime.UtcNow.AddDays(-7), 100));
        Assert.Contains("没有欠料", (await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FillShortageAsync(seed.ShopId, order.OrderId, seed.Actor))).Message);

        // 10 分钟内删除厨房单：连同补扣的配料一起退回
        db.ChangeTracker.Clear();
        await service.DeleteAsync(seed.ShopId, order.OrderId, seed.Actor);
        db.ChangeTracker.Clear();
        Assert.Equal(150m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == first.BatchId)).quantity);
        Assert.Equal(30m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == second.BatchId)).quantity);
        Assert.Equal(100m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == third.BatchId)).quantity);
    }

    [FnbSqlServerFact]
    public async Task FillShortageSkipsItemsStocktakenAfterServing()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var receipts = new FnbReceiptService(db);
        await receipts.PostAsync(Receipt(seed, 150m, 0.01m), seed.Actor);
        var dish = await RestaurantDishAsync(db, seed, "蛋饼", 100m);
        var service = new FnbServeService(db);
        var order = await service.CreateAndServeAsync(OneDishOrder(seed, dish.id, 2), seed.Actor);
        db.ChangeTracker.Clear();
        await receipts.PostAsync(Receipt(seed, 100m, 0.01m), seed.Actor);
        db.ChangeTracker.Clear();

        var stocktake = new FnbStocktakeService(db);
        long documentId = await stocktake.CreateSnapshotAsync(seed.ShopId, Guid.NewGuid(), [seed.RawItemId], seed.Actor);
        var row = await db.fnbStocktakeLine.SingleAsync(x => x.document_id == documentId);
        await stocktake.SaveCountAsync(new CountInput(seed.ShopId, documentId, seed.RawItemId, 60m,
            Convert.ToBase64String(row.row_version)), seed.Actor);
        await stocktake.PostAsync(new PostStocktakeInput(seed.ShopId, documentId, []), seed.Actor);
        db.ChangeTracker.Clear();

        var need = Assert.Single(await service.ServedNeedsAsync(seed.ShopId, order.OrderId));
        Assert.Equal((50m, true), (need.ShortageQuantity, need.SettledByStocktake));
        Assert.DoesNotContain(order.OrderId, await service.ShortageOrderIdsAsync(seed.ShopId, DateTime.UtcNow.AddDays(-7), 100));
        Assert.Contains("已盘点", (await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FillShortageAsync(seed.ShopId, order.OrderId, seed.Actor))).Message);
    }

    [FnbSqlServerFact]
    public async Task StocktakeChecksSnapshotAndPostsAdjustmentOnce()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var received = await new FnbReceiptService(db).PostAsync(Receipt(seed, 150m, 0.01m), seed.Actor);
        var stocktake = new FnbStocktakeService(db);
        long documentId = await stocktake.CreateSnapshotAsync(seed.ShopId, Guid.NewGuid(), [seed.RawItemId], seed.Actor);
        var row = await db.fnbStocktakeLine.SingleAsync(x => x.document_id == documentId);
        await stocktake.SaveCountAsync(new CountInput(seed.ShopId, documentId, seed.RawItemId, 100m,
            Convert.ToBase64String(row.row_version)), seed.Actor);
        Assert.False(Assert.Single(await stocktake.PreviewAsync(seed.ShopId, documentId)).SnapshotChanged);
        await stocktake.PostAsync(new PostStocktakeInput(seed.ShopId, documentId, []), seed.Actor);
        await stocktake.PostAsync(new PostStocktakeInput(seed.ShopId, documentId, []), seed.Actor);
        Assert.Equal(100m, (await db.fnbMaterialBatchStock.SingleAsync(x => x.batch_id == received.BatchId)).quantity);
        Assert.Equal("posted", (await db.fnbStockDocument.SingleAsync(x => x.id == documentId)).status);
    }

    [FnbSqlServerFact]
    public async Task SaveDishCreatesRestaurantDishAndListShowsRecipeState()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var other = await SeedAsync(db);
        string key = Guid.NewGuid().ToString("N")[..8];
        var service = new FnbDishService(db);
        var saved = await service.SaveAsync(new DishInput(seed.ShopId, 0, "酸菜白肉锅" + key, 68m, null, "热菜" + key, true));
        var product = await db.product.SingleAsync(x => x.id == saved.ProductId);
        Assert.Equal(seed.ShopId, product.shop_id);
        Assert.Equal(1, product.valid);
        Assert.Equal(0, product.hidden);
        Assert.Equal("餐饮", (await db.category.SingleAsync(x => x.id == product.category_id)).biz_type);
        var spec = await db.fnbDishSpec.SingleAsync(x => x.product_id == saved.ProductId);
        Assert.True(spec.is_default);
        Assert.Equal(spec.id, saved.SpecId);

        var again = await service.SaveAsync(new DishInput(seed.ShopId, 0, "麻婆豆腐" + key, 32m, null, "热菜" + key, true));
        Assert.Equal(saved.CategoryId, again.CategoryId);
        await service.SaveAsync(new DishInput(other.ShopId, 0, "别店菜" + key, 10m, saved.CategoryId, null, true));

        var listed = await service.ListAsync(seed.ShopId);
        var row = Assert.Single(listed.Dishes, x => x.ProductId == saved.ProductId);
        Assert.Null(row.PublishedRecipeId);
        Assert.DoesNotContain(listed.Dishes, x => x.Name == "别店菜" + key);
        Assert.Contains(listed.Categories, x => x.Id == saved.CategoryId);

        var recipe = new FnbRecipe { shop_id = seed.ShopId, recipe_type = "dish", dish_spec_id = spec.id,
            output_qty = 1, version_no = 1, status = "published", published_at = DateTime.UtcNow };
        db.fnbRecipe.Add(recipe);
        await db.SaveChangesAsync();
        row = Assert.Single((await service.ListAsync(seed.ShopId)).Dishes, x => x.ProductId == saved.ProductId);
        Assert.Equal(recipe.id, row.PublishedRecipeId);
        Assert.Equal(1, row.PublishedVersion);

        await service.SaveAsync(new DishInput(seed.ShopId, saved.ProductId, "酸菜白肉锅" + key, 68m, saved.CategoryId, null, false));
        Assert.DoesNotContain((await service.ListAsync(seed.ShopId)).Dishes, x => x.ProductId == saved.ProductId);
    }

    [FnbSqlServerFact]
    public async Task SaveDishWithNameOnlyUsesZeroPriceAndDefaultCategoryAndKeepsThemOnRename()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        string key = Guid.NewGuid().ToString("N")[..8];
        var service = new FnbDishService(db);
        var saved = await service.SaveAsync(new DishInput(seed.ShopId, 0, "榛果饮" + key, null, null, null, true));
        Assert.Equal(0m, saved.SalePrice);
        Assert.Equal(FnbDishService.DefaultCategoryName, saved.CategoryName);
        Assert.NotNull(saved.SpecId);
        var second = await service.SaveAsync(new DishInput(seed.ShopId, 0, "香草拿铁" + key, null, null, null, true));
        Assert.Equal(saved.CategoryId, second.CategoryId);

        // 已有菜品只改名：售价和分类保持原值
        var priced = await service.SaveAsync(new DishInput(seed.ShopId, 0, "酸菜锅" + key, 68m, null, "热菜" + key, true));
        var renamed = await service.SaveAsync(new DishInput(seed.ShopId, priced.ProductId, "酸菜白肉锅" + key, null, null, null, true));
        Assert.Equal((68m, priced.CategoryId), (renamed.SalePrice, renamed.CategoryId));
        Assert.Equal("酸菜白肉锅" + key, (await db.product.AsNoTracking().SingleAsync(x => x.id == priced.ProductId)).name);
    }

    [FnbSqlServerFact]
    public async Task SaveDishRejectsNonRestaurantCategoryAndOtherShopDish()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var other = await SeedAsync(db);
        string key = Guid.NewGuid().ToString("N")[..8];
        var retail = new Category { biz_type = "零售", name = "零售" + key, valid = 1 };
        db.category.Add(retail);
        await db.SaveChangesAsync();
        var service = new FnbDishService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new DishInput(seed.ShopId, 0, "菜" + key, 1m, retail.id, null, true)));
        var foreign = await service.SaveAsync(new DishInput(other.ShopId, 0, "别店菜" + key, 1m, null, "热菜" + key, true));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(new DishInput(seed.ShopId, foreign.ProductId, "改名" + key, 1m, foreign.CategoryId, null, true)));
    }

    [FnbSqlServerFact]
    public async Task DeletedCategoryNameCanBeReusedButValidSiblingsStayUnique()
    {
        await using var db = OpenTestDatabase();
        string name = "冻品" + Guid.NewGuid().ToString("N")[..6];
        var service = new FnbCategoryService(db);
        var first = new FnbMaterialCategory { level = 1, name = name, valid = true };
        db.fnbMaterialCategory.Add(first);
        await db.SaveChangesAsync();
        Assert.True(await service.NameTakenAsync(0, null, name));
        Assert.False(await service.NameTakenAsync(first.id, null, name));

        // 未删除的一级分类之间，数据库同样拒绝重名（parent_id 为 NULL 也算同级）
        db.fnbMaterialCategory.Add(new FnbMaterialCategory { level = 1, name = name, valid = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // 删掉后只剩已删除的同名分类：不算重名；让出名称后可直接再建（唯一索引本身不区分 valid）
        await service.DeleteAsync(first.id);
        Assert.False(await service.NameTakenAsync(0, null, name));
        await service.FreeNameAsync(0, null, name);
        var again = new FnbMaterialCategory { level = 1, name = name, valid = true };
        db.fnbMaterialCategory.Add(again);
        await db.SaveChangesAsync();
        Assert.NotEqual(first.id, again.id);
        var old = await db.fnbMaterialCategory.AsNoTracking().SingleAsync(x => x.id == first.id);
        Assert.Equal($"{name}（已删除#{first.id}）", old.name);
        Assert.False(old.valid);
    }

    [FnbSqlServerFact]
    public async Task DeleteCategoryRejectsWhileValidMaterialsRemainAndCascadesFromParent()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        int childId = (await db.fnbMaterialItem.AsNoTracking().SingleAsync(x => x.id == seed.RawItemId)).category_id;
        int parentId = (await db.fnbMaterialCategory.AsNoTracking().SingleAsync(x => x.id == childId)).parent_id!.Value;
        var empty = new FnbMaterialCategory { parent_id = parentId, level = 2, name = "空小类" + seed.ShopId,
            default_storage = "chilled", valid = true };
        db.fnbMaterialCategory.Add(empty);
        await db.SaveChangesAsync();
        var service = new FnbCategoryService(db);
        async Task<bool> Valid(int id) => (await db.fnbMaterialCategory.AsNoTracking().SingleAsync(x => x.id == id)).valid;

        var child = await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(childId));
        Assert.Contains("1 种可用食材", child.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(parentId));
        Assert.True(await Valid(childId));
        Assert.True(await Valid(parentId));

        Assert.Equal(new[] { empty.id }, await service.DeleteAsync(empty.id));
        Assert.False(await Valid(empty.id));
        Assert.True(await Valid(parentId));

        await db.fnbMaterialItem.Where(x => x.id == seed.RawItemId).ExecuteUpdateAsync(s => s.SetProperty(x => x.valid, false));
        Assert.Equal(new[] { parentId, childId }, (await service.DeleteAsync(parentId)).Order());
        Assert.False(await Valid(parentId));
        Assert.False(await Valid(childId));
        Assert.Equal(childId, (await db.fnbMaterialItem.AsNoTracking().SingleAsync(x => x.id == seed.RawItemId)).category_id);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(parentId));
    }

    [FnbSqlServerFact]
    public async Task PreparedCategoryIsInheritedFromParentAndTypeChangeNeedsMatchingMaterials()
    {
        await using var db = OpenTestDatabase();
        var seed = await SeedAsync(db);
        var service = new FnbCategoryService(db);
        int childId = (await db.fnbMaterialItem.AsNoTracking().SingleAsync(x => x.id == seed.RawItemId)).category_id;
        var child = await db.fnbMaterialCategory.AsTracking().SingleAsync(x => x.id == childId);
        var parent = await db.fnbMaterialCategory.AsTracking().SingleAsync(x => x.id == child.parent_id);
        Assert.False(await service.IsPreparedAsync(childId));
        // 已有原料食材（面粉）的分类：二级、一级都不能改成半成品
        Assert.Contains("1 种原料食材", await service.TypeChangeConflictAsync(child, true));
        Assert.Contains("1 种原料食材", await service.TypeChangeConflictAsync(parent, true));
        Assert.Null(await service.TypeChangeConflictAsync(child, false));

        // 一级半成品分类下新建的二级分类算半成品
        var top = new FnbMaterialCategory { level = 1, name = "半成品" + seed.ShopId, is_prepared = true, valid = true };
        db.fnbMaterialCategory.Add(top);
        await db.SaveChangesAsync();
        var sauce = new FnbMaterialCategory { parent_id = top.id, level = 2, name = "酱料" + seed.ShopId, default_storage = "chilled", valid = true };
        db.fnbMaterialCategory.Add(sauce);
        await db.SaveChangesAsync();
        Assert.True(await service.IsPreparedAsync(sauce.id));
        Assert.Null(await service.TypeChangeConflictAsync(sauce, false));

        // 其下已有半成品食材：一级不能改回原料；二级自己也标了半成品后，一级改回原料不影响它
        db.fnbMaterialItem.Add(new FnbMaterialItem { code = "sauce" + seed.ShopId, name = "秘制酱", category_id = sauce.id,
            item_type = "prepared", base_unit_code = "g", default_input_unit_code = "g", valid = true });
        await db.SaveChangesAsync();
        Assert.Contains("1 种半成品食材", await service.TypeChangeConflictAsync(top, false));
        sauce.is_prepared = true;
        await db.SaveChangesAsync();
        Assert.Null(await service.TypeChangeConflictAsync(top, false));
        top.is_prepared = false;
        await db.SaveChangesAsync();
        Assert.True(await service.IsPreparedAsync(sauce.id));
        Assert.False(await service.IsPreparedAsync(top.id));
    }
}

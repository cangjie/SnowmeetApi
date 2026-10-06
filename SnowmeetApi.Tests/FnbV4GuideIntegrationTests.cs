using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;
namespace SnowmeetApi.Tests;

public partial class FnbSqlServerIntegrationTests
{
    [FnbSqlServerFact]
    public async Task V4Guide32StepsPizzaLatteFromSetupThroughExport()
    {
        var s = await SeedAsync(); DateTime today = DateTime.UtcNow.AddHours(8).Date;
        // 1–6: an empty store, two-level areas, real photos, and opening-check configuration.
        var home = Data(await Workflow(s, db => new FnbHomeController(db).GetHome(s.Mini, s.ShopId))); Assert.Equal(0, home.GetProperty("batchCount").GetInt32());
        Data(await Workflow(s, db => new FnbAreaController(db).ListAreas(s.Mini, s.ShopId)));
        int root = Data(await Workflow(s, db => new FnbAreaController(db).SaveArea(s.Mini, new(s.ShopId, 0, null, "吧台", "front")))).GetProperty("id").GetInt32();
        int bar = Data(await Workflow(s, db => new FnbAreaController(db).SaveArea(s.Mini, new(s.ShopId, 0, root, "冷藏柜", "cold")))).GetProperty("id").GetInt32();
        int photo;
        await using (var db = Open()) { var f = new SnowmeetApi.Models.UploadFile { staff_id = s.ManagerId, purpose = "食材批次", file_path_name = "/upload/guide-mock.jpg", create_date = DateTime.UtcNow }; db.UploadFile.Add(f); await db.SaveChangesAsync(); photo = f.id; }
        Data(await Workflow(s, db => new FnbAreaController(db).AddPhoto(s.Mini, new(s.ShopId, root, photo))));
        int temperature = Data(await Workflow(s, db => new FnbCheckController(db).SaveItem(s.Mini, new(s.ShopId, 0, bar, "冷藏温度", "number", Minimum: 0, Maximum: 5)))).GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbCheckController(db).SaveItem(s.Mini, new(s.ShopId, 0, bar, "烤炉点火"))));
        async Task<int> Cat(string name, string code, string measure, bool prepared = false) => Data(await Workflow(s, db => new FnbCatalogController(db).SaveCategory(s.Mini,
            new(s.ShopId, 0, s.ParentId, 2, name, code + s.ItemId, measure, "chilled", 1, 3, prepared)))).GetProperty("id").GetInt32();
        int milkCategory = await Cat("鲜奶", "MLK", "volume"), cheeseCategory = await Cat("奶酪", "CHZ", "weight"), flourCategory = await Cat("面粉", "FLR", "weight"),
            oilCategory = await Cat("食用油", "OIL", "volume"), coffeeCategory = await Cat("咖啡豆", "COF", "weight"), greenCategory = await Cat("叶菜", "VEG", "weight"), prepCategory = await Cat("面团饼底", "DOU", "count", true);
        async Task<int> NewItem(int category, string name, string unit) => Data(await Workflow(s, db => new FnbRouteController(db).CreateItem(s.Mini, new(s.ShopId, category, name, name + "出品态", unit)))).GetProperty("id").GetInt32();
        async Task Up(int item, string name, string unit, decimal size, string code, string op, decimal yield = 1, decimal hours = 0, int? days = null) =>
            Data(await Workflow(s, db => new FnbRouteController(db).AddUpstreamForm(s.Mini, new(s.ShopId, item, name, unit, size, "chilled", code, op, yield, hours, days))));
        async Task<int> Purchase(int item, int sequence, string name)
        { await using var db = Open(); int form = await db.fnbItemForm.Where(x => x.item_id == item && x.seq == sequence).Select(x => x.id).SingleAsync(); return Data(await new FnbRouteController(db).SaveSpec(s.Mini, new(s.ShopId, item, 0, form, name))).GetProperty("id").GetInt32(); }
        // 7–13: classification defaults, backwards-built chains, entry specs and 24-hour thawing.
        int milk = await NewItem(milkCategory, "鲜牛奶", "ml"); await Up(milk, "独立桶", "桶", 2000, "B", "开盖", .98m, days: 3); await Up(milk, "整箱", "箱", 12000, "C", "拆箱");
        int milkBox = await Purchase(milk, 0, "整箱"), milkBucket = await Purchase(milk, 1, "单桶");
        Data(await Workflow(s, db => new FnbRouteController(db).GetRoute(s.Mini, s.ShopId, milk)));
        int beef = s.ItemId; await Up(beef, "真空原块", "袋", 2000, "B", "修剪切片", .95m, days: 3); await Up(beef, "整箱", "箱", 10000, "C", "拆箱"); int beefSpec = await Purchase(beef, 0, "整箱");
        int cheese = await NewItem(cheeseCategory, "马苏里拉", "g"); await Up(cheese, "解冻块", "块", 3000, "T", "刨丝", .98m, days: 3); await Up(cheese, "冷冻块", "块", 3000, "FZ", "冷藏解冻", hours: 24); await Up(cheese, "整箱", "箱", 12000, "C", "拆箱"); int cheeseSpec = await Purchase(cheese, 0, "整箱");
        int flour = await NewItem(flourCategory, "面粉", "g"); await Up(flour, "整袋", "袋", 5000, "B", "拆袋"); int flourSpec = await Purchase(flour, 0, "整袋");
        int oil = await NewItem(oilCategory, "橄榄油", "ml"); await Up(oil, "整桶", "桶", 5000, "B", "分装", .99m, days: 30); int oilSpec = await Purchase(oil, 0, "整桶");
        int coffee = await NewItem(coffeeCategory, "咖啡豆", "g"); await Up(coffee, "密封袋", "袋", 1000, "B", "入豆仓"); await Up(coffee, "整箱", "箱", 10000, "C", "拆箱"); int coffeeSpec = await Purchase(coffee, 0, "整箱");
        int greens = await NewItem(greenCategory, "芝麻菜", "g");
        // 14–18: prep BOM, per-spec dish recipes, supplies independent of recipe consumption.
        int dough = Data(await Workflow(s, db => new FnbPrepController(db).CreatePrep(s.Mini, new(s.ShopId, prepCategory, "发酵披萨饼底", "个", "piece", 6)))).GetProperty("item").GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbPrepController(db).SavePrepBom(s.Mini, new(s.ShopId, dough, 6, [new(flour, 1000), new(oil, 20)]))));
        int pizza = Data(await Workflow(s, db => new FnbDishController(db).CreateDish(s.Mini, new(s.ShopId, "帕斯雀牛肉披萨", "9 寸")))).GetProperty("spec").GetProperty("id").GetInt32();
        int latte = Data(await Workflow(s, db => new FnbDishController(db).CreateDish(s.Mini, new(s.ShopId, "热拿铁", "400 ml")))).GetProperty("spec").GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbDishController(db).SaveSpecLines(s.Mini, new(s.ShopId, pizza, 1, [new(dough, 1), new(beef, 65), new(cheese, 120), new(oil, 10), new(greens, 15)]))));
        Data(await Workflow(s, db => new FnbDishController(db).SaveSpecLines(s.Mini, new(s.ShopId, latte, 1, [new(coffee, 18), new(milk, 250)]))));
        int cup = Data(await Workflow(s, db => new FnbSupplyController(db).SaveSupply(s.Mini, new(s.ShopId, 0, "400 ml 热饮纸杯", "disposable", 50, "条", bar)))).GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbSupplyController(db).PostMovement(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), cup, "in", 2))));
        // 19–21: one multi-line receipt, entry-spec conversions and stored batch photographs.
        FnbReceiptLine R(int item, int? spec, decimal quantity, decimal amount, int area) => new(item, spec, quantity, amount, "chilled", today, today.AddDays(30), area);
        var receipt = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, new(s.ShopId, Guid.NewGuid(), [
            R(milk, milkBox, 2, 240, s.AreaId) with { PhotoIds = [photo] }, R(milk, milkBucket, 2, 40, bar), R(beef, beefSpec, 1, 1800, s.AreaId), R(cheese, cheeseSpec, 1, 840, s.AreaId),
            R(flour, flourSpec, 1, 30, s.AreaId), R(oil, oilSpec, 1, 100, s.AreaId), R(coffee, coffeeSpec, 1, 200, s.AreaId), R(greens, null, 500, 10, bar)]))));
        int Bid(int index) => receipt.GetProperty("batches")[index].GetProperty("batch").GetProperty("id").GetInt32();
        Assert.Equal(24000, receipt.GetProperty("batches")[0].GetProperty("batch").GetProperty("quantity").GetDecimal());
        async Task<int> Op(int batch, decimal input, decimal actual, int area) => Data(await Workflow(s, db => new FnbOperationController(db).PostOperation(s.WorkerMini,
            new(s.ShopId, Guid.NewGuid(), batch, input, actual, area)))).GetProperty("batch").GetProperty("batch").GetProperty("id").GetInt32();
        // 22–25: adjacent operations, real yield deviations, and natural time readiness without completion.
        Data(await Workflow(s, db => new FnbOperationController(db).GetWorkbench(s.Mini, s.ShopId)));
        int beefBags = await Op(Bid(2), 1, 5, s.AreaId); await Op(beefBags, 1, 1880, bar);
        int cheeseBlocks = await Op(Bid(3), 1, 4, s.AreaId); int thaw = await Op(cheeseBlocks, 1, 1, bar);
        Assert.Equal(1, (await Workflow(s, db => new FnbOperationController(db).PostOperation(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), thaw, 1, 2910, bar)))).code);
        await using (var db = Open()) { var b = await db.fnbBatch.SingleAsync(x => x.id == thaw); b.ready_at = DateTime.UtcNow.AddSeconds(-1); db.Entry(b).State = EntityState.Modified; await db.SaveChangesAsync(); }
        await Op(thaw, 1, 2910, bar); await Op(Bid(4), 1, 5000, bar); await Op(Bid(5), 1, 4950, bar);
        int coffeeBags = await Op(Bid(6), 1, 10, bar); await Op(coffeeBags, 1, 1000, bar); await Op(Bid(0), 2, 12, s.AreaId); await Op(Bid(1), 1, 1960, bar);
        // 26–29: two prep batches, opening check, an adjusted order, and concrete batch movements.
        Data(await Workflow(s, db => new FnbPrepController(db).PostPreparation(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), dough, 2, bar, today.AddDays(3)))));
        long sheet = long.Parse(Data(await Workflow(s, db => new FnbCheckController(db).StartToday(s.WorkerMini, new(s.ShopId, Guid.NewGuid())))).GetProperty("sheet").GetProperty("id").GetString()!);
        Data(await Workflow(s, db => new FnbCheckController(db).PassAll(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sheet))));
        Data(await Workflow(s, db => new FnbCheckController(db).SaveDraft(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sheet, [new(temperature, "pass", 3)]))));
        Data(await Workflow(s, db => new FnbCheckController(db).Submit(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sheet)))); Data(await Workflow(s, db => new FnbCheckController(db).Confirm(s.Mini, new(s.ShopId, Guid.NewGuid(), sheet))));
        long order = long.Parse(Data(await Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), [new(pizza, 2), new(latte, 2)], "A1 桌", "一份少放芝麻菜")))).GetProperty("order").GetProperty("id").GetString()!);
        Data(await Workflow(s, db => new FnbServeController(db).PreviewServe(s.WorkerMini, s.ShopId, order)));
        Data(await Workflow(s, db => new FnbServeController(db).PostServe(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), order, [new(dough, 2), new(beef, 130), new(cheese, 240), new(oil, 20), new(greens, 15), new(coffee, 36), new(milk, 500)]))));
        Data(await Workflow(s, db => new FnbServeController(db).ListServeLog(s.WorkerMini, s.ShopId)));
        // 30–32: count only usable final stock, loss posting, low-stock recommendation and xlsx export.
        var snapshot = Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.Mini, new(s.ShopId, Guid.NewGuid())))); long snapshotId = long.Parse(snapshot.GetProperty("document").GetProperty("id").GetString()!);
        var counts = snapshot.GetProperty("lines").EnumerateArray().Select(l => { int id = l.GetProperty("item_id").GetInt32(); decimal count = id == milk ? 1200 : id == beef ? 1740 : id == oil ? 4880 : l.GetProperty("system_qty").GetDecimal(); return new FnbCountLine(id, count); }).ToArray();
        Data(await Workflow(s, db => new FnbV4StocktakeController(db).SaveCount(s.Mini, new(s.ShopId, Guid.NewGuid(), snapshotId, counts))));
        Data(await Workflow(s, db => new FnbV4StocktakeController(db).PostStocktake(s.Mini, new(s.ShopId, Guid.NewGuid(), snapshotId))));
        Data(await Workflow(s, db => new FnbStockController(db).SaveLowStockRule(s.Mini, new(s.ShopId, milk, null, 2000))));
        await using var final = Open(); var stock = await new FnbV4Service(final, s.ShopId).Stock();
        Assert.Equal(1200, stock.Single(x => x.ItemId == milk).AvailableQuantity); Assert.Equal(26000, stock.Single(x => x.ItemId == milk).StagedQuantity); Assert.True(stock.Single(x => x.ItemId == milk).LowStock);
        Assert.Equal(1740, stock.Single(x => x.ItemId == beef).AvailableQuantity); Assert.Equal(10, stock.Single(x => x.ItemId == dough).AvailableQuantity); Assert.Equal(2670, stock.Single(x => x.ItemId == cheese).AvailableQuantity);
        Assert.Equal(100, (await final.fnbSupply.SingleAsync(x => x.id == cup)).quantity);
        var rows = await new FnbV4Service(final, s.ShopId).RecipeChain(); Assert.Equal(7, rows.Length); Assert.Equal(2, rows.Single(x => x.ItemId == milk).Steps.Length);
        byte[] bytes = FnbV4Service.ExportRecipeChain(rows); using var book = new XSSFWorkbook(new MemoryStream(bytes)); Assert.Equal(8, book.GetSheetAt(0).PhysicalNumberOfRows);
        Data(await Workflow(s, db => new FnbV4ReportController(db).GetDashboard(s.Mini, s.ShopId)));
    }
}

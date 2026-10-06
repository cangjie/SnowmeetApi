using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;
public partial class FnbSqlServerIntegrationTests
{
    private static JsonElement Data(ApiResult<object> r)
    { Assert.True(r.code == 0, $"code={r.code}: {r.message}"); return JsonSerializer.SerializeToElement(r.data, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
    private static async Task<ApiResult<object>> Workflow(Seed s, Func<ApplicationDBContext, Task<ApiResult<object>>> action)
    { await using var db = Open(); return await action(db); }
    private static FnbReceiptRequest Receive(Seed s, decimal qty = 1000, decimal amount = 100, DateTime? expire = null) =>
        new(s.ShopId, Guid.NewGuid(), [new(s.ItemId, null, qty, amount, "chilled", DateTime.UtcNow.AddHours(8).Date, expire ?? DateTime.UtcNow.AddHours(8).Date.AddDays(20), s.AreaId)]);

    [FnbSqlServerFact]
    public async Task V4ReceiptReplayConflictRollbackAndCancellation()
    {
        var s = await SeedAsync(); var r = Receive(s); var first = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.WorkerMini, r)));
        var replay = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.WorkerMini, r)));
        Assert.Equal(first.ToString(), replay.ToString()); int bid = first.GetProperty("batches")[0].GetProperty("batch").GetProperty("id").GetInt32();
        Assert.Equal(4, (await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, r with { Lines = [r.Lines[0] with { Quantity = 2000 }] }))).code);
        var invalid = r with { RequestId = Guid.NewGuid(), Lines = [r.Lines[0], r.Lines[0] with { Amount = -1 }] };
        Assert.Equal(1, (await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, invalid))).code);
        await using (var db = Open()) { Assert.Equal(1, await db.fnbBatch.CountAsync(x => x.shop_id == s.ShopId)); Assert.Equal(1000, (await db.fnbBatch.SingleAsync(x => x.id == bid)).quantity); }
        long did = long.Parse(first.GetProperty("documentId").GetString()!);
        Data(await Workflow(s, db => new FnbInboundController(db).DeleteReceipt(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), did))));
        await using (var db = Open()) { Assert.Equal(0, (await db.fnbBatch.SingleAsync(x => x.id == bid)).quantity); Assert.Equal("cancelled", (await db.fnbStockDocument.SingleAsync(x => x.id == did)).status); }
    }
    [FnbSqlServerFact]
    public async Task V4StockWritesRejectEmptyIdsAndConcurrentOverdraw()
    {
        var s = await SeedAsync(); Assert.Equal(1, (await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s) with { RequestId = Guid.Empty }))).code);
        int b = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s, 100, 100)))).GetProperty("batches")[0].GetProperty("batch").GetProperty("id").GetInt32();
        var requests = new[] { new FnbBatchWriteRequest(s.ShopId, Guid.NewGuid(), b, 80, "破损"), new FnbBatchWriteRequest(s.ShopId, Guid.NewGuid(), b, 80, "破损") };
        var results = await Task.WhenAll(requests.Select(r => Workflow(s, db => new FnbStockController(db).PostWaste(s.Mini, r))));
        Assert.Single(results.Where(x => x.code == 0)); Assert.Contains(results, x => x.code is 1 or 4);
        await using var check = Open(); var batch = await check.fnbBatch.SingleAsync(x => x.id == b); Assert.Equal(20, batch.quantity); Assert.Equal(20, batch.amount);
    }
    [FnbSqlServerFact]
    public async Task V4SealedOpenExpiryAndConfirmedDestructionPreserveCost()
    {
        var s = await SeedAsync(); DateTime today = DateTime.UtcNow.AddHours(8).Date;
        var r = Receive(s, 2, 40, today.AddDays(20)) with { Lines = [Receive(s).Lines[0] with { Quantity = 2, Amount = 40, Sealed = true, PackSize = 100, PackLabel = "盒", OpenStorage = "chilled", OpenDays = 0 }] };
        int bid = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.WorkerMini, r))).GetProperty("batches")[0].GetProperty("batch").GetProperty("id").GetInt32();
        Assert.Equal(1, (await Workflow(s, db => new FnbOperationController(db).PostOperation(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), bid, .5m, 50, s.AreaId)))).code);
        var opened = Data(await Workflow(s, db => new FnbOperationController(db).PostOperation(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), bid, 1, 100, s.AreaId))));
        int output = opened.GetProperty("batch").GetProperty("batch").GetProperty("id").GetInt32();
        var label = Data(await Workflow(s, db => new FnbLabelController(db).GetLabelData(s.WorkerMini, s.ShopId, output))); Assert.Equal(today.ToString("yyyy-MM-dd"), label.GetProperty("expireDate").GetString());
        Assert.Equal(1, (await Workflow(s, db => new FnbStockController(db).PostDestroy(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), output, 100, "过期", true)))).code);
        await using (var db = Open()) { var b0 = await db.fnbBatch.SingleAsync(x => x.id == output); b0.op_date = today.AddDays(-1); b0.op_expire_date = today.AddDays(-1); db.Entry(b0).State = EntityState.Modified; await db.SaveChangesAsync(); }
        Assert.Equal(1, (await Workflow(s, db => new FnbStockController(db).PostDestroy(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), output, 100, "过期", false)))).code);
        Data(await Workflow(s, db => new FnbStockController(db).PostDestroy(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), output, 100, "过期", true))));
        await using var final = Open(); var source = await final.fnbBatch.SingleAsync(x => x.id == bid); Assert.Equal(100, source.quantity); Assert.Equal(20, source.amount);
        Assert.Equal("destroyed", (await final.fnbBatch.SingleAsync(x => x.id == output)).dispose_status);
    }
    [FnbSqlServerFact]
    public async Task V4AdjacentOperationsConvertCostsExpiryAndTimedAvailabilityForBothSessions()
    {
        var s = await SeedAsync();
        Data(await Route(s, c => c.AddUpstreamForm(s.Mini, new(s.ShopId, s.ItemId, "袋", "袋", 2000, "frozen", "BAG", "切片", .94m, 24, 3))));
        int spec;
        await using (var db = Open()) { var from = await db.fnbItemForm.SingleAsync(x => x.item_id == s.ItemId && x.seq == 0); spec = Data(await new FnbRouteController(db).SaveSpec(s.Mini, new(s.ShopId, s.ItemId, 0, from.id, "真空袋"))).GetProperty("id").GetInt32(); }
        var r = Receive(s) with { Lines = [new(s.ItemId, spec, 2, 200, "frozen", DateTime.UtcNow.AddHours(8).Date, DateTime.UtcNow.AddHours(8).Date.AddDays(20), s.AreaId)] };
        int bid = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.WorkerMini, r))).GetProperty("batches")[0].GetProperty("batch").GetProperty("id").GetInt32();
        var preview = Data(await Workflow(s, db => new FnbOperationController(db).PreviewOperation(s.Mini, s.ShopId, bid, 1)));
        Assert.Equal(1880, preview.GetProperty("expectedQuantity").GetDecimal());
        var op = Data(await Workflow(s, db => new FnbOperationController(db).PostOperation(s.WeCom, new(s.ShopId, Guid.NewGuid(), bid, 1, 1880, s.AreaId))));
        long opid = long.Parse(op.GetProperty("operation").GetProperty("id").GetString()!); int output = op.GetProperty("operation").GetProperty("output_batch_id").GetInt32();
        Assert.False(op.GetProperty("batch").GetProperty("available").GetBoolean());
        Assert.Equal(3, (await Workflow(s, db => new FnbOperationController(db).CompleteOperation(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), opid)))).code);
        Data(await Workflow(s, db => new FnbOperationController(db).CompleteOperation(s.Mini, new(s.ShopId, Guid.NewGuid(), opid))));
        var op2 = Data(await Workflow(s, db => new FnbOperationController(db).PostOperation(s.Mini, new(s.ShopId, Guid.NewGuid(), bid, .5m, 940, s.AreaId))));
        Assert.NotEqual(op.GetProperty("batch").GetProperty("batch").GetProperty("batch_no").GetString(), op2.GetProperty("batch").GetProperty("batch").GetProperty("batch_no").GetString());
        await using var check = Open(); var b = await check.fnbBatch.SingleAsync(x => x.id == output); Assert.Equal(1880, b.quantity); Assert.Equal(100, b.amount); Assert.Equal(DateTime.UtcNow.AddHours(8).Date.AddDays(3), b.effective_expire);
        var docs = await check.fnbStockDocument.Where(x => x.shop_id == s.ShopId && x.document_type == "op").OrderBy(x => x.id).ToArrayAsync(); Assert.Equal("wecom", docs[0].source_client); Assert.Equal("mini", docs[1].source_client);
        long receiptId = await check.fnbStockDocument.Where(x => x.shop_id == s.ShopId && x.request_id == r.RequestId).Select(x => x.id).SingleAsync();
        Assert.Equal(1, (await Workflow(s, db => new FnbInboundController(db).DeleteReceipt(s.Mini, new(s.ShopId, Guid.NewGuid(), receiptId)))).code);
    }
    [FnbSqlServerFact]
    public async Task V4FefoServingShortageAndRecipeSnapshotKeepBalancesNonnegative()
    {
        var s = await SeedAsync(); DateTime today = DateTime.UtcNow.AddHours(8).Date;
        int late = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s, 100, 20, today.AddDays(5))))).GetProperty("batches")[0].GetProperty("batch").GetProperty("id").GetInt32();
        int early = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s, 50, 10, today.AddDays(1))))).GetProperty("batches")[0].GetProperty("batch").GetProperty("id").GetInt32();
        int spec = Data(await Workflow(s, db => new FnbDishController(db).CreateDish(s.Mini, new(s.ShopId, "披萨", "9 寸")))).GetProperty("spec").GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbDishController(db).SaveSpecLines(s.Mini, new(s.ShopId, spec, 1, [new(s.ItemId, 80)]))));
        long order = long.Parse(Data(await Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), [new(spec, 1)])))).GetProperty("order").GetProperty("id").GetString()!);
        // Publishing a new recipe must not alter the pending order's recipe snapshot.
        Data(await Workflow(s, db => new FnbDishController(db).SaveSpecLines(s.Mini, new(s.ShopId, spec, 1, [new(s.ItemId, 200)]))));
        var served = Data(await Workflow(s, db => new FnbServeController(db).PostServe(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), order)))); Assert.Equal(16, served.GetProperty("cost").GetDecimal());
        await using (var db = Open()) { Assert.Equal(0, (await db.fnbBatch.SingleAsync(x => x.id == early)).quantity); Assert.Equal(70, (await db.fnbBatch.SingleAsync(x => x.id == late)).quantity); }
        long second = long.Parse(Data(await Workflow(s, db => new FnbServeController(db).CreateOrder(s.Mini, new(s.ShopId, Guid.NewGuid(), [new(spec, 1)])))).GetProperty("order").GetProperty("id").GetString()!);
        var shortage = Data(await Workflow(s, db => new FnbServeController(db).PostServe(s.Mini, new(s.ShopId, Guid.NewGuid(), second)))); Assert.Equal(130, shortage.GetProperty("lines")[0].GetProperty("shortage_qty").GetDecimal());
        Assert.Equal(1, (await Workflow(s, db => new FnbServeController(db).PostServe(s.Mini, new(s.ShopId, Guid.NewGuid(), second)))).code);
        await using var final = Open(); Assert.All(await final.fnbBatch.Where(x => x.shop_id == s.ShopId).ToArrayAsync(), b => Assert.True(b.quantity >= 0 && b.amount >= 0));
    }
    [FnbSqlServerFact]
    public async Task V4StocktakeRejectsStaleSnapshotAndPostsLossAndGain()
    {
        var s = await SeedAsync(); Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s, 100))));
        long snapshot = long.Parse(Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.WorkerMini, new(s.ShopId, Guid.NewGuid())))).GetProperty("document").GetProperty("id").GetString()!);
        Data(await Workflow(s, db => new FnbV4StocktakeController(db).SaveCount(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), snapshot, [new(s.ItemId, 80)]))));
        Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s, 10))));
        Assert.Equal(4, (await Workflow(s, db => new FnbV4StocktakeController(db).PostStocktake(s.Mini, new(s.ShopId, Guid.NewGuid(), snapshot)))).code);
        long next = long.Parse(Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.Mini, new(s.ShopId, Guid.NewGuid())))).GetProperty("document").GetProperty("id").GetString()!);
        Data(await Workflow(s, db => new FnbV4StocktakeController(db).SaveCount(s.Mini, new(s.ShopId, Guid.NewGuid(), next, [new(s.ItemId, 120)]))));
        Data(await Workflow(s, db => new FnbV4StocktakeController(db).PostStocktake(s.Mini, new(s.ShopId, Guid.NewGuid(), next))));
        await using var db0 = Open(); Assert.Equal(120, await db0.fnbBatch.Where(x => x.shop_id == s.ShopId && x.item_id == s.ItemId).SumAsync(x => x.quantity));
    }
    [FnbSqlServerFact]
    public async Task V4AreasSuppliesAndToolsEnforceBindingsAuditAndPermissions()
    {
        var s = await SeedAsync();
        Assert.Equal(3, (await Workflow(s, db => new FnbAreaController(db).SaveArea(s.WorkerMini, new(s.ShopId, 0, null, "无权新建")))).code);
        Assert.Equal(1, (await Workflow(s, db => new FnbAreaController(db).SaveArea(s.Mini, new(s.ShopId, 0, s.AreaId, "第三层")))).code);
        int supply = Data(await Workflow(s, db => new FnbSupplyController(db).SaveSupply(s.Mini, new(s.ShopId, 0, "纸杯", "disposable", 50, "条", s.AreaId)))).GetProperty("id").GetInt32();
        var receipt = new FnbSupplyPostRequest(s.ShopId, Guid.NewGuid(), supply, "in", 2);
        var first = Data(await Workflow(s, db => new FnbSupplyController(db).PostMovement(s.WorkerMini, receipt)));
        Data(await Workflow(s, db => new FnbSupplyController(db).PostMovement(s.WorkerMini, receipt)));
        Assert.Equal(100, first.GetProperty("supply").GetProperty("quantity").GetDecimal());
        Data(await Workflow(s, db => new FnbSupplyController(db).PostMovement(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), supply, "out", 20, "外卖"))));
        Assert.Equal(1, (await Workflow(s, db => new FnbSupplyController(db).UndoReceipt(s.Mini, new(s.ShopId, Guid.NewGuid(), long.Parse(first.GetProperty("movement").GetProperty("id").GetString()!))))).code);
        Assert.Equal(1, (await Workflow(s, db => new FnbSupplyController(db).PostMovement(s.Mini, new(s.ShopId, Guid.NewGuid(), supply, "waste", 100, "受潮")))).code);
        int tool = Data(await Workflow(s, db => new FnbToolController(db).SaveTool(s.Mini, new(s.ShopId, 0, "磨豆机", 1, AreaId: s.AreaId, DailyCheck: true)))).GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbToolController(db).ChangeStatus(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), tool, "missing"))));
        Assert.Equal(1, (await Workflow(s, db => new FnbToolController(db).ChangeStatus(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), tool, "normal")))).code);
        Data(await Workflow(s, db => new FnbToolController(db).ChangeStatus(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), tool, "normal", Remark: "已找到"))));
        Data(await Workflow(s, db => new FnbToolController(db).ChangeStatus(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), tool, "damaged"))));
        Assert.Equal(3, (await Workflow(s, db => new FnbToolController(db).ChangeStatus(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), tool, "disposed", Remark: "损坏")))).code);
        Data(await Workflow(s, db => new FnbToolController(db).ChangeStatus(s.Mini, new(s.ShopId, Guid.NewGuid(), tool, "disposed", Remark: "无法维修"))));
        Assert.Equal(1, (await Workflow(s, db => new FnbAreaController(db).DeleteArea(s.Mini, new(s.ShopId, s.AreaId)))).code);
        await using var db0 = Open(); Assert.Equal(80, (await db0.fnbSupply.SingleAsync(x => x.id == supply)).quantity);
        Assert.Equal(5, await db0.fnbToolLog.CountAsync(x => x.tool_id == tool)); Assert.False((await db0.fnbCheckItem.SingleAsync(x => x.tool_id == tool)).valid);
    }
    [FnbSqlServerFact]
    public async Task V4OpeningCheckSnapshotNumericRulesAndAppendOnlyHandling()
    {
        var s = await SeedAsync();
        int ci = Data(await Workflow(s, db => new FnbCheckController(db).SaveItem(s.Mini, new(s.ShopId, 0, s.AreaId, "冷藏温度", "number", Minimum: 0, Maximum: 5, Unit: "℃")))).GetProperty("id").GetInt32();
        int yes = Data(await Workflow(s, db => new FnbCheckController(db).SaveItem(s.Mini, new(s.ShopId, 0, s.AreaId, "卫生清洁")))).GetProperty("id").GetInt32();
        long sid = long.Parse(Data(await Workflow(s, db => new FnbCheckController(db).StartToday(s.WorkerMini, new(s.ShopId, Guid.NewGuid())))).GetProperty("sheet").GetProperty("id").GetString()!);
        var bulk = Data(await Workflow(s, db => new FnbCheckController(db).PassAll(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid)))); Assert.Equal(1, bulk.GetProperty("filled").GetInt32());
        Assert.Equal(1, (await Workflow(s, db => new FnbCheckController(db).Submit(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid)))).code);
        Data(await Workflow(s, db => new FnbCheckController(db).SaveDraft(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid, [new(ci, "pass", 8)]))));
        Assert.Equal(1, (await Workflow(s, db => new FnbCheckController(db).Submit(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid)))).code);
        Data(await Workflow(s, db => new FnbCheckController(db).SaveDraft(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid, [new(ci, "pass", 8, "刚除霜"), new(yes, "abnormal", Reason: "地面未清洁")]))));
        Data(await Workflow(s, db => new FnbCheckController(db).SaveItem(s.Mini, new(s.ShopId, 0, s.AreaId, "新检查项"))));
        Assert.Equal(4, (await Workflow(s, db => new FnbCheckController(db).Submit(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid)))).code);
        Data(await Workflow(s, db => new FnbCheckController(db).RefreshSnapshot(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid))));
        Data(await Workflow(s, db => new FnbCheckController(db).PassAll(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid))));
        var submitted = Data(await Workflow(s, db => new FnbCheckController(db).Submit(s.WorkerMini, new(s.ShopId, Guid.NewGuid(), sid)))); Assert.Equal(2, submitted.GetProperty("abnormal").GetInt32());
        Assert.Equal(1, (await Workflow(s, db => new FnbCheckController(db).SaveDraft(s.Mini, new(s.ShopId, Guid.NewGuid(), sid, [new(ci, "na")])))).code);
        Assert.Equal(1, (await Workflow(s, db => new FnbCheckController(db).Confirm(s.Mini, new(s.ShopId, Guid.NewGuid(), sid)))).code);
        foreach (var l in submitted.GetProperty("lines").EnumerateArray().Where(x => x.GetProperty("line").GetProperty("result").GetString() == "abnormal"))
        {
            long lineId = long.Parse(l.GetProperty("line").GetProperty("id").GetString()!);
            Data(await Workflow(s, db => new FnbCheckController(db).HandleAbnormal(s.Mini, new(s.ShopId, Guid.NewGuid(), lineId, "已处理"))));
        }
        Data(await Workflow(s, db => new FnbCheckController(db).Confirm(s.Mini, new(s.ShopId, Guid.NewGuid(), sid))));
        await using var check = Open(); Assert.Equal("confirmed", (await check.fnbCheckSheet.SingleAsync(x => x.id == sid)).status); Assert.Equal(2, await check.fnbCheckHandling.CountAsync(x => check.fnbCheckLine.Any(l => l.id == x.line_id && l.sheet_id == sid)));
    }
    private sealed class TestExpirySender : IFnbExpirySender
    {
        public int Count; public bool Fail;
        public Task<(bool Success, string? MessageId, string? Error)> Send(FnbBatch batch, string name, string receivers, string status)
        { Count++; return Task.FromResult((!Fail, (string?)"mock-message", Fail ? "mock-failure" : null)); }
    }
    [FnbSqlServerFact]
    public async Task V4ExpiryPushUsesNewBatchesAndDeduplicatesWithoutRealMessages()
    {
        var s = await SeedAsync(); Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s, 100, 10, DateTime.UtcNow.AddHours(8).Date))));
        var fake = new TestExpirySender();
        await using var db = Open(); var controller = new FnbV4AlertController(db, new ConfigurationBuilder().Build(), fake);
        Data(await controller.PushExpireAlert("mock-receiver")); int calls = fake.Count; Assert.True(calls > 0);
        Data(await controller.PushExpireAlert("mock-receiver")); Assert.Equal(calls, fake.Count);
    }
}

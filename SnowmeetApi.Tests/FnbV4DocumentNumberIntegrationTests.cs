using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Tests;

public partial class FnbSqlServerIntegrationTests
{
    [FnbSqlServerFact]
    public async Task V4ShortStockDocumentNumbersPreserveHistoryDeletionAndReplay()
    {
        var s = await SeedAsync(); string prefix = "STOCKTAKE-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-";
        string historical = prefix + Guid.NewGuid().ToString("N");
        await using (var db = Open())
        {
            db.fnbStockDocument.Add(new FnbStockDocument { shop_id = s.ShopId, document_type = "stocktake", document_no = historical,
                request_id = Guid.NewGuid(), status = "draft", source_client = "mini", business_date = DateTime.UtcNow.AddHours(8).Date,
                occurred_at = DateTime.UtcNow, created_at = DateTime.UtcNow, created_by_staff_id = s.ManagerId });
            await db.SaveChangesAsync();
        }
        var request = new FnbSnapshotRequest(s.ShopId, Guid.NewGuid());
        var first = Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.Mini, request)));
        var replay = Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.Mini, request)));
        Assert.Equal(first.GetProperty("document").GetProperty("id").GetString(), replay.GetProperty("document").GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("document").GetProperty("document_no").GetString(), replay.GetProperty("document").GetProperty("document_no").GetString());
        Assert.Equal(prefix + "0001", first.GetProperty("document").GetProperty("document_no").GetString());
        var second = Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.Mini, new(s.ShopId, Guid.NewGuid()))));
        Assert.Equal(prefix + "0002", second.GetProperty("document").GetProperty("document_no").GetString());
        long secondId = long.Parse(second.GetProperty("document").GetProperty("id").GetString()!);
        await using (var db = Open())
        {
            var document = await db.fnbStockDocument.SingleAsync(x => x.id == secondId); document.valid = false;
            db.Entry(document).State = EntityState.Modified; await db.SaveChangesAsync();
        }
        var third = Data(await Workflow(s, db => new FnbV4StocktakeController(db).CreateSnapshot(s.Mini, new(s.ShopId, Guid.NewGuid()))));
        Assert.Equal(prefix + "0003", third.GetProperty("document").GetProperty("document_no").GetString());
        var receipt = Data(await Workflow(s, db => new FnbInboundController(db).PostReceipt(s.Mini, Receive(s))));
        long receiptId = long.Parse(receipt.GetProperty("documentId").GetString()!);
        await using var final = Open();
        Assert.EndsWith("-0001", (await final.fnbStockDocument.SingleAsync(x => x.id == receiptId)).document_no);
        Assert.True(await final.fnbStockDocument.AnyAsync(x => x.shop_id == s.ShopId && x.document_no == historical));
    }

    [FnbSqlServerFact]
    public async Task V4ShortKitchenNumbersReplayAndRemainDistinctUnderConcurrency()
    {
        var s = await SeedAsync();
        int spec = Data(await Workflow(s, db => new FnbDishController(db).CreateDish(s.Mini, new(s.ShopId, "短号测试菜")))).GetProperty("spec").GetProperty("id").GetInt32();
        Data(await Workflow(s, db => new FnbDishController(db).SaveSpecLines(s.Mini, new(s.ShopId, spec, 1, [new(s.ItemId, 1)]))));
        var request = new FnbOrderCreateRequest(s.ShopId, Guid.Parse("11111111-1111-1111-1111-111111111111"), [new(spec, 1)]);
        var first = Data(await Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, request)));
        var replay = Data(await Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, request)));
        Assert.Equal(first.GetProperty("order").GetProperty("id").GetString(), replay.GetProperty("order").GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("order").GetProperty("display_no").GetString(), replay.GetProperty("order").GetProperty("display_no").GetString());
        string prefix = "SO" + DateTime.UtcNow.AddHours(8).ToString("yyMMdd") + "-";
        Assert.Equal(prefix + "0001", first.GetProperty("order").GetProperty("display_no").GetString());
        var sameGuidPrefix = request with { RequestId = Guid.Parse("11111111-1111-1111-1111-222222222222") };
        Data(await Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, sameGuidPrefix)));
        var requests = Enumerable.Range(0, 3).Select(_ => request with { RequestId = Guid.NewGuid() }).ToArray();
        var results = await Task.WhenAll(requests.Select(r => Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, r))));
        for (int i = 0; i < results.Length; i++)
        {
            Assert.Contains(results[i].code, new[] { 0, 4 });
            if (results[i].code == 4) results[i] = await Workflow(s, db => new FnbServeController(db).CreateOrder(s.WorkerMini, requests[i]));
            Data(results[i]);
        }
        await using var final = Open();
        var numbers = await final.fnbOrder.Where(x => x.shop_id == s.ShopId).Select(x => x.display_no).OrderBy(x => x).ToArrayAsync();
        Assert.Equal(Enumerable.Range(1, 5).Select(n => prefix + n.ToString("D4")), numbers);
    }
}

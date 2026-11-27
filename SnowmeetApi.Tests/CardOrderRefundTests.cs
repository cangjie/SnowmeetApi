using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

// All state lives in a new in-memory database. These tests never submit channel refunds.
public class CardOrderRefundTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ApplicationDBContext Db { get; }
        public CardOrderRefundService Service => new(Db);
        private Fixture(SqliteConnection connection, ApplicationDBContext db) { this.connection = connection; Db = db; }

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
            await db.Database.EnsureCreatedAsync();
            db.order.Add(new Order { id = 10, type = "养护", order_source = CareCardSaleService.SingleOrderSource,
                source_order_no = "10", valid = 1, total_amount = 3400 });
            db.retail.AddRange(new Retail { id = 21, order_id = 10, valid = 1 },
                new Retail { id = 22, order_id = 10, valid = 1 });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            db.punchCard.AddRange(new PunchCard { id = 31, source_retail_id = 21, member_id = 1,
                    biz_type = "养护", card_name = "次卡", total = 10, punches = 0 },
                new PunchCard { id = 32, source_retail_id = 22, member_id = 1,
                    biz_type = "养护", card_name = "季卡", total = null, punches = 0 });
            db.orderPayment.AddRange(new OrderPayment { id = 41, order_id = 10, amount = 2000, valid = 1,
                    status = "支付成功", pay_method = "微信支付", out_trade_no = "test-wx" },
                new OrderPayment { id = 42, order_id = 10, amount = 1400, valid = 1,
                    status = "支付成功", pay_method = "支付宝", out_trade_no = "test-ali" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await db.retail.Where(r => r.id == 21).ExecuteUpdateAsync(s => s.SetProperty(r => r.punch_card_id, 31));
            await db.retail.Where(r => r.id == 22).ExecuteUpdateAsync(s => s.SetProperty(r => r.punch_card_id, 32));
            return new Fixture(connection, db);
        }

        public static List<OrderPaymentRefund> Requests() => new() {
            new OrderPaymentRefund { payment_id = 41, amount = 2000 },
            new OrderPaymentRefund { payment_id = 42, amount = 1400 } };
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    [Fact]
    public async Task 有有效核销或未归还次数时禁止退款_作废核销且归零后允许()
    {
        await using var f = await Fixture.Create();
        f.Db.punchCardUsed.Add(new PunchCardUsed { id = 1, card_id = 32, order_id = 99, biz_type = "养护", valid = true });
        await f.Db.SaveChangesAsync();
        Assert.False((await CardOrderRefundEligibility.EvaluateAsync(f.Db, 10)).CanRefund);
        await f.Db.punchCardUsed.Where(u => u.id == 1).ExecuteUpdateAsync(s => s.SetProperty(u => u.valid, false));
        await f.Db.punchCard.Where(c => c.id == 31).ExecuteUpdateAsync(s => s.SetProperty(c => c.punches, 1));
        Assert.False((await CardOrderRefundEligibility.EvaluateAsync(f.Db, 10)).CanRefund);
        await f.Db.punchCard.Where(c => c.id == 31).ExecuteUpdateAsync(s => s.SetProperty(c => c.punches, 0));
        Assert.True((await CardOrderRefundEligibility.EvaluateAsync(f.Db, 10)).CanRefund);
    }

    [Fact]
    public async Task 必须按全部支付金额退款_部分金额不冻结卡()
    {
        await using var f = await Fixture.Create();
        var requests = Fixture.Requests(); requests[1].amount = 1000;
        var result = await f.Service.PrepareAsync(10, 7, "整单退款", requests);
        Assert.Null(result.BatchId);
        Assert.All(await f.Db.punchCard.ToListAsync(), c => Assert.False(c.refund_pending));
        Assert.Empty(await f.Db.orderPaymentRefund.ToListAsync());
    }

    [Fact]
    public async Task 多笔退款_部分成功保持冻结_全部成功才停卡_重复核对幂等()
    {
        await using var f = await Fixture.Create();
        var prepared = await f.Service.PrepareAsync(10, 7, "整单退款", Fixture.Requests());
        int batch = Assert.IsType<int>(prepared.BatchId);
        Assert.All(await f.Db.punchCard.ToListAsync(), c => { Assert.True(c.refund_pending); Assert.False(c.is_refund); });
        var repeated = await f.Service.PrepareAsync(10, 7, "整单退款", Fixture.Requests());
        Assert.Equal(batch, repeated.BatchId);
        Assert.Equal(2, await f.Db.orderPaymentRefund.CountAsync());
        await f.Db.orderPaymentRefund.Where(r => r.card_refund_batch_id == batch && r.payment_id == 41)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.state, 1).SetProperty(r => r.card_refund_state, "success"));
        await f.Service.ReconcileAsync(batch);
        Assert.All(await f.Db.punchCard.ToListAsync(), c => { Assert.True(c.refund_pending); Assert.False(c.is_refund); });
        await f.Db.orderPaymentRefund.Where(r => r.card_refund_batch_id == batch && r.payment_id == 42)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.state, 1).SetProperty(r => r.card_refund_state, "success"));
        await f.Service.ReconcileAsync(batch);
        await f.Service.ReconcileAsync(batch);
        Assert.All(await f.Db.punchCard.AsNoTracking().ToListAsync(), c => { Assert.False(c.refund_pending); Assert.True(c.is_refund); });
        Assert.Equal("succeeded", (await f.Db.cardOrderRefundBatch.AsNoTracking().FirstAsync()).status);
    }

    [Fact]
    public async Task 全部分笔确认失败才恢复_结果未知保持冻结()
    {
        await using var f = await Fixture.Create();
        int batch = Assert.IsType<int>((await f.Service.PrepareAsync(10, 7, "整单退款", Fixture.Requests())).BatchId);
        await f.Db.orderPaymentRefund.Where(r => r.card_refund_batch_id == batch && r.payment_id == 41)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.card_refund_state, "failed"));
        await f.Db.orderPaymentRefund.Where(r => r.card_refund_batch_id == batch && r.payment_id == 42)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.card_refund_state, "unknown"));
        await f.Service.ReconcileAsync(batch);
        Assert.All(await f.Db.punchCard.ToListAsync(), c => Assert.True(c.refund_pending));
        await f.Db.orderPaymentRefund.Where(r => r.card_refund_batch_id == batch)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.card_refund_state, "failed"));
        await f.Service.ReconcileAsync(batch);
        Assert.All(await f.Db.punchCard.AsNoTracking().ToListAsync(), c => { Assert.False(c.refund_pending); Assert.False(c.is_refund); });
        Assert.Equal("failed", (await f.Db.cardOrderRefundBatch.AsNoTracking().FirstAsync()).status);
    }

    [Fact]
    public async Task 重复微信成功回调只记录一次_另一笔未成功时仍冻结()
    {
        await using var f = await Fixture.Create();
        int batch = Assert.IsType<int>((await f.Service.PrepareAsync(10, 7, "整单退款", Fixture.Requests())).BatchId);
        var refund = await f.Db.orderPaymentRefund.AsNoTracking().FirstAsync(r => r.payment_id == 41);
        var payment = await f.Db.orderPayment.AsNoTracking().FirstAsync(p => p.id == 41);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Settings:AppId"] = "local-test-app" }).Build();
        var tenpay = new TenpayController(f.Db, config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        await tenpay.ApplyRefundSuccess(refund, payment, "first-channel-id", false);
        await tenpay.ApplyRefundSuccess(refund, payment, "second-channel-id", false);
        var saved = await f.Db.orderPaymentRefund.AsNoTracking().FirstAsync(r => r.id == refund.id);
        Assert.Equal(1, saved.state);
        Assert.Equal("first-channel-id", saved.TransactionId);
        Assert.All(await f.Db.punchCard.AsNoTracking().ToListAsync(), c => { Assert.True(c.refund_pending); Assert.False(c.is_refund); });
        Assert.Equal("processing", (await f.Db.cardOrderRefundBatch.AsNoTracking().FirstAsync(b => b.id == batch)).status);
    }

    [Fact]
    public async Task 退款冻结后不能再通过养护核销校验()
    {
        await using var f = await Fixture.Create();
        Assert.NotNull((await f.Service.PrepareAsync(10, 7, "整单退款", Fixture.Requests())).BatchId);
        var careController = new CareController(f.Db, new ConfigurationBuilder().Build(), new HttpContextAccessor());
        var care = new Care { id = 50, card_id = 31, use_card = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => careController.ValidateCardBalances(new[] { care }, 1, true));
        Assert.Empty(await f.Db.punchCardUsed.AsNoTracking().ToListAsync());
    }
}

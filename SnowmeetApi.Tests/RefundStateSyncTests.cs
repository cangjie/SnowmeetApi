using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 微信退款成功回调从 2026-07 中旬起没有更新过 payment_refund.state，退款状态改为也能主动向微信查询落库。
    /// 这里验证落库逻辑和同步入口的筛选；真正向微信查询要网络和商户密钥，不在单测里跑。
    /// </summary>
    public class RefundStateSyncTests
    {
        static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();

        sealed class TestHttp : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }

        static async Task<SqliteConnection> CreateDb()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            await using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return connection;
        }

        static ApplicationDBContext Open(SqliteConnection c) => new(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlite(c).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

        [Fact]
        public async Task 退款落库_状态改成功_记交易号()
        {
            await using var c = await CreateDb();
            await using (var seed = Open(c))
            {
                seed.OrderPayment.Add(new OrderPayment { id = 1, order_id = 500, pay_method = "微信支付", status = "支付成功", amount = 309, mch_id = 11 });
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 1, order_id = 500, payment_id = 1, amount = 309, refund_id = "5030", out_refund_no = "A_TK_01", state = 0 });
                await seed.SaveChangesAsync();
            }
            await using var db = Open(c);
            var tenpay = new TenpayController(db, Config, new TestHttp());
            var refund = await db.orderPaymentRefund.FirstAsync(r => r.id == 1);
            var payment = await db.OrderPayment.FirstAsync(p => p.id == 1);
            await tenpay.ApplyRefundSuccess(refund, payment, " 4500000562 ", false);

            await using var check = Open(c);
            var saved = await check.orderPaymentRefund.FirstAsync(r => r.id == 1);
            Assert.Equal(1, saved.state);
            Assert.Equal("4500000562", saved.TransactionId);
            Assert.NotNull(saved.update_date);
        }

        [Fact]
        public async Task 同步入口_只处理处理中且已有微信退款号的近期退款_查询失败不抛异常不改状态()
        {
            await using var c = await CreateDb();
            await using (var seed = Open(c))
            {
                seed.OrderPayment.Add(new OrderPayment { id = 1, order_id = 500, pay_method = "微信支付", status = "支付成功", amount = 309, mch_id = 11 });
                // 处理中、有微信退款号：要查
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 1, order_id = 500, payment_id = 1, amount = 100, refund_id = "5030", out_refund_no = "A_TK_01", state = 0, create_date = DateTime.Now.AddHours(-1) });
                // 已成功：不查
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 2, order_id = 500, payment_id = 1, amount = 100, refund_id = "5031", out_refund_no = "A_TK_02", state = 1, create_date = DateTime.Now.AddHours(-1) });
                // 没有微信退款号（没发出去）：不查
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 3, order_id = 500, payment_id = 1, amount = 100, refund_id = "", out_refund_no = "A_TK_03", state = 0, create_date = DateTime.Now.AddHours(-1) });
                // 太久以前：超出 days 窗口不查
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 4, order_id = 500, payment_id = 1, amount = 100, refund_id = "5033", out_refund_no = "A_TK_04", state = 0, create_date = DateTime.Now.AddDays(-30) });
                await seed.SaveChangesAsync();
            }
            await using var db = Open(c);
            var tenpay = new TenpayController(db, Config, new TestHttp());
            var result = await tenpay.SyncPendingRefunds(3, 1);
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ok.Value);
            Assert.Contains("\"total\":1", json);
            Assert.Contains("\"succeeded\":0", json);

            await using var check = Open(c);
            Assert.Equal(0, (await check.orderPaymentRefund.FirstAsync(r => r.id == 1)).state);
            Assert.Equal(0, (await check.orderPaymentRefund.FirstAsync(r => r.id == 4)).state);
        }
    }
}

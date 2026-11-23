using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 刚付款的雪票取二维码提速（2026-11-23 用户确认「B + C」）：
    ///   付款后供应商大约 15～25 秒出码，原来后台每分钟才查一次，二维码平均要等 52 秒；
    ///   现在对「最近 10 分钟内付款成功、还没取到码」的票每 10 秒查一次（RefreshAutoReserve?minutes=10），其余仍是每分钟。
    /// 因为查得更勤，要保证：只查刚付款的；供应商还没出码时不写票、不写日志；一时查不到不能把票标成出票失败；
    /// 快的和慢的两路不会同时处理同一张票（否则取到码时会重复通知顾客）。
    /// </summary>
    public class SkipassFreshCodePollTests
    {
        const string CodeJson = "{\"orderId\":1001,\"orderState\":2,\"vouchers\":[{\"code\":\"123456\",\"qrcodeUrl\":\"https://img.example/qr.png\",\"type\":1,\"voucherStatus\":0}]}";
        const string NoCodeJson = "{\"orderId\":1001,\"orderState\":2,\"vouchers\":[]}";

        [Fact]
        public async Task 快轮询只处理最近付款的票_早先付款的留给每分钟那一轮()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 2);
            await f.AddTicket(2, "1002", paidMinutesAgo: 30);
            var asked = new List<string>();
            var c = f.Controller(json => { asked.Add(json); return CodeJson; });

            await c.RefreshAutoReserve(minutes: 10);

            Assert.Equal(new[] { "1001" }, asked);
            Assert.Equal("123456", (await f.Ticket(1)).card_no);
            Assert.Null((await f.Ticket(2)).card_no);
        }

        [Fact]
        public async Task 每分钟那一轮仍处理48小时内的票_不看付款时间()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 180, createdMinutesAgo: 185);
            var c = f.Controller(_ => CodeJson);

            await c.RefreshAutoReserve(hours: 48);

            Assert.Equal("https://img.example/qr.png", (await f.Ticket(1)).qr_code_url);
        }

        [Fact]
        public async Task 取到码_写票和日志_只通知一次_再查不会重复()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            int asked = 0, notified = 0;
            var c = f.Controller(_ => { asked++; return CodeJson; }, _ => { notified++; });

            await c.RefreshAutoReserve(minutes: 10);
            await c.RefreshAutoReserve(minutes: 10);

            var t = await f.Ticket(1);
            Assert.Equal("123456", t.card_no);
            Assert.Equal("https://img.example/qr.png", t.qr_code_url);
            Assert.NotNull(t.card_member_pick_time);
            Assert.NotNull(t.update_date);
            Assert.Equal(1, asked);                // 取到码之后这张票就不再向供应商查了
            Assert.Equal(1, notified);
            Assert.Equal(1, await f.LogCount("万龙雪票信息更新成功"));
        }

        [Fact]
        public async Task 供应商还没出码_第一次查到时票变成已出票_之后不再写票也不写日志_下一轮继续查()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            int asked = 0, notified = 0;
            var c = f.Controller(_ => { asked++; return NoCodeJson; }, _ => { notified++; });

            await c.RefreshAutoReserve(minutes: 10);
            var afterFirst = await f.Ticket(1);
            await c.RefreshAutoReserve(minutes: 10);
            await c.RefreshAutoReserve(minutes: 10);

            var t = await f.Ticket(1);
            Assert.Equal(3, asked);                // 一直没出码就一直查
            Assert.Null(t.card_no);
            Assert.Null(t.qr_code_url);
            // 后台管理页的「申请取消」只在状态是「已出票」时可点；原来每轮查询都写 card_member_pick_time，
            // 下单成功后一分钟内就是「已出票」，哪怕供应商一直没出码（要能取消的恰恰是这种票）。第一次查到时写一次，之后不再动
            Assert.Equal("已出票", t.status);
            Assert.NotNull(t.card_member_pick_time);
            Assert.Equal(afterFirst.card_member_pick_time, t.card_member_pick_time);
            Assert.Equal(afterFirst.update_date, t.update_date);
            Assert.Equal(0, notified);
            Assert.Equal(0, await f.LogCount("万龙雪票信息更新成功"));   // 没取到码，不记「更新成功」
        }

        [Fact]
        public async Task 下单成功但还没查过的票是已确认_取到码后是已出票_pick_time取到码那一轮的时间()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            Assert.Equal("已确认", (await f.Ticket(1)).status);
            string body = NoCodeJson;
            var c = f.Controller(_ => body);

            await c.RefreshAutoReserve(minutes: 10);
            var withoutCode = await f.Ticket(1);
            await Task.Delay(30);                  // 保证两轮的时间戳能分开
            body = CodeJson;
            await c.RefreshAutoReserve(minutes: 10);
            var withCode = await f.Ticket(1);

            Assert.Equal("已出票", withoutCode.status);
            Assert.Equal("已出票", withCode.status);
            // 优惠券激活日按 card_member_pick_time 算：取到码那一轮要覆盖掉前一轮写的时间（和原来每轮覆盖、最终是取到码那一轮一致）
            Assert.True(withCode.card_member_pick_time > withoutCode.card_member_pick_time);
            Assert.Equal(1, await f.LogCount("万龙雪票信息更新成功"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("null")]
        public async Task 快轮询一时没查到订单_不会把票标成出票失败(string body)
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            var c = f.Controller(_ => body);

            await c.RefreshAutoReserve(minutes: 10);

            var t = await f.Ticket(1);
            Assert.Equal(0, t.is_cancel);
            Assert.Equal("", t.memo);
        }

        [Fact]
        public async Task 供应商说订单已取消_照旧标记()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            var c = f.Controller(_ => "{\"orderId\":1001,\"orderState\":3,\"vouchers\":[]}");

            await c.RefreshAutoReserve(minutes: 10);

            Assert.Equal(-3, (await f.Ticket(1)).is_cancel);
        }

        [Fact]
        public async Task 慢的一轮正在处理时_快轮询直接让位_不重复查供应商()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            var started = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            int asked = 0;
            var slow = f.Controller(null, null, async _ => { asked++; started.TrySetResult(); await release.Task; return CodeJson; });
            var fast = f.Controller(_ => { asked++; return CodeJson; });

            Task slowRun = slow.RefreshAutoReserve(hours: 48);
            await started.Task;                    // 慢的已经拿到锁、正在向供应商查
            await fast.RefreshAutoReserve(minutes: 10);
            Assert.Equal(1, asked);                // 快的没有再查同一张票

            release.SetResult();
            await slowRun;
            Assert.Equal("123456", (await f.Ticket(1)).card_no);
        }

        [Fact]
        public async Task 快的一轮出错_锁也会释放_下一轮能继续()
        {
            await using var f = await Fixture.Create();
            await f.AddTicket(1, "1001", paidMinutesAgo: 1);
            int calls = 0;
            var c = f.Controller(_ => { calls++; if (calls == 1) throw new InvalidOperationException("供应商接口挂了"); return CodeJson; });

            await c.RefreshAutoReserve(minutes: 10);   // 单张票查询出错只跳过这张，不抛
            await c.RefreshAutoReserve(minutes: 10);

            Assert.Equal(2, calls);
            Assert.Equal("123456", (await f.Ticket(1)).card_no);
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly SqliteConnection connection;
            public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
            private Fixture(SqliteConnection connection) { this.connection = connection; }

            public ApplicationDBContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

            public SkiPassController Controller(Func<string, string>? supplier = null, Action<Models.SkiPass>? notify = null,
                Func<string, Task<string?>>? supplierAsync = null)
            {
                var c = new SkiPassController(NewContext(), Config, new TestHttpContextAccessor());
                c.SupplierOrderJsonProvider = supplierAsync != null
                    ? supplierAsync
                    : (id => Task.FromResult<string?>(supplier!(id)));
                c.CodeReadyNotifier = s => { notify?.Invoke(s); return Task.CompletedTask; };
                return c;
            }

            public async Task AddTicket(int id, string reserveNo, int paidMinutesAgo, int? createdMinutesAgo = null)
            {
                await using var db = NewContext();
                var order = new SnowmeetApi.Models.Order { id = id, type = "雪票", shop = "万龙", member_id = 11, valid = 1, is_test = 0, code = "XP_" + id };
                db.order.Add(order);
                db.orderPayment.Add(new OrderPayment { order_id = id, amount = 100, pay_method = "微信支付", status = "支付成功", valid = 1,
                    is_debt = 0, paid_date = DateTime.Now.AddMinutes(-paidMinutesAgo) });
                db.skiPass.Add(new Models.SkiPass { id = id, member_id = 11, product_id = 901, resort = "万龙", product_name = "测试票", count = 1,
                    order_id = id, deal_price = 100, valid = 1, reserve_no = reserveNo, reserve_date = DateTime.Today,
                    create_date = DateTime.Now.AddMinutes(-(createdMinutesAgo ?? paidMinutesAgo + 1)) });
                await db.SaveChangesAsync();
            }

            public async Task<Models.SkiPass> Ticket(int id)
            {
                await using var db = NewContext();
                return await db.skiPass.SingleAsync(s => s.id == id);
            }

            public async Task<int> LogCount(string scene)
            {
                await using var db = NewContext();
                return await db.coreDataModLog.CountAsync(l => l.table_name == "ski_pass" && l.scene == scene);
            }

            public static async Task<Fixture> Create()
            {
                var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
                await connection.OpenAsync();
                var f = new Fixture(connection);
                await using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
                await db.Database.EnsureCreatedAsync();
                db.member.Add(new Member { id = 11, real_name = "顾客", gender = "男" });
                db.product.Add(new Product { id = 901, name = "测试票", type = "雪票", sale_price = 100, hidden = 0 });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 901, resort = "万龙", source = "万龙自我游",
                    third_party_no = "37696034", rules = "", tags = "", available_days = "", unavailable_days = "" });
                await db.SaveChangesAsync();
                return f;
            }

            public async ValueTask DisposeAsync() => await connection.DisposeAsync();
        }

        // 框架自带的 HttpContextAccessor 靠 AsyncLocal 保存，跨 await 回不来；控制器构造函数要读 Request.Host
        private sealed class TestHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
        }
    }
}

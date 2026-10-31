using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 雪票购物车：一个订单、一笔付款、每张票一条 ski_pass（张数都是 1，供应商一单只能 1 张）。
    /// 重点是钱：订单总额 = 各张当天价格之和；退一张只退这一张的成交价，退款记到这张票上；
    /// 取消一张不能让另几张票的赠券作废。
    /// </summary>
    public class SkipassCartTests
    {
        static readonly DateTime D0 = DateTime.Today.AddDays(2);
        static readonly DateTime D1 = DateTime.Today.AddDays(3);

        // ---------------- 纯规则 ----------------

        [Fact]
        public void 购物车校验_空_坏手机号_缺姓名_超上限_都不通过()
        {
            Assert.NotNull(SkipassCartRules.Validate(null));
            Assert.NotNull(SkipassCartRules.Validate(new List<SkipassCartItem>()));
            Assert.Equal("请填写正确的手机号", SkipassCartRules.Validate(new[] { Item(901, "12345") }));
            Assert.Equal("请填写姓名", SkipassCartRules.Validate(new[] { Item(901, "13501177897", name: " ") }));
            Assert.Equal("张数不正确", SkipassCartRules.Validate(new[] { Item(901, "13501177897", count: 0) }));
            Assert.Equal("票种不正确", SkipassCartRules.Validate(new[] { Item(0, "13501177897") }));
            Assert.Null(SkipassCartRules.Validate(new[] { Item(901, "13501177897", count: 10) }));
            Assert.Contains("最多订 10 张", SkipassCartRules.Validate(new[] { Item(901, "13501177897", count: 6), Item(902, "13501177897", count: 5) }));
        }

        [Fact]
        public void 购物车展开_每条按张数拆成1张_顺序不变()
        {
            var expanded = SkipassCartRules.Expand(new[] { Item(901, "13501177897", count: 2), Item(902, "13900139000") });
            Assert.Equal(new[] { 901, 901, 902 }, expanded.Select(x => x.productId).ToArray());
            Assert.All(expanded, x => Assert.Equal(1, x.count));
        }

        [Theory]
        [InlineData(3, 309, 927, 309)]   // 一单三张：只退这一张的成交价
        [InlineData(2, 389.5, 779, 389.5)]
        [InlineData(1, 309, 309, 309)]   // 一单一张：沿用整笔支付额
        [InlineData(1, 100, 309, 309)]   // 老订单成交价和支付额不一致时保持原来的整笔退
        public void 一张票退多少(int passesInOrder, double dealPrice, double paymentAmount, double expect)
        {
            Assert.Equal(expect, SkipassSaleRules.RefundAmountFor(passesInOrder, dealPrice, paymentAmount));
        }

        [Theory]
        [InlineData(-2, 1, "万龙", null, null, null, true)]
        [InlineData(-2, 1, "万龙", "", null, null, true)]
        [InlineData(-2, 1, "万龙", "123", null, null, false)]  // 供应商那边已经有订单，要人工处理
        [InlineData(1, 1, "万龙", null, null, null, false)]
        [InlineData(-2, 0, "万龙", null, null, null, false)]
        [InlineData(-2, 1, "南山", null, null, null, false)]
        [InlineData(-2, 1, "万龙", null, 1, null, false)]
        [InlineData(-2, 1, "万龙", null, null, 309.0, false)]
        public void 预订失败的票是否自动退款(int isCancel, int valid, string resort, string? reserveNo, int? haveRefund, double? refundAmount, bool expect)
        {
            Assert.Equal(expect, SkipassSaleRules.NeedsAutoRefundForFailedBooking(isCancel, valid, resort, reserveNo, haveRefund, refundAmount));
        }

        // ---------------- 下单 ----------------

        [Fact]
        public async Task 购物车下单_一个订单一笔付款_每张票一条记录_总额是各张价格之和()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var request = new SkipassCartRequest
            {
                staffId = 28,
                items = new List<SkipassCartItem>
                {
                    Item(901, "13501177897", date: D0, count: 2),
                    Item(902, "13900139000", date: D1, count: 1)
                }
            };
            var result = await f.Controller(db).ReserveSkiPassCart(request, "staff-session");
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var order = Assert.IsType<Order>(ok.Value);

            await using var check = f.NewContext();
            var orders = await check.order.Where(o => o.type == "雪票").ToListAsync();
            Assert.Single(orders);
            Assert.Equal(579 * 2 + 399, orders[0].total_amount);
            Assert.Equal(579 * 2 + 399, orders[0].paying_amount);
            var payments = await check.orderPayment.Where(p => p.order_id == order.id).ToListAsync();
            Assert.Single(payments);
            Assert.Equal(579 * 2 + 399, payments[0].amount);
            var passes = await check.skiPass.Where(s => s.order_id == order.id).OrderBy(s => s.id).ToListAsync();
            Assert.Equal(3, passes.Count);
            Assert.All(passes, p => Assert.Equal(1, p.count));
            Assert.Equal(new double?[] { 579, 579, 399 }, passes.Select(p => p.deal_price).ToArray());
            Assert.Equal(new[] { "13501177897", "13501177897", "13900139000" }, passes.Select(p => p.contact_cell).ToArray());
            Assert.All(passes, p => Assert.Equal(0, p.valid)); // 付款成功后才生效
        }

        [Fact]
        public async Task 旧接口买2张_按张数展开成2条记录_订单金额是2倍单价_不再乘两次()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).ReserveSkiPass(901, D0, 2, "13501177897", "张三", "staff-session");
            Assert.IsType<OkObjectResult>(result.Result);
            await using var check = f.NewContext();
            var order = await check.order.SingleAsync(o => o.type == "雪票");
            Assert.Equal(579 * 2, order.total_amount);
            Assert.Equal(2, await check.skiPass.CountAsync(s => s.order_id == order.id));
        }

        [Theory]
        [InlineData("empty")]
        [InlineData("badCell")]
        [InlineData("tooMany")]
        [InlineData("noPrice")]
        [InlineData("noProduct")]
        public async Task 购物车下单_非法请求被拒绝_不建订单(string kind)
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var items = kind switch
            {
                "empty" => new List<SkipassCartItem>(),
                "badCell" => new List<SkipassCartItem> { Item(901, "123", date: D0) },
                "tooMany" => new List<SkipassCartItem> { Item(901, "13501177897", date: D0, count: 11) },
                "noPrice" => new List<SkipassCartItem> { Item(901, "13501177897", date: DateTime.Today.AddDays(40)) },
                _ => new List<SkipassCartItem> { Item(99999, "13501177897", date: D0) }
            };
            var result = await f.Controller(db).ReserveSkiPassCart(new SkipassCartRequest { items = items }, "staff-session");
            Assert.IsType<BadRequestObjectResult>(result.Result);
            await using var check = f.NewContext();
            Assert.Empty(await check.order.ToListAsync());
            Assert.Empty(await check.skiPass.ToListAsync());
        }

        [Fact]
        public async Task 购物车下单_顾客会话被拒绝()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).ReserveSkiPassCart(
                new SkipassCartRequest { items = new List<SkipassCartItem> { Item(901, "13501177897", date: D0) } }, "customer-session");
            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal(SkipassSaleRules.STAFF_ONLY_MESSAGE, bad.Value);
            await using var check = f.NewContext();
            Assert.Empty(await check.order.ToListAsync());
        }

        [Fact]
        public async Task 购物车下单_某个自我游账号预存款不够_整单不让订()
        {
            await using var f = await Fixture.Create();
            f.Balances["万龙自我游"] = 1000;   // 901 要 579 ×2 = 1158，不够
            f.Balances["大好河山"] = 100000;
            await using var db = f.NewContext();
            var request = new SkipassCartRequest
            {
                items = new List<SkipassCartItem> { Item(901, "13501177897", date: D0, count: 2), Item(902, "13501177897", date: D0) }
            };
            var result = await f.Controller(db).ReserveSkiPassCart(request, "staff-session");
            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Contains("万龙自我游预存款不足", (string)bad.Value!);
            await using var check = f.NewContext();
            Assert.Empty(await check.order.ToListAsync());
        }

        // ---------------- 退款：按票退、写到对应的票 ----------------

        [Fact]
        public async Task 购物车订单取消一张_只退这一张的成交价_退款行记下票号()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下"); // 非微信/支付宝：退款只建记录，不发真实请求
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            var pass = await db.skiPass.AsNoTracking().FirstAsync(s => s.id == 502);
            pass.is_cancel = 1;
            await ctl.AutoRefundCancelled(pass, "退票确认退款", null);

            await using var check = f.NewContext();
            var refunds = await check.orderPaymentRefund.Where(r => r.order_id == 900).ToListAsync();
            Assert.Single(refunds);
            Assert.Equal(399, refunds[0].amount);          // 这一张的成交价，不是整笔 1557
            Assert.Equal(502, refunds[0].skipass_id);
            Assert.Equal("系统自动退款", refunds[0].oper);
        }

        [Fact]
        public async Task 同一张票已经有退款行_不重复退_另一张票照常退()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = 900, payment_id = 800, amount = 399, skipass_id = 502,
                    refund_id = "R1", state = 1, out_refund_no = "x_TK_01" });
                await seed.SaveChangesAsync();
            }
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            var same = await db.skiPass.AsNoTracking().FirstAsync(s => s.id == 502);
            same.is_cancel = 1;
            await ctl.AutoRefundCancelled(same, "退票确认退款", null);
            var other = await db.skiPass.AsNoTracking().FirstAsync(s => s.id == 501);
            other.is_cancel = 1;
            await ctl.AutoRefundCancelled(other, "退票确认退款", null);

            await using var check = f.NewContext();
            var refunds = await check.orderPaymentRefund.Where(r => r.order_id == 900).OrderBy(r => r.id).ToListAsync();
            Assert.Equal(2, refunds.Count);
            Assert.Equal(new int?[] { 502, 501 }, refunds.Select(r => r.skipass_id).ToArray());
            Assert.Equal(579, refunds[1].amount);
        }

        [Fact]
        public async Task 累计退款不能超过支付额()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                // 之前已经退过 1200（比如手工退的），这张票 579 再退会超过 1557
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = 900, payment_id = 800, amount = 1200, refund_id = "R0",
                    state = 1, out_refund_no = "x_TK_01" });
                await seed.SaveChangesAsync();
            }
            await using var db = f.NewContext();
            var pass = await db.skiPass.AsNoTracking().FirstAsync(s => s.id == 501);
            pass.is_cancel = 1;
            await f.Controller(db).AutoRefundCancelled(pass, "退票确认退款", null);
            await using var check = f.NewContext();
            Assert.Equal(1, await check.orderPaymentRefund.CountAsync(r => r.order_id == 900));
        }

        [Fact]
        public async Task 预订失败的票_自动退这一张的款_已有供应商订单号的不退()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            var failed = await db.skiPass.AsNoTracking().FirstAsync(s => s.id == 503);
            failed.is_cancel = -2;
            await ctl.AutoRefundFailedBooking(failed, "出票失败退款");
            var withOrderNo = await db.skiPass.AsNoTracking().FirstAsync(s => s.id == 501);
            withOrderNo.is_cancel = -2;
            withOrderNo.reserve_no = "999";
            await ctl.AutoRefundFailedBooking(withOrderNo, "出票失败退款");

            await using var check = f.NewContext();
            var refunds = await check.orderPaymentRefund.Where(r => r.order_id == 900).ToListAsync();
            Assert.Single(refunds);
            Assert.Equal(579, refunds[0].amount);
            Assert.Equal(503, refunds[0].skipass_id);
        }

        [Fact]
        public async Task 退款成功后续_记到被退的那张票_另几张票的赠券不被作废()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 7001, order_id = 900, payment_id = 800, amount = 399, skipass_id = 502,
                    refund_id = "R1", state = 1, out_refund_no = "x_TK_01" });
                seed.ticket.Add(new Ticket { order_id = 900, used = 0, code = "T1", create_memo = "x" });
                var cancelled = await seed.skiPass.FirstAsync(s => s.id == 502);
                cancelled.is_cancel = 1;
                seed.skiPass.Update(cancelled); // 夹具的上下文不跟踪实体，要显式 Update 才会写库
                await seed.SaveChangesAsync();
            }
            await using (var db = f.NewContext())
            {
                await f.Controller(db).RefundCallBack(800, 7001);
            }
            await using var check = f.NewContext();
            Assert.Equal(399, (await check.skiPass.FirstAsync(s => s.id == 502)).refund_amount);
            Assert.Null((await check.skiPass.FirstAsync(s => s.id == 501)).refund_amount);
            Assert.Null((await check.skiPass.FirstAsync(s => s.id == 503)).refund_amount);
            // 另两张票还在持有：订单的赠券不取消
            Assert.NotEqual("订单取消", (await check.ticket.FirstAsync(t => t.code == "T1")).use_memo);
        }

        [Fact]
        public async Task 退款成功后续_这单最后一张票也处理完时才取消订单的赠券()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { id = 7001, order_id = 900, payment_id = 800, amount = 399, skipass_id = 502,
                    refund_id = "R1", state = 1, out_refund_no = "x_TK_01" });
                seed.ticket.Add(new Ticket { order_id = 900, used = 0, code = "T1", create_memo = "x" });
                foreach (int id in new[] { 501, 502, 503 })
                {
                    var p = await seed.skiPass.FirstAsync(s => s.id == id);
                    p.is_cancel = 1;
                    seed.skiPass.Update(p);
                }
                await seed.SaveChangesAsync();
            }
            await using (var db = f.NewContext())
            {
                await f.Controller(db).RefundCallBack(800, 7001);
            }
            await using var check = f.NewContext();
            Assert.Equal("订单取消", (await check.ticket.FirstAsync(t => t.code == "T1")).use_memo);
        }

        // ---------------- 夹具 ----------------

        static SkipassCartItem Item(int productId, string cell, string name = "张三", DateTime? date = null, int count = 1)
            => new() { productId = productId, cell = cell, name = name, date = date ?? D0, count = count };

        // 一个已付款的购物车订单：501 (579)、502 (399)、503 (579) 三张票，支付 1557
        static async Task SeedPaidCartOrder(Fixture f, string payMethod)
        {
            await using var db = f.NewContext();
            db.order.Add(new Order { id = 900, type = "雪票", member_id = 10, total_amount = 1557, paying_amount = 1557, valid = 1, create_date = DateTime.Now });
            db.OrderPayment.Add(new OrderPayment { id = 800, order_id = 900, pay_method = payMethod, status = "支付成功", amount = 1557, mch_id = 11, valid = 1 });
            db.skiPass.AddRange(
                NewPass(501, 579), NewPass(502, 399), NewPass(503, 579));
            await db.SaveChangesAsync();
        }

        static SnowmeetApi.Models.SkiPass NewPass(int id, double price) => new()
        {
            id = id, order_id = 900, member_id = 10, product_id = 901, resort = "万龙", product_name = "p", count = 1,
            deal_price = price, valid = 1, contact_cell = "13501177897", contact_name = "张三", reserve_date = D0, is_cancel = 0
        };

        sealed class TestHttp : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }

        sealed class Fixture : IAsyncDisposable
        {
            readonly SqliteConnection connection;
            public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
            public IHttpContextAccessor Http { get; } = new TestHttp();
            public Dictionary<string, double> Balances { get; } = new() { ["万龙自我游"] = 100000, ["大好河山"] = 100000 };

            Fixture(SqliteConnection c) { connection = c; }

            public ApplicationDBContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

            public SkiPassController Controller(ApplicationDBContext db) =>
                new(db, Config, Http) { BalanceProvider = source => Task.FromResult(Balances.TryGetValue(source, out var b) ? b : 0) };

            public static async Task<Fixture> Create()
            {
                var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
                await connection.OpenAsync();
                var f = new Fixture(connection);
                await using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
                await db.Database.EnsureCreatedAsync();
                db.member.Add(new Member { id = 10, real_name = "店员", gender = "男" });
                db.member.Add(new Member { id = 11, real_name = "顾客", gender = "男" });
                db.Set<Staff>().Add(new Staff { id = 28, title_level = 300, valid = 1 });
                db.Set<SocialAccountForJob>().Add(new SocialAccountForJob { id = 1, member_id = 10, wechat_mini_openid = "staff-openid" });
                db.Set<StaffSocialAccount>().Add(new StaffSocialAccount { staff_id = 28, social_account_id = 1, valid = 1 });
                db.miniSession.Add(new MiniSession { session_key = "staff-session", session_type = "wechat_mini_openid", member_id = 10,
                    wechat_openid = "staff-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
                db.miniSession.Add(new MiniSession { session_key = "customer-session", session_type = "wechat_mini_openid", member_id = 11,
                    wechat_openid = "customer-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
                db.product.Add(new Product { id = 901, name = "【万龙】全雪区日场4小时雪票-11.6", type = "雪票", sale_price = 579, market_price = 579, cost = 299, hidden = 0 });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 901, resort = "万龙", source = "万龙自我游",
                    third_party_no = "1", rules = "", tags = "", available_days = "", unavailable_days = "" });
                db.product.Add(new Product { id = 902, name = "【万龙】全雪区日场1天雪票-11.6", type = "雪票", sale_price = 399, market_price = 399, cost = 371, hidden = 0 });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 902, resort = "万龙", source = "大好河山",
                    third_party_no = "2", rules = "", tags = "", available_days = "", unavailable_days = "" });
                foreach (DateTime d in new[] { D0, D1 })
                {
                    db.skipassDailyPrice.Add(new SkipassDailyPrice { product_id = 901, third_party_id = "1", reserve_date = d, settlementPrice = 299,
                        salePrice = 579, marketPrice = 579, deal_price = 579, day_type = "平日", valid = 1 });
                    db.skipassDailyPrice.Add(new SkipassDailyPrice { product_id = 902, third_party_id = "2", reserve_date = d, settlementPrice = 371,
                        salePrice = 399, marketPrice = 399, deal_price = 399, day_type = "平日", valid = 1 });
                }
                await db.SaveChangesAsync();
                return f;
            }

            public async ValueTask DisposeAsync() { await connection.DisposeAsync(); }
        }
    }
}

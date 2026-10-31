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
        [InlineData("QJ_XP_261031_00002_ZF_02", 1, 8354, "QJ_XP_261031_00002_ZF_02")]       // 一单一张：沿用付款单号
        [InlineData("QJ_XP_261031_00002_ZF_02", 4, 8354, "QJ_XP_261031_00002_ZF_02_P8354")] // 购物车：每张票各用各的单号
        [InlineData("QJ_XP_261031_00002_ZF_02", 4, 8355, "QJ_XP_261031_00002_ZF_02_P8355")]
        public void 向供应商下单的我方单号_多张票的订单每张票各用各的(string outTradeNo, int passes, int skipassId, string expect)
        {
            Assert.Equal(expect, SkipassSaleRules.SupplierSourceId(outTradeNo, passes, skipassId));
        }

        [Fact]
        public void 同一个订单的几张票我方单号互不相同()
        {
            var ids = new[] { 8354, 8355, 8356, 8357 }.Select(i => SkipassSaleRules.SupplierSourceId("PAY_01", 4, i)).ToList();
            Assert.Equal(4, ids.Distinct().Count());
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

        // ---------------- 出票中 / 出票失败 ----------------

        [Fact]
        public void 出票中怎么处理_90秒内等_30分钟内重试_之后确认失败()
        {
            var start = new DateTime(2026, 10, 31, 19, 0, 0);
            Assert.Equal(SkipassSaleRules.IssuingAction.Wait, SkipassSaleRules.DecideIssuing(start, start.AddSeconds(90)));
            Assert.Equal(SkipassSaleRules.IssuingAction.Retry, SkipassSaleRules.DecideIssuing(start, start.AddSeconds(91)));
            Assert.Equal(SkipassSaleRules.IssuingAction.Retry, SkipassSaleRules.DecideIssuing(start, start.AddMinutes(30)));
            Assert.Equal(SkipassSaleRules.IssuingAction.GiveUp, SkipassSaleRules.DecideIssuing(start, start.AddMinutes(30).AddSeconds(1)));
        }

        [Theory]
        [InlineData(null, 0, false)]     // 出票中（没有供应商订单号）：不能退
        [InlineData("", 0, false)]
        [InlineData("123", 0, true)]     // 已出票：可以走退票
        [InlineData(null, -2, true)]     // 确认出票失败：可以退款
        [InlineData(null, 1, false)]
        public void 出票中不允许退票退款(string? reserveNo, int isCancel, bool expect)
        {
            Assert.Equal(expect, SkipassSaleRules.CanRefundBeforeIssued(reserveNo, isCancel));
        }

        [Fact]
        public async Task 出票中的票_退票接口被拒绝_状态不变()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using var db = f.NewContext();
            var result = await f.Controller(db).Cancel(501, "staff-session");
            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Contains("出票中", (string)bad.Value!);
            await using var check = f.NewContext();
            var pass = await check.skiPass.FirstAsync(p => p.id == 501);
            Assert.Equal(0, pass.is_cancel);
            Assert.Empty(await check.orderPaymentRefund.ToListAsync());
        }

        [Fact]
        public async Task 出票中的票_手工退款接口被拒绝()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using var db = f.NewContext();
            var result = await f.Controller(db).Refund(501, "手工退款", "staff-session");
            Assert.IsType<BadRequestObjectResult>(result.Result);
            await using var check = f.NewContext();
            Assert.Empty(await check.orderPaymentRefund.ToListAsync());
        }

        [Fact]
        public async Task 后台出票检查_超过30分钟没出成_确认出票失败并自动退这一张的款_90秒内的不动()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                var old = await seed.skiPass.FirstAsync(p => p.id == 501);
                old.booking_now = true;
                old.update_date = DateTime.Now.AddMinutes(-31);
                seed.skiPass.Update(old);
                var fresh = await seed.skiPass.FirstAsync(p => p.id == 502);
                fresh.booking_now = true;
                fresh.update_date = DateTime.Now.AddSeconds(-20);
                seed.skiPass.Update(fresh);
                await seed.SaveChangesAsync();
            }
            await using (var db = f.NewContext())
            {
                await f.Controller(db).RefreshIssuing();
            }
            await using var check = f.NewContext();
            var timedOut = await check.skiPass.FirstAsync(p => p.id == 501);
            Assert.Equal(-2, timedOut.is_cancel);
            Assert.Contains("出票失败", timedOut.memo);
            Assert.Equal(0, (await check.skiPass.FirstAsync(p => p.id == 502)).is_cancel);
            var refunds = await check.orderPaymentRefund.Where(r => r.order_id == 900).ToListAsync();
            Assert.Single(refunds);
            Assert.Equal(579, refunds[0].amount);       // 这一张的成交价，不是整笔 1557
            Assert.Equal(501, refunds[0].skipass_id);
        }

        [Fact]
        public async Task 后台出票检查_已经是出票失败还没退款的补退_退过的不重复()
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                foreach (int id in new[] { 502, 503 })
                {
                    var p = await seed.skiPass.FirstAsync(x => x.id == id);
                    p.is_cancel = -2;
                    seed.skiPass.Update(p);
                }
                seed.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = 900, payment_id = 800, amount = 579, skipass_id = 503,
                    refund_id = "R1", state = 1, out_refund_no = "x_TK_01" });
                await seed.SaveChangesAsync();
            }
            await using (var db = f.NewContext())
            {
                await f.Controller(db).RefreshIssuing();
                await f.Controller(db).RefreshIssuing();
            }
            await using var check = f.NewContext();
            var refunds = await check.orderPaymentRefund.Where(r => r.order_id == 900).OrderBy(r => r.id).ToListAsync();
            Assert.Equal(new int?[] { 503, 502 }, refunds.Select(r => r.skipass_id).ToArray());
            Assert.Equal(399, refunds[1].amount);
        }

        // ---------------- 取票二维码转发 ----------------

        [Theory]
        [InlineData("https://dimg04.c-ctrip.com/images/0450l12000ub6ustm1A2C.png", true)]
        [InlineData("https://www.zowoyoo.com/qr/abc.png", true)]
        [InlineData("https://zowoyoo.com/a.png", true)]
        [InlineData("http://www.zowoyoo.com/qr/abc.png", false)]          // 只取 https
        [InlineData("https://evil.com/zowoyoo.com/a.png", false)]
        [InlineData("https://zowoyoo.com.evil.com/a.png", false)]          // 后缀骗不了
        [InlineData("https://evilzowoyoo.com/a.png", false)]
        [InlineData("file:///etc/passwd", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void 二维码图片只取供应商域名下的https图片(string? url, bool expect)
        {
            Assert.Equal(expect, SkipassSaleRules.IsAllowedQrImageUrl(url));
        }

        static readonly byte[] PngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        static async Task SeedQrPass(Fixture f, string? qrUrl, int memberId = 10)
        {
            await using var db = f.NewContext();
            db.skiPass.Add(new SnowmeetApi.Models.SkiPass { id = 601, order_id = 900, member_id = memberId, product_id = 901, resort = "万龙",
                product_name = "p", count = 1, deal_price = 579, valid = 1, qr_code_url = qrUrl, reserve_date = D0 });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task 二维码转发_购买者本人拿到图片_按地址缓存只取一次()
        {
            await using var f = await Fixture.Create();
            await SeedQrPass(f, "https://dimg04.c-ctrip.com/images/cache-test-1.png");
            int fetches = 0;
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            ctl.QrFetcher = url => { fetches++; return Task.FromResult((PngBytes, "image/png")); };
            var first = Assert.IsType<FileContentResult>(await ctl.QrImage(601, "staff-session"));
            Assert.Equal("image/png", first.ContentType);
            Assert.Equal(PngBytes, first.FileContents);
            Assert.IsType<FileContentResult>(await ctl.QrImage(601, "staff-session"));
            Assert.Equal(1, fetches);
        }

        [Fact]
        public async Task 二维码转发_别人的票_顾客拿不到_在职店员可以()
        {
            await using var f = await Fixture.Create();
            await SeedQrPass(f, "https://dimg04.c-ctrip.com/images/cache-test-2.png", memberId: 11);
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            ctl.QrFetcher = url => Task.FromResult((PngBytes, "image/png"));
            // 票是顾客 11 的：店员 10 能看，另一个普通会话（这里用无效会话）不行
            Assert.IsType<FileContentResult>(await ctl.QrImage(601, "staff-session"));
            Assert.IsType<BadRequestResult>(await ctl.QrImage(601, "no-such-session"));
            Assert.IsType<BadRequestResult>(await ctl.QrImage(601, ""));
        }

        [Fact]
        public async Task 二维码转发_没有会话的顾客看别人的票被拒绝()
        {
            await using var f = await Fixture.Create();
            await SeedQrPass(f, "https://dimg04.c-ctrip.com/images/cache-test-3.png", memberId: 10); // 店员自己的票
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            ctl.QrFetcher = url => Task.FromResult((PngBytes, "image/png"));
            Assert.IsType<BadRequestResult>(await ctl.QrImage(601, "customer-session")); // 顾客 11 看店员 10 的票
        }

        [Fact]
        public async Task 二维码转发_没有二维码_不存在的票_非供应商域名_取图失败()
        {
            await using var f = await Fixture.Create();
            await SeedQrPass(f, null);
            await using var db = f.NewContext();
            var ctl = f.Controller(db);
            ctl.QrFetcher = url => Task.FromResult((PngBytes, "image/png"));
            Assert.IsType<NotFoundResult>(await ctl.QrImage(601, "staff-session"));
            Assert.IsType<NotFoundResult>(await ctl.QrImage(999, "staff-session"));

            await using var f2 = await Fixture.Create();
            await SeedQrPass(f2, "https://evil.example.com/a.png");
            await using var db2 = f2.NewContext();
            var ctl2 = f2.Controller(db2);
            int fetches = 0;
            ctl2.QrFetcher = url => { fetches++; return Task.FromResult((PngBytes, "image/png")); };
            Assert.IsType<BadRequestResult>(await ctl2.QrImage(601, "staff-session"));
            Assert.Equal(0, fetches);

            await using var f3 = await Fixture.Create();
            await SeedQrPass(f3, "https://dimg04.c-ctrip.com/images/cache-test-fail.png");
            await using var db3 = f3.NewContext();
            var ctl3 = f3.Controller(db3);
            ctl3.QrFetcher = url => throw new InvalidOperationException("boom");
            var failed = Assert.IsType<StatusCodeResult>(await ctl3.QrImage(601, "staff-session"));
            Assert.Equal(502, failed.StatusCode);
        }

        // ---------------- 确认失败前先核对供应商 ----------------

        [Theory]
        [InlineData(SkipassSaleRules.SupplierOrderKind.NoOrder, SkipassSaleRules.GiveUpAction.ConfirmFailedAndRefund)]
        [InlineData(SkipassSaleRules.SupplierOrderKind.Cancelled, SkipassSaleRules.GiveUpAction.ConfirmFailedAndRefund)]
        [InlineData(SkipassSaleRules.SupplierOrderKind.LiveWithCode, SkipassSaleRules.GiveUpAction.RecordIssued)]
        [InlineData(SkipassSaleRules.SupplierOrderKind.LiveWithoutCode, SkipassSaleRules.GiveUpAction.CancelThenConfirm)]
        [InlineData(SkipassSaleRules.SupplierOrderKind.Unknown, SkipassSaleRules.GiveUpAction.Wait)]
        public void 超时后按供应商那边的订单情况决定(SkipassSaleRules.SupplierOrderKind kind, SkipassSaleRules.GiveUpAction expect)
        {
            Assert.Equal(expect, SkipassSaleRules.DecideGiveUp(kind));
        }

        // 把 501 设成「出票中超过 31 分钟」，然后跑一轮 RefreshIssuing
        static async Task<(Fixture f, SkiPassController ctl)> StuckLongAgo(Func<SkiPassController, Task>? tweak = null)
        {
            var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using (var seed = f.NewContext())
            {
                var p = await seed.skiPass.FirstAsync(x => x.id == 501);
                p.booking_now = true;
                p.update_date = DateTime.Now.AddMinutes(-31);
                seed.skiPass.Update(p);
                await seed.SaveChangesAsync();
            }
            return (f, null!);
        }

        [Fact]
        public async Task 超时_供应商那边有订单且有取票码_补记订单号_不判失败不退款()
        {
            var (f, _) = await StuckLongAgo();
            await using var _f = f;
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierOrderChecker = (pass, src) => Task.FromResult(new SkiPassController.SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.LiveWithCode, "1222999999"));
                await ctl.RefreshIssuing();
            }
            await using var check = f.NewContext();
            var pass = await check.skiPass.FirstAsync(p => p.id == 501);
            Assert.Equal("1222999999", pass.reserve_no);
            Assert.Equal(0, pass.is_cancel);
            Assert.Empty(await check.orderPaymentRefund.ToListAsync());
        }

        [Fact]
        public async Task 超时_供应商那边有订单没有取票码_先取消并确认已取消才退款_没确认就不退()
        {
            // 取消没确认：不退款，票还在出票中
            var (f, _) = await StuckLongAgo();
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierOrderChecker = (pass, src) => Task.FromResult(new SkiPassController.SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.LiveWithoutCode, "1222888888"));
                string? cancelled = null;
                ctl.SupplierOrderCanceller = (orderId, src) => { cancelled = orderId; return Task.FromResult(false); };
                await ctl.RefreshIssuing();
                Assert.Equal("1222888888", cancelled);
            }
            await using (var check = f.NewContext())
            {
                Assert.Equal(0, (await check.skiPass.FirstAsync(p => p.id == 501)).is_cancel);
                Assert.Empty(await check.orderPaymentRefund.ToListAsync());
            }
            // 取消已确认：判定出票失败并退这一张的款
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierOrderChecker = (pass, src) => Task.FromResult(new SkiPassController.SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.LiveWithoutCode, "1222888888"));
                ctl.SupplierOrderCanceller = (orderId, src) => Task.FromResult(true);
                await ctl.RefreshIssuing();
            }
            await using var after = f.NewContext();
            Assert.Equal(-2, (await after.skiPass.FirstAsync(p => p.id == 501)).is_cancel);
            var refunds = await after.orderPaymentRefund.ToListAsync();
            Assert.Single(refunds);
            Assert.Equal(579, refunds[0].amount);
            await f.DisposeAsync();
        }

        [Fact]
        public async Task 超时_核对不了供应商订单_什么都不做_不退款()
        {
            var (f, _) = await StuckLongAgo();
            await using var _f = f;
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierOrderChecker = (pass, src) => Task.FromResult(new SkiPassController.SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.Unknown, null));
                await ctl.RefreshIssuing();
                await ctl.RefreshIssuing();
            }
            await using var check = f.NewContext();
            var pass = await check.skiPass.FirstAsync(p => p.id == 501);
            Assert.Equal(0, pass.is_cancel);
            Assert.Null(pass.reserve_no);
            Assert.Empty(await check.orderPaymentRefund.ToListAsync());
        }

        [Theory]
        [InlineData(SkipassSaleRules.SupplierOrderKind.NoOrder)]
        [InlineData(SkipassSaleRules.SupplierOrderKind.Cancelled)]
        public async Task 超时_供应商那边没有订单或已取消_确认失败并退这一张的款(SkipassSaleRules.SupplierOrderKind kind)
        {
            var (f, _) = await StuckLongAgo();
            await using var _f = f;
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierOrderChecker = (pass, src) => Task.FromResult(new SkiPassController.SupplierOrderCheck(kind, null));
                await ctl.RefreshIssuing();
            }
            await using var check = f.NewContext();
            Assert.Equal(-2, (await check.skiPass.FirstAsync(p => p.id == 501)).is_cancel);
            var refunds = await check.orderPaymentRefund.ToListAsync();
            Assert.Single(refunds);
            Assert.Equal(501, refunds[0].skipass_id);
        }

        // ---------------- 已取票检查的轮询节奏 ----------------

        [Theory]
        [InlineData(6, 59, 59, false)]
        [InlineData(7, 0, 0, true)]
        [InlineData(12, 30, 0, true)]
        [InlineData(20, 59, 59, true)]
        [InlineData(21, 0, 0, false)]
        [InlineData(23, 30, 0, false)]
        [InlineData(3, 0, 0, false)]
        public void 已取票检查只在早7点到晚9点之间(int h, int m, int sec, bool expect)
        {
            Assert.Equal(expect, SkipassSaleRules.InPickupWindow(new DateTime(2026, 11, 1, h, m, sec)));
        }

        [Fact]
        public void 已取票检查每5分钟一次_后台每分钟醒一次的时间漂移不影响()
        {
            var t0 = new DateTime(2026, 11, 1, 10, 0, 0);
            Assert.True(SkipassSaleRules.ShouldCheckPickup(t0, null));                           // 第一次
            Assert.False(SkipassSaleRules.ShouldCheckPickup(t0.AddMinutes(1), t0));
            Assert.False(SkipassSaleRules.ShouldCheckPickup(t0.AddMinutes(4), t0));
            Assert.True(SkipassSaleRules.ShouldCheckPickup(t0.AddMinutes(4).AddSeconds(45), t0)); // 每分钟 +几秒漂移，第 5 次醒来时差几秒不到 5 分钟，也要查
            Assert.True(SkipassSaleRules.ShouldCheckPickup(t0.AddMinutes(5), t0));
            Assert.True(SkipassSaleRules.ShouldCheckPickup(t0.AddMinutes(30), t0));
        }

        [Fact]
        public void 已取票检查窗口外一律不查_哪怕很久没查过()
        {
            var last = new DateTime(2026, 11, 1, 20, 50, 0);
            Assert.False(SkipassSaleRules.ShouldCheckPickup(new DateTime(2026, 11, 1, 21, 5, 0), last));
            Assert.False(SkipassSaleRules.ShouldCheckPickup(new DateTime(2026, 11, 2, 6, 30, 0), last));
            Assert.True(SkipassSaleRules.ShouldCheckPickup(new DateTime(2026, 11, 2, 7, 0, 0), last)); // 次日 7 点第一轮
        }

        [Theory]
        [InlineData(0)]
        [InlineData(14)]
        public async Task 已取票检查_按游玩日期过滤的查询能正常执行_没有供应商订单号的票直接跳过(int recentDays)
        {
            await using var f = await Fixture.Create();
            await SeedPaidCartOrder(f, payMethod: "线下"); // 三张票都没有 reserve_no，不会发出网络请求
            await using var db = f.NewContext();
            await f.Controller(db).RefreshUsedAll(recentDays);
            await using var check = f.NewContext();
            Assert.All(await check.skiPass.ToListAsync(), p => Assert.Equal(0, p.is_used));
        }

        // ---------------- 退票留痕：谁、什么时候、提交了什么、供应商反馈了什么 ----------------

        static async Task SeedIssuedPass(Fixture f)
        {
            await SeedPaidCartOrder(f, payMethod: "线下");
            await using var seed = f.NewContext();
            var p = await seed.skiPass.FirstAsync(x => x.id == 501);
            p.reserve_no = "1222500001";
            seed.skiPass.Update(p);
            await seed.SaveChangesAsync();
        }

        static async Task<List<CoreDataModLog>> LogsOf(Fixture f, int skipassId)
        {
            await using var db = f.NewContext();
            return await db.coreDataModLog.Where(l => l.table_name == "ski_pass" && l.key_value == skipassId).OrderBy(l => l.id).ToListAsync();
        }

        [Fact]
        public async Task 退票_供应商即时确认_按这一张退款_每一步都留痕()
        {
            await using var f = await Fixture.Create();
            await SeedIssuedPass(f);
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierCancelSubmitter = (pass, src) => Task.FromResult(new SkiPassController.SupplierCancelFeedback(1, "取消成功", "7069756", 579));
                var result = await ctl.Cancel(501, "staff-session");
                Assert.IsType<OkObjectResult>(result.Result);
            }
            await using var check = f.NewContext();
            Assert.Equal(1, (await check.skiPass.FirstAsync(p => p.id == 501)).is_cancel);
            var refunds = await check.orderPaymentRefund.Where(r => r.order_id == 900).ToListAsync();
            Assert.Single(refunds);
            Assert.Equal(579, refunds[0].amount);
            Assert.Equal(501, refunds[0].skipass_id);

            var logs = await LogsOf(f, 501);
            Assert.Equal(new[] { "退票申请提交", "供应商退票反馈", "退款未发出" }, logs.Select(l => l.scene).ToArray());
            Assert.Equal(10, logs[0].member_id);           // 谁点的
            Assert.Equal(28, logs[0].staff_id);
            Assert.Contains("1222500001", logs[0].manual_memo);
            Assert.Contains("cancelState=1", logs[1].manual_memo); // 供应商反馈了什么
            Assert.Contains("取消成功", logs[1].manual_memo);
            Assert.Contains("cancelId=7069756", logs[1].manual_memo);
            Assert.All(logs, l => Assert.True(DateTime.Now - l.create_date < TimeSpan.FromMinutes(1))); // 什么时候
        }

        [Fact]
        public async Task 退票_供应商提交待审核_不退款_记下提交和反馈()
        {
            await using var f = await Fixture.Create();
            await SeedIssuedPass(f);
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierCancelSubmitter = (pass, src) => Task.FromResult(new SkiPassController.SupplierCancelFeedback(2, "取消申请已提交", "7069757", 0));
                await ctl.Cancel(501, "staff-session");
            }
            await using var check = f.NewContext();
            var pass = await check.skiPass.FirstAsync(p => p.id == 501);
            Assert.Equal(2, pass.is_cancel);
            Assert.Contains("取消申请已提交", pass.memo);
            Assert.Empty(await check.orderPaymentRefund.ToListAsync());
            var logs = await LogsOf(f, 501);
            Assert.Equal(new[] { "退票申请提交", "供应商退票反馈" }, logs.Select(l => l.scene).ToArray());
            Assert.Contains("cancelState=2", logs[1].manual_memo);
        }

        [Fact]
        public async Task 退票_向供应商提交出错_标成取消失败_不退款_记下错误()
        {
            await using var f = await Fixture.Create();
            await SeedIssuedPass(f);
            await using (var db = f.NewContext())
            {
                var ctl = f.Controller(db);
                ctl.SupplierCancelSubmitter = (pass, src) => throw new InvalidOperationException("供应商接口超时");
                await ctl.Cancel(501, "staff-session");
            }
            await using var check = f.NewContext();
            Assert.Equal(-1, (await check.skiPass.FirstAsync(p => p.id == 501)).is_cancel);
            Assert.Empty(await check.orderPaymentRefund.ToListAsync());
            var logs = await LogsOf(f, 501);
            Assert.Equal(new[] { "退票申请提交", "供应商退票出错" }, logs.Select(l => l.scene).ToArray());
            Assert.Contains("供应商接口超时", logs[1].manual_memo);
        }

        [Fact]
        public async Task 确认出票失败_留痕写明原因()
        {
            var (f, _) = await StuckLongAgo();
            await using var _f = f;
            await using (var db = f.NewContext())
            {
                await f.Controller(db).RefreshIssuing();
            }
            var logs = await LogsOf(f, 501);
            Assert.Contains("确认出票失败", logs.Select(l => l.scene));
            Assert.Contains("供应商那边没有这张票的有效订单", logs.First(l => l.scene == "确认出票失败").manual_memo);
            Assert.Contains("退款未发出", logs.Select(l => l.scene)); // 夹具里是线下付款，不会真发微信退款
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

            // 供应商订单核对默认「没有订单」、取消默认「已确认取消」：测试里不发真实请求，需要别的结果时在测试里改
            public SkiPassController Controller(ApplicationDBContext db) =>
                new(db, Config, Http)
                {
                    BalanceProvider = source => Task.FromResult(Balances.TryGetValue(source, out var b) ? b : 0),
                    SupplierOrderChecker = (pass, source) => Task.FromResult(new SkiPassController.SupplierOrderCheck(SkipassSaleRules.SupplierOrderKind.NoOrder, null)),
                    SupplierOrderCanceller = (orderId, source) => Task.FromResult(true)
                };

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

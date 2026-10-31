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
    /// 万龙雪票下单入口 ReserveSkiPass 的店员闸门（2026-10-29 用户拍板：顾客能看价格，不能订票）。
    /// 页面隐藏支付栏不算数，这里验证的是后端：顾客、无会话一律 400 且不建订单；店员正常下单，成交价取库里的 deal_price。
    /// 以及顾客看不到进货价（GetProductsByResort / GetProduct / GetBalance）。
    /// </summary>
    public class SkipassReserveGateTests
    {
        static readonly DateTime TravelDate = DateTime.Today.AddDays(3);

        [Fact]
        public async Task 顾客会话下单_被拒绝_不建订单()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).ReserveSkiPass(901, TravelDate, 1, "13800138000", "张三", "customer-session");
            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal(SkipassSaleRules.STAFF_ONLY_MESSAGE, bad.Value);
            Assert.Empty(await db.order.ToListAsync());
            Assert.Empty(await db.skiPass.ToListAsync());
        }

        [Theory]
        [InlineData("")]
        [InlineData("no-such-session")]
        public async Task 没有会话或会话无效_被拒绝_不建订单(string sessionKey)
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).ReserveSkiPass(901, TravelDate, 1, "13800138000", "张三", sessionKey);
            Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Empty(await db.order.ToListAsync());
        }

        [Fact]
        public async Task 已停用店员的会话_被拒绝()
        {
            await using var f = await Fixture.Create(staffValid: 0);
            await using var db = f.NewContext();
            var result = await f.Controller(db).ReserveSkiPass(901, TravelDate, 1, "13800138000", "张三", "staff-session");
            Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Empty(await db.order.ToListAsync());
        }

        [Fact]
        public async Task 店员会话下单_通过闸门_成交价取当天价格行的deal_price()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).ReserveSkiPass(901, TravelDate, 1, "13800138000", "张三", "staff-session");
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var order = Assert.IsType<SnowmeetApi.Models.Order>(ok.Value);
            Assert.Equal(579, order.total_amount);
            var skipass = await db.skiPass.SingleAsync();
            Assert.Equal(579, skipass.deal_price);
            Assert.Equal("张三", skipass.contact_name);
        }

        [Fact]
        public async Task 顾客看商品列表_没有进货价()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).GetProductsByResort("万龙", 0, "customer-session");
            var list = Assert.IsType<List<SnowmeetApi.Controllers.SkiPassController.SkipassWithPrice>>(
                Assert.IsType<OkObjectResult>(result.Result).Value);
            var item = list.Single(x => x.product_id == 901);
            Assert.Null(item.cost);
            Assert.NotEmpty(item.dailyPrice);
            Assert.All(item.dailyPrice, d => { Assert.Equal(0, d.settlementPrice); Assert.Equal(0, d.salePrice); });
            Assert.Equal(579, item.dailyPrice[0].deal_price);
            Assert.Equal(579, item.dailyPrice[0].marketPrice);
        }

        [Fact]
        public async Task 没带会话看商品列表_也没有进货价()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).GetProductsByResort("万龙", 0);
            var item = ListOf(result).Single(x => x.product_id == 901);
            Assert.Null(item.cost);
            Assert.All(item.dailyPrice, d => Assert.Equal(0, d.settlementPrice));
        }

        [Fact]
        public async Task 店员看商品列表_有进货价()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var result = await f.Controller(db).GetProductsByResort("万龙", 0, "staff-session");
            var item = ListOf(result).Single(x => x.product_id == 901);
            Assert.Equal(299, item.cost);
            Assert.All(item.dailyPrice, d => Assert.Equal(299, d.settlementPrice));
        }

        static List<SnowmeetApi.Controllers.SkiPassController.SkipassWithPrice> ListOf(
            ActionResult<List<SnowmeetApi.Controllers.SkiPassController.SkipassWithPrice>> result)
        {
            return Assert.IsType<List<SnowmeetApi.Controllers.SkiPassController.SkipassWithPrice>>(
                Assert.IsType<OkObjectResult>(result.Result).Value);
        }

        [Fact]
        public async Task 商品列表同时包含万龙自我游和大好河山来源_其他来源和隐藏的不出现()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var list = ListOf(await f.Controller(db).GetProductsByResort("万龙", 0));
            Assert.Equal(new[] { 901, 902 }, list.Select(x => x.product_id).OrderBy(x => x).ToArray());
            Assert.Equal("大好河山", list.Single(x => x.product_id == 902).source);
            Assert.Equal("万龙自我游", list.Single(x => x.product_id == 901).source);
        }

        [Fact]
        public async Task 显示隐藏商品时_隐藏的万龙自我游商品也在_其他来源仍不出现()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var list = ListOf(await f.Controller(db).GetProductsByResort("万龙", 1, "staff-session"));
            Assert.Equal(new[] { 901, 902, 904 }, list.Select(x => x.product_id).OrderBy(x => x).ToArray());
        }

        [Fact]
        public async Task 大好河山来源的商品_顾客同样看不到进货价()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var item = ListOf(await f.Controller(db).GetProductsByResort("万龙", 0, "customer-session")).Single(x => x.product_id == 902);
            Assert.Null(item.cost);
            Assert.NotEmpty(item.dailyPrice);
            Assert.All(item.dailyPrice, d => { Assert.Equal(0, d.settlementPrice); Assert.Equal(399, d.deal_price); });
        }

        [Fact]
        public async Task 顾客看单个商品_没有进货价_店员有()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var customer = Assert.IsType<OkObjectResult>(
                (await f.Controller(db).GetProduct(901, "customer-session")).Result);
            var cItem = Assert.IsType<SnowmeetApi.Controllers.SkiPassController.SkipassWithPrice>(customer.Value);
            Assert.Null(cItem.cost);
            Assert.All(cItem.dailyPrice, d => Assert.Equal(0, d.settlementPrice));

            var staff = Assert.IsType<OkObjectResult>(
                (await f.Controller(db).GetProduct(901, "staff-session")).Result);
            var sItem = Assert.IsType<SnowmeetApi.Controllers.SkiPassController.SkipassWithPrice>(staff.Value);
            Assert.Equal(299, sItem.cost);
            Assert.All(sItem.dailyPrice, d => Assert.Equal(299, d.settlementPrice));
        }

        [Fact]
        public async Task 自我游商品详情_顾客没有进货价_店员有()
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var helper = new WanlongZiwoyouHelper(db, f.Config);

            var customer = Assert.IsType<OkObjectResult>((await helper.GetProductById(901, "customer-session")).Result);
            var cJson = Newtonsoft.Json.JsonConvert.SerializeObject(customer.Value);
            var cObj = Newtonsoft.Json.Linq.JObject.Parse(cJson);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, cObj["cost"]!.Type);
            Assert.All(cObj["dailyPrice"]!, d => Assert.Equal(0, (double)d["settlementPrice"]!));

            var staff = Assert.IsType<OkObjectResult>((await helper.GetProductById(901, "staff-session")).Result);
            var sObj = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(staff.Value));
            Assert.Equal(299, (double)sObj["cost"]!);
            Assert.All(sObj["dailyPrice"]!, d => Assert.Equal(299, (double)d["settlementPrice"]!));
        }

        [Theory]
        [InlineData("customer-session")]
        [InlineData("")]
        [InlineData(null)]
        public async Task 顾客或无会话查预存款_401(string? sessionKey)
        {
            await using var f = await Fixture.Create();
            await using var db = f.NewContext();
            var helper = new WanlongZiwoyouHelper(db, f.Config);
            var result = await helper.GetBalance(sessionKey);
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly SqliteConnection connection;
            public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
            public IHttpContextAccessor Http { get; } = new TestHttpContextAccessor();

            private Fixture(SqliteConnection connection) { this.connection = connection; }

            public ApplicationDBContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

            public double Balance { get; set; } = 100000;

            // 预存款查询换成假的：订票前会核对各自我游账号的预存款，测试里不发真实请求
            public SkiPassController Controller(ApplicationDBContext db) =>
                new(db, Config, Http) { BalanceProvider = _ => Task.FromResult(Balance) };

            public static async Task<Fixture> Create(int staffValid = 1)
            {
                var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
                await connection.OpenAsync();
                var f = new Fixture(connection);
                await using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
                await db.Database.EnsureCreatedAsync();

                db.member.Add(new Member { id = 10, real_name = "店员", gender = "男" });
                db.member.Add(new Member { id = 11, real_name = "顾客", gender = "男" });
                db.Set<Staff>().Add(new Staff { id = 28, title_level = 300, valid = staffValid });
                db.Set<SocialAccountForJob>().Add(new SocialAccountForJob { id = 1, member_id = 10, wechat_mini_openid = "staff-openid" });
                db.Set<StaffSocialAccount>().Add(new StaffSocialAccount { staff_id = 28, social_account_id = 1, valid = 1 });
                db.miniSession.Add(new MiniSession { session_key = "staff-session", session_type = "wechat_mini_openid", member_id = 10,
                    wechat_openid = "staff-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
                db.miniSession.Add(new MiniSession { session_key = "customer-session", session_type = "wechat_mini_openid", member_id = 11,
                    wechat_openid = "customer-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });

                db.product.Add(new Product { id = 901, name = "【26-27雪季】1天日场自带板滑雪票", type = "雪票", sale_price = 579,
                    market_price = 579, cost = 299, hidden = 0, start_date = new DateTime(2026, 10, 1), end_date = new DateTime(2027, 6, 1) });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 901, resort = "万龙", source = "万龙自我游",
                    third_party_no = "37696034", rules = "测试规则", tags = "", available_days = "", unavailable_days = "" });
                for (int i = 0; i < 7; i++)
                {
                    db.skipassDailyPrice.Add(new SkipassDailyPrice { product_id = 901, third_party_id = "37696034",
                        reserve_date = DateTime.Today.AddDays(i), settlementPrice = 299, salePrice = 299, marketPrice = 579,
                        deal_price = 579, day_type = "平日", valid = 1 });
                }
                // 万龙试营业的票：大好河山来源；另有别的来源的商品（不该出现）和被隐藏的万龙自我游商品
                db.product.Add(new Product { id = 902, name = "【万龙】全雪区日场1天雪票-11.6", type = "雪票", sale_price = 399,
                    market_price = 399, cost = 371.07, hidden = 0, start_date = new DateTime(2026, 10, 1), end_date = new DateTime(2027, 6, 1) });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 902, resort = "万龙", source = "大好河山",
                    third_party_no = "37704649", rules = "试营业", tags = "", available_days = "", unavailable_days = "" });
                db.product.Add(new Product { id = 903, name = "别的渠道的雪票", type = "雪票", sale_price = 100, hidden = 0 });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 903, resort = "万龙", source = "别的渠道",
                    third_party_no = "99999999", rules = "", tags = "", available_days = "", unavailable_days = "" });
                db.product.Add(new Product { id = 904, name = "【25-26雪季】被隐藏的票", type = "雪票", sale_price = 100, hidden = 1 });
                db.skiPassProduct.Add(new SnowmeetApi.Models.SkiPassProduct { product_id = 904, resort = "万龙", source = "万龙自我游",
                    third_party_no = "88888888", rules = "", tags = "", available_days = "", unavailable_days = "" });
                for (int i = 0; i < 7; i++)
                {
                    db.skipassDailyPrice.Add(new SkipassDailyPrice { product_id = 902, third_party_id = "37704649",
                        reserve_date = DateTime.Today.AddDays(i), settlementPrice = 371.07, salePrice = 399, marketPrice = 399,
                        deal_price = 399, day_type = "平日", valid = 1 });
                }
                await db.SaveChangesAsync();
                return f;
            }

            public async ValueTask DisposeAsync() { await connection.DisposeAsync(); }
        }

        // 框架自带的 HttpContextAccessor 靠 AsyncLocal 保存，跨 await 回不来；控制器构造函数要读 Request.Host
        private sealed class TestHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
        }
    }
}

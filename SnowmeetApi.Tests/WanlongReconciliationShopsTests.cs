using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-17 用户：万龙服务中心的租赁订单也要给万龙分账，万龙对账页（background/rent/wl_list.html）也要看得到。
// 万龙对账账号（职级 50）调 GetConfirmedRentOrder 时门店被固定，以前只有万龙体验中心；现在是体验中心 + 服务中心，别的门店仍看不到。
public class WanlongReconciliationShopsTests
{
    private static async Task<(TestServer server, SqliteConnection connection)> StartAsync(int titleLevel)
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Settings:AppId"] = "local-test" }).Build());
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDBContext>(o => o.UseSqlite(connection)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddControllers().AddApplicationPart(typeof(RentController).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
        }).Configure(app => { app.UseRouting(); app.UseEndpoints(e => e.MapControllers()); }));
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
        await db.Database.EnsureCreatedAsync();
        DateTime day = DateTime.Today.AddDays(-5);
        db.AddRange(new Staff { id = 7, valid = 1, title_level = titleLevel }, new Member { id = 8 },
            new SocialAccountForJob { id = 9, member_id = 8, wechat_mini_openid = "test" },
            new StaffSocialAccount { id = 10, staff_id = 7, social_account_id = 9, valid = 1, start_date = DateTime.Today.AddDays(-30) },
            new MiniSession { session_key = "test", session_type = "wechat_mini_openid", member_id = 8, valid = 1, expire_date = DateTime.Now.AddHours(1) });
        db.order.AddRange(
            new Order { id = 1, code = "WT_EXPERIENCE", type = "租赁", shop = "万龙体验中心", valid = 1, closed = 1, close_date = day, biz_date = day },
            new Order { id = 2, code = "WF_SERVICE", type = "租赁", shop = "万龙服务中心", valid = 1, closed = 1, close_date = day, biz_date = day },
            new Order { id = 3, code = "QJ_OTHER", type = "租赁", shop = "崇礼旗舰店", valid = 1, closed = 1, close_date = day, biz_date = day });
        db.orderPayment.AddRange(
            new OrderPayment { id = 11, order_id = 1, pay_method = "微信支付", status = "支付成功", amount = 100, valid = 1 },
            new OrderPayment { id = 12, order_id = 2, pay_method = "微信支付", status = "支付成功", amount = 100, valid = 1 },
            new OrderPayment { id = 13, order_id = 3, pay_method = "微信支付", status = "支付成功", amount = 100, valid = 1 });
        await db.SaveChangesAsync();
        return (server, connection);
    }

    private static async Task<string[]> CodesAsync(TestServer server, string shopParam)
    {
        using var client = server.CreateClient();
        DateTime day = DateTime.Today.AddDays(-5);
        string url = "/api/Rent/GetConfirmedRentOrder?sessionKey=test&startDate=" + day.ToString("yyyy-MM-dd")
            + "&endDate=" + day.ToString("yyyy-MM-dd") + shopParam;
        using var result = JsonDocument.Parse(await client.GetStringAsync(url));
        return result.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("code").GetString()!).OrderBy(c => c).ToArray();
    }

    // 对账页（职级 50 的万龙账号）：不管页面传什么门店，都是体验中心 + 服务中心，别的门店看不到
    [Theory]
    [InlineData("&shop=%E4%B8%87%E9%BE%99%E4%BD%93%E9%AA%8C%E4%B8%AD%E5%BF%83")]                                   // 旧页面传的「万龙体验中心」
    [InlineData("&shop=%E4%B8%87%E9%BE%99%E4%BD%93%E9%AA%8C%E4%B8%AD%E5%BF%83%2C%E4%B8%87%E9%BE%99%E6%9C%8D%E5%8A%A1%E4%B8%AD%E5%BF%83")] // 新页面传的两家
    [InlineData("&shop=%E5%B4%87%E7%A4%BC%E6%97%97%E8%88%B0%E5%BA%97")]                                           // 想看崇礼也看不到
    public async Task The_wanlong_reconciliation_account_sees_both_wanlong_shops_and_nothing_else(string shopParam)
    {
        var (server, connection) = await StartAsync(titleLevel: 50);
        await using var _ = connection;
        using var __ = server;

        Assert.Equal(new[] { "WF_SERVICE", "WT_EXPERIENCE" }, await CodesAsync(server, shopParam));
    }

    // 其它职级仍按传入的门店查，不受影响
    [Fact]
    public async Task Higher_levels_still_get_exactly_the_requested_shop()
    {
        var (server, connection) = await StartAsync(titleLevel: 200);
        await using var _ = connection;
        using var __ = server;

        Assert.Equal(new[] { "WT_EXPERIENCE" }, await CodesAsync(server, "&shop=%E4%B8%87%E9%BE%99%E4%BD%93%E9%AA%8C%E4%B8%AD%E5%BF%83"));
        Assert.Equal(new[] { "WF_SERVICE" }, await CodesAsync(server, "&shop=%E4%B8%87%E9%BE%99%E6%9C%8D%E5%8A%A1%E4%B8%AD%E5%BF%83"));
    }

    // 页面文件本身要传两家（职级更高的账号也能用这个页面看全）
    [Fact]
    public void The_page_asks_for_both_wanlong_shops()
    {
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(root, "wwwroot", "background", "rent", "wl_list.html")); i++)
        {
            root = Path.GetFullPath(Path.Combine(root, ".."));
        }
        string html = File.ReadAllText(Path.Combine(root, "wwwroot", "background", "rent", "wl_list.html"));
        Assert.Contains("encodeURIComponent('万龙体验中心,万龙服务中心')", html);
    }
}

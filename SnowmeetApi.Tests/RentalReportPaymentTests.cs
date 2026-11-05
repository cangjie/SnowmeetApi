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

public class RentalReportPaymentTests
{
    [Fact]
    public async Task Report_opt_in_includes_other_payments_without_changing_existing_callers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        using var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Settings:AppId"] = "local-test" }).Build());
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDBContext>(o => o.UseSqlite(connection)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
            services.AddControllers().AddApplicationPart(typeof(RentController).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
        }).Configure(app => { app.UseRouting(); app.UseEndpoints(e => e.MapControllers()); }));
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(new Staff { id = 7, valid = 1, title_level = 200 }, new Member { id = 8 },
                new SocialAccountForJob { id = 9, member_id = 8, wechat_mini_openid = "test" },
                new StaffSocialAccount { id = 10, staff_id = 7, social_account_id = 9, valid = 1, start_date = DateTime.Today.AddDays(-1) },
                new MiniSession { session_key = "test", session_type = "wechat_mini_openid", member_id = 8, valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.order.AddRange(
                new Order { id = 1, code = "WECHAT", type = "租赁", shop = "测试店", valid = 1, closed = 1, close_date = new DateTime(2026, 11, 4), biz_date = new DateTime(2026, 11, 4) },
                new Order { id = 2, code = "CASH", type = "租赁", shop = "测试店", valid = 1, closed = 1, close_date = new DateTime(2026, 11, 4), biz_date = new DateTime(2026, 11, 4) });
            db.orderPayment.AddRange(
                new OrderPayment { id = 11, order_id = 1, pay_method = "微信支付", status = "支付成功", amount = 100, valid = 1 },
                new OrderPayment { id = 12, order_id = 2, pay_method = "现金", status = "支付成功", amount = 200, valid = 1 });
            db.Set<OrderPaymentRefund>().AddRange(
                new OrderPaymentRefund { id = 13, order_id = 2, payment_id = 12, amount = 40, state = 1, refund_id = "" },
                new OrderPaymentRefund { id = 14, order_id = 2, payment_id = 12, amount = 30, state = 0, refund_id = "accepted" },
                new OrderPaymentRefund { id = 15, order_id = 2, payment_id = 12, amount = 90, state = 0, refund_id = "" });
            await db.SaveChangesAsync();
        }
        using var client = server.CreateClient();
        const string url = "/api/Rent/GetConfirmedRentOrder?sessionKey=test&startDate=2026-11-04&endDate=2026-11-04";
        using var oldResult = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Equal(new[] { "WECHAT" }, oldResult.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToArray());
        using var reportResult = JsonDocument.Parse(await client.GetStringAsync(url + "&includeOtherPaymentMethods=true"));
        var orders = reportResult.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, orders.Length);
        var cash = orders.Single(x => x.GetProperty("code").GetString() == "CASH");
        var payment = cash.GetProperty("availablePayments")[0];
        Assert.Equal("现金", payment.GetProperty("pay_method").GetString());
        Assert.Equal(200, payment.GetProperty("amount").GetDouble());
        Assert.Equal(70, payment.GetProperty("refundedAmount").GetDouble());
    }
}

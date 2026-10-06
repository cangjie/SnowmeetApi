using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;

namespace SnowmeetApi.Tests;

public class FnbV4HttpContractTests
{
    [Fact]
    public async Task V4RegistersDocumentedActionsAndRejectsLegacyRoutesAndInvalidRequests()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // 独立 TestHost，不调用生产 Startup，不读取生产连接文件。
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<ApplicationDBContext>(options => options.UseSqlite(connection)
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
                services.AddControllers().AddApplicationPart(typeof(FnbAuthController).Assembly);
            })
            .Configure(app => { app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapControllers()); }));
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE mini_session (session_key TEXT, session_type TEXT, wechat_openid TEXT, valid INTEGER, expire_date TEXT)");
        }
        var actions = server.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().ToArray();
        string[] expected = ["FnbAuth/GetMe", "FnbAuth/WeComLogin",
            "FnbCatalog/ListUnits", "FnbCatalog/ListCategories", "FnbCatalog/SaveCategory", "FnbCatalog/DeleteCategory",
            "FnbCatalog/ListShelfRules", "FnbCatalog/SaveShelfRule", "FnbRoute/ListItems", "FnbRoute/GetRoute",
            "FnbRoute/CreateItem", "FnbRoute/SaveItemDefaults", "FnbRoute/AddUpstreamForm", "FnbRoute/UpdateForm",
            "FnbRoute/RemoveForm", "FnbRoute/SaveSpec", "FnbRoute/FindSpecByBarcode"];
        var v4 = actions.Where(a => a.ControllerName is "FnbAuth" or "FnbCatalog" or "FnbRoute")
            .Select(a => a.ControllerName + "/" + a.ActionName).OrderBy(x => x).ToArray();
        Assert.Equal(expected.OrderBy(x => x), v4);
        string[] complete = ["FnbArea/SaveArea", "FnbCheck/Submit", "FnbSupply/PostMovement", "FnbTool/ChangeStatus",
            "FnbInbound/PostReceipt", "FnbStock/GetBatch", "FnbOperation/PostOperation", "FnbPrep/PostPreparation",
            "FnbDish/SaveSpecLines", "FnbServe/PostServe", "FnbV4Stocktake/PostStocktake", "FnbV4Report/DownloadRecipeChain", "FnbLabel/GetLabelData", "FnbV4Alert/PushExpireAlert"];
        foreach (var endpoint in complete) Assert.Contains(endpoint, actions.Select(x => x.ControllerName + "/" + x.ActionName));
        Assert.DoesNotContain(actions, a => a.ControllerName is "FnbInventory" or "FnbRecipe" or "FnbKitchen" or "FnbReport" or "FnbStocktake");
        Assert.DoesNotContain(actions, a => a.ControllerName == "FnbMaterial" &&
            a.ActionName is "GetBatches" or "SaveBatch" or "DisposeBatch" or "DeleteBatch" or "GenBatchNo" or "PushExpireAlert");
        using var client = server.CreateClient();
        var labelLink = await client.GetAsync("/fnb/b?id=9223372036854775806&source=label");
        Assert.Equal(HttpStatusCode.Redirect, labelLink.StatusCode);
        Assert.Equal("/fnb/v4/index.html?id=9223372036854775806&source=label", labelLink.Headers.Location?.OriginalString);
        var response = await client.GetAsync("/api/FnbAuth/GetMe?sessionKey=missing");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("code").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("message", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/FnbMaterial/GetBatches?shopId=1&sessionKey=missing")).StatusCode);
        var missingCode = await client.PostAsync("/api/FnbAuth/WeComLogin",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, missingCode.StatusCode);
        using var missingCodeJson = JsonDocument.Parse(await missingCode.Content.ReadAsStringAsync());
        Assert.Equal(1, missingCodeJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/FnbRoute/CreateItem?sessionKey=missing",
            new StringContent("{\"shopId\":\"invalid\"}", Encoding.UTF8, "application/json"))).StatusCode);
        var longBody = await client.PostAsync("/api/FnbServe/PostServe?sessionKey=missing", new StringContent(
            "{\"shopId\":1,\"requestId\":\"e0bbec70-3dd8-448d-a835-7e5f17bf0c01\",\"orderId\":\"9223372036854775806\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, longBody.StatusCode);
        using var longJson = JsonDocument.Parse(await longBody.Content.ReadAsStringAsync()); Assert.Equal(2, longJson.RootElement.GetProperty("code").GetInt32());
    }
}

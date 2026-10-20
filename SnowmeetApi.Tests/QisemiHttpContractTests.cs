using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers;
using SnowmeetApi.Services.Qisemi;
using Swashbuckle.AspNetCore.Swagger;

namespace SnowmeetApi.Tests;

public class QisemiHttpContractTests
{
    private sealed class Probe(QisemiSessionContext session) : IQisemiReadService
    {
        public Task<object> ReadAsync(string op, QisemiQuery q, long? id, string? key, CancellationToken ct)
        {
            if (q.Keyword == "fail") throw new InvalidOperationException("secret database internal value");
            return Task.FromResult<object>(new { op, id = id?.ToString(), session.SessionKey, session.SessionType });
        }
        public Task<QisemiFileHandle> FileAsync(long id, QisemiQuery q, CancellationToken ct) =>
            Task.FromResult(new QisemiFileHandle(new MemoryStream([1, 2, 3]), "test.png", "image/png", true));
    }
    internal static TestServer Host(Action<IServiceCollection> configure) => new(new WebHostBuilder().ConfigureServices(s =>
    {
        s.AddLogging(); s.AddScoped<QisemiSessionContext>(); s.AddScoped<QisemiSessionFilter>();
        s.AddControllers().AddApplicationPart(typeof(QisemiController).Assembly);
        s.AddSwaggerGen(o => { o.SwaggerDoc("test", new() { Title = "test", Version = "test" }); o.OperationFilter<QisemiSessionSwagger>();
            o.DocInclusionPredicate((_, d) => d.RelativePath?.StartsWith("api/Qisemi/") == true); });
        configure(s);
    }).Configure(app => { app.UseRouting(); app.UseEndpoints(e => e.MapControllers()); }));

    [Theory]
    [InlineData(null, null)]
    [InlineData("arbitrary-reserved-key", "wecom")]
    public async Task SessionHeadersAreOptionalAndReadWithoutAuthentication(string? key, string? type)
    {
        using var host = Host(s => s.AddScoped<IQisemiReadService, Probe>()); using var client = host.CreateClient();
        if (key != null) client.DefaultRequestHeaders.Add("SessionKey", key);
        if (type != null) client.DefaultRequestHeaders.Add("SessionType", type);
        var response = await client.GetAsync("/api/Qisemi/Inventory?sourceId=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(key ?? "", doc.RootElement.GetProperty("data").GetProperty("sessionKey").GetString());
        Assert.Equal(type ?? "", doc.RootElement.GetProperty("data").GetProperty("sessionType").GetString());
    }
    [Fact]
    public async Task InvalidQueriesAreRejectedAndFailuresDoNotExposeDatabaseDetails()
    {
        using var host = Host(s => s.AddScoped<IQisemiReadService, Probe>()); using var client = host.CreateClient();
        foreach (string query in new[] { "pageSize=101", "sourceId=-1", "dateFrom=not-a-date", "warehouseId=xyz", "inStockOnly=xyz", "topCategoryId=0" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/Qisemi/Inventory?" + query)).StatusCode);
        var response = await client.GetAsync("/api/Qisemi/Inventory?keyword=fail");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/api/Qisemi/Inventory", null)).StatusCode);
    }
    [Fact]
    public async Task PrivateContentIsStreamedAndNeverMadeCacheable()
    {
        using var host = Host(s => s.AddScoped<IQisemiReadService, Probe>()); using var client = host.CreateClient();
        var response = await client.GetAsync("/api/Qisemi/FileReferences/1/Content");
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
    [Theory]
    [InlineData("/wecom/qisemi")]
    [InlineData("/wecom/qisemi/")]
    public async Task FrontendEntryRedirectsWithoutAmbiguousRoutes(string path)
    {
        using var host=Host(s=>s.AddScoped<IQisemiReadService,Probe>());using var client=host.CreateClient();
        var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.Equal("/wecom/qisemi/index.html",response.Headers.Location?.OriginalString);
    }
    [Fact]
    public void SwaggerShowsBothReservedHeadersOnEveryQisemiRoute()
    {
        using var host = Host(s => s.AddScoped<IQisemiReadService, Probe>());
        var swagger = host.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("test");
        var operations = swagger.Paths.Where(x => x.Key.StartsWith("/api/Qisemi/")).SelectMany(x => x.Value.Operations.Values).ToArray();
        Assert.Equal(28, operations.Length);
        Assert.All(operations, op =>
        {
            Assert.Contains(op.Parameters, p => p.Name == "SessionKey" && !p.Required && p.In == Microsoft.OpenApi.Models.ParameterLocation.Header);
            Assert.Contains(op.Parameters, p => p.Name == "SessionType" && !p.Required);
        });
    }
}

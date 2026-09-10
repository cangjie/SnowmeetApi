using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;
using SnowmeetApi.Services.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class AdminAssistantEndpointIntegrationTests
    {
        [Theory]
        [InlineData("{\"version\":")]
        [InlineData("null")]
        [InlineData("")]
        [InlineData("{\"version\":1,\"page_key\":\"pages/admin/member/member_list\",\"question\":\"help\"}")]
        [InlineData("{\"Version\":\"1\",\"page_key\":\"pages/admin/member/member_list\",\"question\":\"help\"}")]
        [InlineData("{\"version\":\"1\",\"version\":\"1\",\"page_key\":\"pages/admin/member/member_list\",\"question\":\"help\"}")]
        [InlineData("{\"version\":\"1\",\"page_key\":\"pages/admin/member/member_list\",\"question\":\"help\",\"unknown\":true}")]
        [InlineData("{\"version\":\"1\",\"page_key\":\"pages/admin/member/member_list\",\"question\":\"help\",\"context\":{\"rental_order_query\":{\"Cell_Suffix\":\"1234\"}}}")]
        [InlineData("{\"version\":\"1\",\"page_key\":\"pages/admin/member/member_list\",\"question\":\"help\",\"context\":{\"rental_order_query\":{\"cell_suffix\":\"1234\",\"cell_suffix\":\"5678\"}}}")]
        public async Task 无效请求体始终返回统一v1错误信封(string payload)
        {
            using TestServer server = CreateServer();
            using HttpClient client = server.CreateClient();

            HttpResponseMessage response = await client.PostAsync("/api/AdminAi/AskAdminAssistantByStaff?sessionKey=trusted-session",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Equal(1, document.RootElement.GetProperty("code").GetInt32());
            Assert.Equal("请求参数不合法", document.RootElement.GetProperty("message").GetString());
            Assert.False(document.RootElement.TryGetProperty("title", out _));
        }

        private static TestServer CreateServer() => new(new WebHostBuilder().UseStartup<TestStartup>());

        private sealed class TestStartup
        {
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddDbContext<ApplicationDBContext>(options => options.UseSqlite("Data Source=:memory:"));
                services.AddSingleton<IHttpClientFactory, EmptyHttpClientFactory>();
                services.AddSingleton<IAdminAssistantService, RejectingAssistantService>();
                services.AddHttpContextAccessor();
                services.AddControllers().AddApplicationPart(typeof(AdminAiController).Assembly);
            }

            public void Configure(IApplicationBuilder app)
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }
        }

        private sealed class RejectingAssistantService : IAdminAssistantService
        {
            public Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Staff staff, string traceId,
                bool structuredEnabled, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("invalid bodies must not reach the assistant service");
        }

        private sealed class EmptyHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new();
        }
    }
}

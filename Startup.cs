using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnowmeetApi.Data;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models.Users;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
//using Microsoft.AspNetCore.Mvc.NewtonsoftJson;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SnowmeetApi
{
    public class Startup
    {
        public ApplicationDBContext? _db = null;
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            string conStr = Util.GetSqlServerConnectionString();


            services.AddControllers();
            services.AddDbContext<ApplicationDBContext>(
                options => options.UseSqlServer(conStr)
            );
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v2", new OpenApiInfo { Title = "SnowmeetApi", Version = "v2" });
            });
            services.ConfigureSwaggerGen(options => 
            {
                options.CustomSchemaIds(x => x.FullName);
            });
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddControllers().AddJsonOptions(options =>{
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            IConfiguration config = app.ApplicationServices.GetService<IConfiguration>();
            IHttpContextAccessor http = app.ApplicationServices.GetService<IHttpContextAccessor>();
            app.UseStaticFiles();
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => 
            {
                c.SwaggerEndpoint("/swagger/v2/swagger.json", "SnowmeetApi v2");
            });
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseWebSockets();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        
                        await Echo(webSocket, config, http);
                        Console.WriteLine(webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }
                }
                else
                {
                    await next(context);
                }
            });
        }
        private static async Task Echo(WebSocket webSocket, IConfiguration config, IHttpContextAccessor http)
        {
            var buffer = new byte[1024 * 1024 * 10];
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);
            while (!receiveResult.CloseStatus.HasValue)
            {
                string receiveMessage = System.Text.Encoding.UTF8.GetString(buffer).Trim();
                receiveMessage = receiveMessage.Replace("\0", "");
                string ret = await Util.DealWebSocketMessage(receiveMessage, config, http);
                var retBuff =System.Text.Encoding.UTF8.GetBytes(ret);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(retBuff, 0, retBuff.Length),
                    receiveResult.MessageType,
                    receiveResult.EndOfMessage,
                    CancellationToken.None);
                receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);
            }
            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }
    }
}

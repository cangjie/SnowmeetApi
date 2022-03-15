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
namespace SnowmeetApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //string conStr = "Data Source=(local);Initial Catalog=snowmeet;Integrated Security=True";
            //string conStr = "Server=52.83.254.45;Database=snowmeet;UID=sa;PWD=Jarrod780209";

            string path = $"{Environment.CurrentDirectory}";

            if (path.StartsWith("/"))
            {
                path = path + "/";
            }
            else
            {
                path = path + "\\";
            }
            path = path + "config.sqlServer";

            string conStr = "";

            using (StreamReader sr = new StreamReader(path, true))
            {
                conStr = sr.ReadToEnd();
                sr.Close();
            }


            services.AddControllers();
            services.AddDbContext<ApplicationDBContext>(
                options => options.UseSqlServer(conStr)
            );
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SnowmeetApi", Version = "v1" });
            });
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            /*
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SnowmeetApi v1"));
            }
            */
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SnowmeetApi v1"));

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}

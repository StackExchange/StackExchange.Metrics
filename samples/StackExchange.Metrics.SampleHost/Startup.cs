using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.SampleHost
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment hostingEnvironment)
        {
            Configuration = configuration;
            HostingEnvironment = hostingEnvironment;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment HostingEnvironment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMetricsCollector()
                .ConfigureSources(
                    o =>
                    {
                        o.DefaultTags.Add("host", Environment.MachineName);
                    })
                .AddDefaultSources().AddSource<AppMetricSource>()
                .UseExceptionHandler(ex => Console.WriteLine(ex))
                .Configure(
                    o =>
                    {
                        o.SnapshotInterval = TimeSpan.FromSeconds(5);
                    }
                );

            services.Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true);
        }

        static readonly ReadOnlyMemory<byte> _indexPageBytes = Encoding.UTF8.GetBytes("<html><head><title>Hello World</title></head><body>Hello World</body></html>");

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.Use(async (ctx, next) =>
            {
                var appMetrics = ctx.RequestServices.GetService<AppMetricSource>();
                appMetrics.OnRequest(ctx.Request.Path);
                await next();
                if (ctx.Response.StatusCode >= StatusCodes.Status400BadRequest)
                {
                    appMetrics.OnError(ctx.Request.Path, ctx.Response.StatusCode);
                }
            });

            app.UseEndpoints(
                e =>
                {
                    e.Map("/", async ctx => await ctx.Response.BodyWriter.WriteAsync(_indexPageBytes));
                    e.Map("/error", _ => throw new Exception("BOOM"));
                    e.Map("/status-code", ctx => {
                        ctx.Response.StatusCode = int.TryParse(ctx.Request.Query["v"], out var statusCode) ? statusCode : StatusCodes.Status200OK;
                        return Task.CompletedTask;
                    });
                    e.Map("/metrics", async ctx =>
                    {
                        var collector = ctx.RequestServices.GetService<MetricsCollector>();
                        var creationOptions = ctx.RequestServices.GetService<MetricSourceOptions>();
                        ctx.Response.ContentType = "text/plain";
                        using (var streamWriter = new StreamWriter(ctx.Response.Body, Encoding.UTF8, 1024, leaveOpen: true))
                        {
                            var r = new Random();
                            // allocaaaaaaaaaaaaaaaaaaaaaaaate!
                            // GC counters are only updated when collections happen - so make collections happen
                            for (var i = 0; i < 100; i++)
                            {
                                var s = new string(' ', r.Next(5_000, 150_000));
                            }
                            await collector.DumpAsync(streamWriter, creationOptions);
                        }
                    });
                }
            );
        }
    }
}

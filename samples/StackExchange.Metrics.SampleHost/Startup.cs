using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
                .AddDefaultSets()
                .AddDefaultTag("role", "sample_host")
                .UseExceptionHandler(ex => Console.WriteLine(ex))
                .Configure(
                    o =>
                    {
                        o.MetricsNamePrefix = "sample_host.";
                        o.SnapshotInterval = TimeSpan.FromSeconds(5);
                    }
                );

            services.AddSingleton<PerfCounters>();
            services.Configure<KestrelServerOptions>(o => o.AllowSynchronousIO = true);
        }

        static readonly ReadOnlyMemory<byte> _indexPageBytes = Encoding.UTF8.GetBytes("<html><head><title>Hello World</title></head><body>Hello World</body></html>");

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.Use((ctx, next) =>
            {
                var perfCounters = ctx.RequestServices.GetService<PerfCounters>();
                perfCounters.IncrementMyCounter(ctx.Request.Path, MyCounterCategory.Example_One);
                return next();
            });

            app.UseEndpoints(
                e =>
                {
                    e.Map("/", async ctx => await ctx.Response.BodyWriter.WriteAsync(_indexPageBytes));
                    e.Map("/metrics", async ctx =>
                    {
                        var collector = ctx.RequestServices.GetService<MetricsCollector>();
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
                            await collector.DumpAsync(streamWriter);
                        }
                    });
                }
            );
        }
    }
}

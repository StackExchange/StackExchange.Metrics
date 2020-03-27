using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace StackExchange.Metrics.SampleHost
{
    public static class Program
    {
        public static Task Main(string[] args) => CreateWebHostBuilder(args).Build().RunAsync();
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) => WebHost.CreateDefaultBuilder(args).UseStartup<Startup>();
    }
}

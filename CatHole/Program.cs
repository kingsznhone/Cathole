using System.Net;
using System.Text.Json;
using CatHole;
using CatHole.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
namespace CatHole
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {

            var host = Host.CreateDefaultBuilder(args)
            .UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext())
            .ConfigureServices((context, services) =>
            {
                // 简化版本：直接注册 RelayManager 和 HostedService
                services.AddRelayHostedService();
            })
            .Build();

            await host.RunAsync();
        }
    }
}

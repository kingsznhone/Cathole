using CatHole.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CatHole.CLI
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
                services.AddSingleton<CatHoleRelayManager>(sp =>
                {
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    return new CatHoleRelayManager(loggerFactory);
                });
                services.AddHostedService<RelayHostedService>();
            })
            .Build();

            await host.RunAsync();
        }
    }
}

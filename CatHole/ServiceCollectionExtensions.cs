using CatHole.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CatHole;

/// <summary>
/// Extension methods for registering CatHole services in DI container
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RelayManager as a singleton service
    /// </summary>
    public static IServiceCollection AddRelayManager(this IServiceCollection services)
    {
        services.AddSingleton<RelayManager>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new RelayManager(loggerFactory);
        });

        return services;
    }

    /// <summary>
    /// Adds RelayHostedService which automatically loads and manages relays from configuration
    /// </summary>
    public static IServiceCollection AddRelayHostedService(this IServiceCollection services)
    {
        services.AddRelayManager();
        services.AddHostedService<RelayHostedService>();

        return services;
    }
}

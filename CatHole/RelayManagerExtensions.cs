using CatHole.Core;
using Microsoft.Extensions.Configuration;

namespace CatHole;

/// <summary>
/// Extension methods for RelayManager configuration
/// </summary>
public static class RelayManagerExtensions
{
    /// <summary>
    /// Loads relays from IConfiguration section
    /// </summary>
    /// <param name="manager">The RelayManager instance</param>
    /// <param name="configuration">The configuration root</param>
    /// <param name="sectionName">The configuration section name (default: "Relays")</param>
    /// <returns>Number of relays loaded</returns>
    public static int LoadFromConfiguration(
        this CatHoleRelayManager manager,
        IConfiguration configuration,
        string sectionName = "Relays")
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(configuration);

        var relayOptions = configuration.GetSection(sectionName).Get<List<CatHoleRelayOption>>();

        if (relayOptions == null || relayOptions.Count == 0)
        {
            return 0;
        }

        return manager.AddRelays(relayOptions);
    }
}

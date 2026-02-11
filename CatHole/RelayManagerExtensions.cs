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
        this RelayManager manager,
        IConfiguration configuration,
        string sectionName = "Relays")
    {
        if (manager == null)
            throw new ArgumentNullException(nameof(manager));
        
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var relayOptions = configuration.GetSection(sectionName).Get<List<RelayOption>>();

        if (relayOptions == null || relayOptions.Count == 0)
        {
            return 0;
        }

        return manager.AddRelays(relayOptions);
    }

    /// <summary>
    /// Loads relays from IConfiguration section asynchronously
    /// </summary>
    public static Task<int> LoadFromConfigurationAsync(
        this RelayManager manager,
        IConfiguration configuration,
        string sectionName = "Relays")
    {
        var count = manager.LoadFromConfiguration(configuration, sectionName);
        return Task.FromResult(count);
    }
}

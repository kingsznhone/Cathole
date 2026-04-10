using CatFlap.Core;

namespace CatFlap.Panel.Services;

/// <summary>
/// Hosted service that loads persisted relay configs on startup
/// and performs a graceful shutdown of all relays on stop.
/// </summary>
public sealed class RelayStartupService : IHostedService
{
    private readonly RelayConfigService _config;
    private readonly CatFlapRelayManager _manager;
    private readonly ILogger<RelayStartupService> _logger;

    public RelayStartupService(RelayConfigService config, CatFlapRelayManager manager, ILogger<RelayStartupService> logger)
    {
        _config = config;
        _manager = manager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = await _config.GetAllAsync();
        if (options.Count == 0)
        {
            _logger.LogInformation("No relay configs found, starting with no active relays");
            return;
        }

        var started = _manager.AddRelays(options);
        _logger.LogInformation("Started {Started}/{Total} relays from config", started, options.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _manager.StopAllAsync(cancellationToken).ConfigureAwait(false);
    }
}

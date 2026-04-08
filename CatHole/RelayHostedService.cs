using CatHole.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CatHole;

/// <summary>
/// Hosted service implementation for ASP.NET Core applications.
/// Automatically starts relays on application start and stops them on shutdown.
/// </summary>
public class RelayHostedService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RelayHostedService> _logger;
    private readonly CatHoleRelayManager _relayManager;

    public RelayHostedService(
        IConfiguration configuration,
        ILogger<RelayHostedService> logger,
        CatHoleRelayManager relayManager)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(relayManager);

        _configuration = configuration;
        _logger = logger;
        _relayManager = relayManager;
    }

    /// <summary>
    /// Gets the underlying RelayManager for runtime management
    /// </summary>
    public CatHoleRelayManager Manager => _relayManager;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Relay Hosted Service");

        try
        {
            var count = _relayManager.LoadFromConfiguration(_configuration);

            if (count == 0)
            {
                _logger.LogWarning("No relay configurations found in appsettings.json");
            }
            else
            {
                _logger.LogInformation("Started {Count} relays successfully", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start relays");
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Relay Hosted Service");

        try
        {
            await _relayManager.StopAllAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Relay Hosted Service stopped successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Relay shutdown cancelled by host timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping relays");
            throw;
        }
    }
}

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
    private readonly RelayManager _relayManager;

    public RelayHostedService(
        IConfiguration configuration,
        ILogger<RelayHostedService> logger,
        RelayManager relayManager)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _relayManager = relayManager ?? throw new ArgumentNullException(nameof(relayManager));
    }

    /// <summary>
    /// Gets the underlying RelayManager for runtime management
    /// </summary>
    public RelayManager Manager => _relayManager;

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
            await _relayManager.StopAllAsync();
            _logger.LogInformation("Relay Hosted Service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping relays");
            throw;
        }
    }
}

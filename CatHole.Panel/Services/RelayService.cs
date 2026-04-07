using System.Net;
using CatHole.Core;

namespace CatHole.Panel.Services;

/// <summary>
/// Orchestration service that coordinates RelayConfigService (persistence)
/// and CatHoleRelayManager (runtime). Blazor components should inject this
/// as their single entry point for all relay operations.
/// </summary>
public sealed class RelayService
{
    private readonly RelayConfigService _config;
    private readonly CatHoleRelayManager _manager;
    private readonly ILogger<RelayService> _logger;

    public RelayService(RelayConfigService config, CatHoleRelayManager manager, ILogger<RelayService> logger)
    {
        _config = config;
        _manager = manager;
        _logger = logger;
    }

    /// <summary>Returns all configured relays (source of truth is the config file).</summary>
    public Task<IReadOnlyList<CatHoleRelayOption>> GetAllOptions() => _config.GetAllAsync();

    /// <summary>
    /// Returns a snapshot of all currently running relay instances.
    /// Hold the instance across renders to read fresh <see cref="CatHoleRelay.Statistics"/>
    /// on each tick without re-fetching. Re-fetch the list only after Add/Remove/Update.
    /// </summary>
    public IReadOnlyList<CatHoleRelay> GetAllRelays() => _manager.GetAllRelays();

    /// <summary>UI pre-validation hint. Uniqueness is still enforced atomically inside AddAsync/UpdateAsync.</summary>
    public Task<bool> ExistsAsync(string name) => _config.ExistsAsync(name);

    /// <summary>Returns runtime statistics for a relay, or null if it is not currently running.</summary>
    public CatHoleRelayStatistics? GetStatistics(Guid id)
        => _manager.TryGetRelay(id, out var relay) ? relay!.Statistics : null;

    /// <summary>
    /// Adds and starts a new relay.
    /// Order: validate → persist → start runtime.
    /// If runtime start fails, the config write is rolled back.
    /// </summary>
    public async Task AddRelayAsync(CatHoleRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        CatHoleRelayFactory.ValidateOption(option);

        await CheckPortConflictAsync(option, excludeId: null);

        await _config.AddAsync(option);
        try
        {
            _manager.AddRelay(option);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start relay '{Name}' after persisting; rolling back config", option.Name);
            await _config.RemoveAsync(option.Id);
            throw;
        }
    }

    /// <summary>
    /// Stops and removes a relay.
    /// Order: stop runtime → remove from config.
    /// </summary>
    public async Task RemoveRelayAsync(Guid id)
    {
        await _manager.RemoveRelayAsync(id);
        await _config.RemoveAsync(id);
    }

    /// <summary>
    /// Stops the old relay, updates its config, then restarts with the new option.
    /// If the restart fails, the config is already updated; the user can retry.
    /// </summary>
    public async Task UpdateRelayAsync(Guid id, CatHoleRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        CatHoleRelayFactory.ValidateOption(option);

        await CheckPortConflictAsync(option, excludeId: id);

        await _manager.RemoveRelayAsync(id);
        await _config.UpdateAsync(id, option);
        try
        {
            _manager.AddRelay(option);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to restart relay '{Name}' [{Id}] after update; config is saved but relay is not running",
                option.Name, id);
            throw;
        }
    }

    private async Task CheckPortConflictAsync(CatHoleRelayOption option, Guid? excludeId)
    {
        var all = await _config.GetAllAsync();
        var newEndpoint = IPEndPoint.Parse(option.ListenHost);
        foreach (var existing in all)
        {
            if (excludeId.HasValue && existing.Id == excludeId.Value) continue;
            var existingEndpoint = IPEndPoint.Parse(existing.ListenHost);

            if (existingEndpoint.Port != newEndpoint.Port) continue;

            // Conflict when either side binds all interfaces, or both bind the same specific IP
            bool ipOverlap = existingEndpoint.Address.Equals(newEndpoint.Address)
                || existingEndpoint.Address.Equals(IPAddress.Any)
                || existingEndpoint.Address.Equals(IPAddress.IPv6Any)
                || newEndpoint.Address.Equals(IPAddress.Any)
                || newEndpoint.Address.Equals(IPAddress.IPv6Any);

            if (!ipOverlap) continue;

            bool tcpConflict = option.TCP && existing.TCP;
            bool udpConflict = option.UDP && existing.UDP;
            if (tcpConflict || udpConflict)
            {
                var protocols = string.Join("/",
                    new[] { tcpConflict ? "TCP" : null, udpConflict ? "UDP" : null }
                        .Where(p => p is not null));
                throw new InvalidOperationException(
                    $"Relay '{existing.Name}' already listens on {option.ListenHost} for {protocols}");
            }
        }
    }
}

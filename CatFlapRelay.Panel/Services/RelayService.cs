using System.Net;
using CatFlapRelay;
using CatFlapRelay.Panel.Models;
using Microsoft.Extensions.Options;

namespace CatFlapRelay.Panel.Services;

/// <summary>
/// Orchestration service that coordinates RelayConfigService (persistence)
/// and CatFlapRelayManager (runtime). Blazor components should inject this
/// as their single entry point for all relay operations.
/// </summary>
public sealed class RelayService
{
    private readonly RelayConfigService _config;
    private readonly FlapRelayManager _manager;
    private readonly ILogger<RelayService> _logger;
    private readonly PanelSettings _panelSettings;

    /// <summary>Fired on the calling thread after any relay list mutation (add, remove, update, replace-all).</summary>
    public event Action? RelaysChanged;

    public RelayService(RelayConfigService config, FlapRelayManager manager, ILogger<RelayService> logger, IOptions<PanelSettings> panelSettings)
    {
        _config = config;
        _manager = manager;
        _logger = logger;
        _panelSettings = panelSettings.Value;
    }

    /// <summary>Returns all configured relays (source of truth is the config file).</summary>
    public Task<IReadOnlyList<FlapRelayOption>> GetAllOptions() => _config.GetAllAsync();

    /// <summary>
    /// Returns a snapshot of all currently running relay instances.
    /// Hold the instance across renders to read fresh <see cref="CatFlapRelay.Statistics"/>
    /// on each tick without re-fetching. Re-fetch the list only after Add/Remove/Update.
    /// </summary>
    public IReadOnlyList<FlapRelay> GetAllRelays() => _manager.GetAllRelays();

    /// <summary>UI pre-validation hint. Uniqueness is still enforced atomically inside AddAsync/UpdateAsync.</summary>
    public Task<bool> ExistsAsync(string name) => _config.ExistsAsync(name);

    /// <summary>Returns runtime statistics for a relay, or null if it is not currently running.</summary>
    public FlapRelayStatistics? GetStatistics(Guid id)
        => _manager.TryGetRelay(id, out var relay) ? relay!.Statistics : null;

    /// <summary>
    /// Adds and starts a new relay.
    /// Order: validate → persist → start runtime.
    /// If runtime start fails, the config write is rolled back.
    /// </summary>
    public async Task AddRelayAsync(FlapRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        FlapRelayFactory.ValidateOption(option);

        var existing = await _config.GetAllAsync();
        if (existing.Count >= _panelSettings.MaxRelays)
            throw new InvalidOperationException(
                $"Maximum relay count ({_panelSettings.MaxRelays}) reached. Remove an existing relay or increase PanelSettings:MaxRelays.");

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
        RelaysChanged?.Invoke();
    }

    /// <summary>
    /// Stops and removes a relay.
    /// Order: stop runtime → remove from config.
    /// </summary>
    public async Task RemoveRelayAsync(Guid id)
    {
        await _manager.RemoveRelayAsync(id);
        await _config.RemoveAsync(id);
        RelaysChanged?.Invoke();
    }

    /// <summary>
    /// Stops the old relay, updates its config, then restarts with the new option.
    /// If the restart fails, the config is already updated; the user can retry.
    /// </summary>
    public async Task UpdateRelayAsync(Guid id, FlapRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        FlapRelayFactory.ValidateOption(option);

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
        RelaysChanged?.Invoke();
    }

    /// <summary>
    /// Stops all running relays
    /// Relays that fail to start are logged but do not abort the rest.
    /// </summary>
    public async Task ReplaceAllAsync(IEnumerable<FlapRelayOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var list = options.ToList();
        foreach (var o in list)
            FlapRelayFactory.ValidateOption(o);

        await _manager.StopAllAsync();
        await _manager.ClearAsync();
        await _config.ReplaceAllAsync(list);

        foreach (var o in list)
        {
            try { _manager.AddRelay(o); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart relay '{Name}' after config replace", o.Name);
            }
        }
        RelaysChanged?.Invoke();
    }

    /// <summary>Starts all relays that are not currently running.</summary>
    public void StartAll()
    {
        _manager.StartAll();
        RelaysChanged?.Invoke();
    }

    /// <summary>Stops all running relays without removing them from config.</summary>
    public async Task StopAllAsync()
    {
        await _manager.StopAllAsync();
        RelaysChanged?.Invoke();
    }

    /// <summary>Returns a single configured relay option by id, or null if not found.</summary>
    public async Task<FlapRelayOption?> GetByIdAsync(Guid id)
    {
        var all = await _config.GetAllAsync();
        return all.FirstOrDefault(o => o.Id == id);
    }

    /// <summary>Starts a relay that is already registered in the manager but not currently running.</summary>
    public void StartRelay(Guid id)
    {
        _manager.StartRelay(id);
        RelaysChanged?.Invoke();
    }

    /// <summary>Stops a relay without removing it from the manager or config.</summary>
    public async Task StopRelayAsync(Guid id)
    {
        await _manager.StopRelayAsync(id);
        RelaysChanged?.Invoke();
    }

    /// <summary>Stops and removes all relays from both runtime and config.</summary>
    public async Task ClearAllAsync()
    {
        await _manager.ClearAsync();
        await _config.ReplaceAllAsync([]);
        RelaysChanged?.Invoke();
    }

    private async Task CheckPortConflictAsync(FlapRelayOption option, Guid? excludeId)
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

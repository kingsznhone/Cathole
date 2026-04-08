using System.Text.Json;
using CatHole.Core;

namespace CatHole.Panel.Services;

/// <summary>
/// Singleton service for persisting relay configurations to data/relays.json.
/// Maintains an in-memory cache as the source of truth; every mutation is
/// atomically flushed to disk via a .tmp → rename pattern.
/// </summary>
public sealed class RelayConfigService
{
    private readonly string _configPath;
    private readonly ILogger<RelayConfigService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<CatHoleRelayOption>? _cache;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public RelayConfigService(IConfiguration configuration, ILogger<RelayConfigService> logger)
    {
        _logger = logger;
        var dataPath = configuration["DataPath"] ?? "data";
        _configPath = Path.Combine(dataPath, "relays.json");
    }

    public async Task<IReadOnlyList<CatHoleRelayOption>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return _cache!.AsReadOnly();
        }
        finally { _lock.Release(); }
    }

    public async Task AddAsync(CatHoleRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            if (_cache!.Any(r => r.Name == option.Name))
                throw new InvalidOperationException($"Relay '{option.Name}' already exists.");
            _cache.Add(option);
            await PersistAsync();
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            if (_cache!.RemoveAll(r => r.Id == id) > 0)
                await PersistAsync();
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Returns whether a relay with the given name exists.
    /// Intended for UI pre-validation only — uniqueness is still enforced
    /// atomically inside <see cref="AddAsync"/> and <see cref="UpdateAsync"/>.
    /// </summary>
    public async Task<bool> ExistsAsync(string name)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return _cache!.Any(r => r.Name == name);
        }
        finally { _lock.Release(); }
    }

    /// <param name="id">The id used to locate the entry.</param>
    /// <param name="option">Updated option; Name may differ from the existing entry for renames.</param>
    public async Task UpdateAsync(Guid id, CatHoleRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            var idx = _cache!.FindIndex(r => r.Id == id);
            if (idx < 0)
                throw new KeyNotFoundException($"Relay [{id}] not found.");
            if (_cache[idx].Name != option.Name && _cache.Any(r => r.Name == option.Name))
                throw new InvalidOperationException($"Relay '{option.Name}' already exists.");
            _cache[idx] = option;
            await PersistAsync();
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Atomically replaces the entire relay list with <paramref name="options"/> and persists.
    /// </summary>
    public async Task ReplaceAllAsync(IEnumerable<CatHoleRelayOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _lock.WaitAsync();
        try
        {
            _cache = [.. options];
            await PersistAsync();
        }
        finally { _lock.Release(); }
    }

    // Must be called while holding _lock.
    private async Task EnsureLoadedAsync()
    {
        if (_cache is not null) return;

        if (!File.Exists(_configPath))
        {
            _logger.LogInformation("No relay config found at {Path}, starting with empty list", _configPath);
            _cache = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            _cache = JsonSerializer.Deserialize<List<CatHoleRelayOption>>(json, _jsonOptions) ?? [];
            _logger.LogInformation("Loaded {Count} relay configs from {Path}", _cache.Count, _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load relay config from {Path}, starting with empty list", _configPath);
            _cache = [];
        }
    }

    // Must be called while holding _lock.
    private async Task PersistAsync()
    {
        var tmp = _configPath + ".tmp";
        var json = JsonSerializer.Serialize(_cache, _jsonOptions);
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, _configPath, overwrite: true);
        _logger.LogDebug("Persisted {Count} relay configs to {Path}", _cache!.Count, _configPath);
    }
}

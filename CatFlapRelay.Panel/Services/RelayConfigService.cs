using CatFlapRelay;
using CatFlapRelay.Panel.Data;
using Microsoft.EntityFrameworkCore;

namespace CatFlapRelay.Panel.Services;

/// <summary>
/// Singleton service for persisting relay configurations to the application database.
/// Each operation creates its own <see cref="ApplicationDbContext"/> via the factory,
/// which is safe for use from a singleton lifetime.
/// </summary>
public sealed class RelayConfigService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<RelayConfigService> _logger;

    public RelayConfigService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<RelayConfigService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FlapRelayOption>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entries = await db.Relays.AsNoTracking().ToListAsync();
        return entries.ConvertAll(ToOption).AsReadOnly();
    }

    public async Task AddAsync(FlapRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.Relays.AnyAsync(r => r.Name == option.Name))
            throw new InvalidOperationException($"Relay '{option.Name}' already exists.");
        db.Relays.Add(ToEntry(option));
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entry = await db.Relays.FindAsync(id);
        if (entry is not null)
        {
            db.Relays.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Returns whether a relay with the given name exists.
    /// Intended for UI pre-validation only — uniqueness is still enforced
    /// atomically inside <see cref="AddAsync"/> and <see cref="UpdateAsync"/>.
    /// </summary>
    public async Task<bool> ExistsAsync(string name)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Relays.AnyAsync(r => r.Name == name);
    }

    /// <param name="id">The id used to locate the entry.</param>
    /// <param name="option">Updated option; Name may differ from the existing entry for renames.</param>
    public async Task UpdateAsync(Guid id, FlapRelayOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entry = await db.Relays.FindAsync(id)
            ?? throw new KeyNotFoundException($"Relay [{id}] not found.");
        if (entry.Name != option.Name && await db.Relays.AnyAsync(r => r.Name == option.Name))
            throw new InvalidOperationException($"Relay '{option.Name}' already exists.");
        UpdateEntry(entry, option);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Atomically replaces the entire relay list with <paramref name="options"/> and persists.
    /// </summary>
    public async Task ReplaceAllAsync(IEnumerable<FlapRelayOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var list = options.ToList();
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Relays.ExecuteDeleteAsync();
        db.Relays.AddRange(list.ConvertAll(ToEntry));
        await db.SaveChangesAsync();
    }

    private static FlapRelayOption ToOption(RelayEntry e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        ListenHost = e.ListenHost,
        TargetHost = e.TargetHost,
        BufferSize = e.BufferSize,
        TCP = e.Tcp,
        UDP = e.Udp,
        SocketTimeout =e.SocketTimeout,
        UdpTunnelTimeout =e.UdpTunnelTimeout,
        DualMode = e.DualMode,
    };

    private static RelayEntry ToEntry(FlapRelayOption o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        ListenHost = o.ListenHost,
        TargetHost = o.TargetHost,
        BufferSize = o.BufferSize,
        Tcp = o.TCP,
        Udp = o.UDP,
        SocketTimeout = o.SocketTimeout,
        UdpTunnelTimeout = o.UdpTunnelTimeout,
        DualMode = o.DualMode,
    };

    private static void UpdateEntry(RelayEntry entry, FlapRelayOption o)
    {
        entry.Name = o.Name;
        entry.ListenHost = o.ListenHost;
        entry.TargetHost = o.TargetHost;
        entry.BufferSize = o.BufferSize;
        entry.Tcp = o.TCP;
        entry.Udp = o.UDP;
        entry.SocketTimeout = o.SocketTimeout;
        entry.UdpTunnelTimeout = o.UdpTunnelTimeout;
        entry.DualMode = o.DualMode;
    }
}

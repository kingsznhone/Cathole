using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CatHole.Core
{
    /// <summary>
    /// Core manager for managing multiple relay instances.
    /// Framework-agnostic and can be used in any .NET application.
    /// </summary>
    public class CatHoleRelayManager : IDisposable, IAsyncDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<CatHoleRelayManager> _logger;
        private readonly ConcurrentDictionary<Guid, CatHoleRelay> _relays = new();
        private readonly Lock _managementLock = new();
        private bool _disposed;

        public CatHoleRelayManager(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<CatHoleRelayManager>();
        }

        /// <summary>
        /// Gets the total number of managed relays
        /// </summary>
        public int Count => _relays.Count;

        /// <summary>
        /// Adds and starts a new relay
        /// </summary>
        public bool AddRelay(CatHoleRelayOption option)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));

            if (string.IsNullOrWhiteSpace(option.Name))
                throw new ArgumentException("Relay name cannot be empty", nameof(option));

            lock (_managementLock)
            {
                if (_relays.ContainsKey(option.Id))
                {
                    _logger.LogWarning("Relay '{Name}' [{Id}] already exists", option.Name, option.Id);
                    return false;
                }

                try
                {
                    var relayLogger = _loggerFactory.CreateLogger<CatHoleRelay>();
                    var relay = new CatHoleRelay(option, relayLogger);

                    if (_relays.TryAdd(option.Id, relay))
                    {
                        relay.Start();
                        _logger.LogInformation("Added and started relay '{Name}' [{Id}]: {ListenHost} -> {TargetHost}",
                            option.Name, option.Id, option.ListenHost, option.TargetHost);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add relay '{Name}' [{Id}]", option.Name, option.Id);
                    throw;
                }

                return false;
            }
        }

        /// <summary>
        /// Adds multiple relays
        /// </summary>
        public int AddRelays(IEnumerable<CatHoleRelayOption> options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var count = 0;
            foreach (var option in options)
            {
                if (AddRelay(option))
                    count++;
            }

            _logger.LogInformation("Added {Count} relays successfully", count);
            return count;
        }

        /// <summary>
        /// Removes and stops a relay by id
        /// </summary>
        public async Task<bool> RemoveRelayAsync(Guid id)
        {
            if (_relays.TryRemove(id, out var relay))
            {
                _logger.LogInformation("Removing relay '{Name}' [{Id}]", relay.Option.Name, id);
                await relay.DisposeAsync();
                _logger.LogInformation("Removed relay '{Name}' [{Id}]", relay.Option.Name, id);
                return true;
            }

            _logger.LogWarning("Relay [{Id}] not found", id);
            return false;
        }

        /// <summary>
        /// Removes a relay synchronously
        /// </summary>
        public bool RemoveRelay(Guid id)
        {
            return RemoveRelayAsync(id).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Starts a relay by id without removing it. Returns false if the relay is not found.
        /// </summary>
        public bool StartRelay(Guid id)
        {
            if (!_relays.TryGetValue(id, out var relay))
            {
                _logger.LogWarning("Relay [{Id}] not found", id);
                return false;
            }

            relay.Start();
            return true;
        }

        /// <summary>
        /// Stops a relay by id without removing or disposing it. Returns false if the relay is not found.
        /// </summary>
        public async Task<bool> StopRelayAsync(Guid id)
        {
            if (!_relays.TryGetValue(id, out var relay))
            {
                _logger.LogWarning("Relay [{Id}] not found", id);
                return false;
            }

            await relay.StopAsync();
            return true;
        }

        /// <summary>
        /// Gets a relay by id
        /// </summary>
        public bool TryGetRelay(Guid id, out CatHoleRelay? relay)
        {
            return _relays.TryGetValue(id, out relay);
        }

        /// <summary>
        /// Returns a point-in-time snapshot of all managed relay instances.
        /// Callers may read <see cref="CatHoleRelay.Statistics"/> and
        /// <see cref="CatHoleRelay.Option"/> on each instance freely;
        /// the snapshot list itself will not reflect subsequent Add/Remove operations.
        /// </summary>
        public IReadOnlyList<CatHoleRelay> GetAllRelays() => [.. _relays.Values];

        /// <summary>
        /// Starts all relays
        /// </summary>
        public void StartAll()
        {
            _logger.LogInformation("Starting all {Count} relays", _relays.Count);
            
            foreach (var relay in _relays.Values)
            {
                relay.Start();
            }
        }

        /// <summary>
        /// Stops all relays asynchronously
        /// </summary>
        public async Task StopAllAsync()
        {
            _logger.LogInformation("Stopping all {Count} relays", _relays.Count);

            var stopTasks = _relays.Values.Select(relay => relay.StopAsync()).ToList();
            await Task.WhenAll(stopTasks);

            _logger.LogInformation("All relays stopped successfully");
        }

        /// <summary>
        /// Stops all relays synchronously
        /// </summary>
        public void StopAll()
        {
            StopAllAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Removes all relays
        /// </summary>
        public async Task ClearAsync()
        {
            _logger.LogInformation("Clearing all relays");

            await Task.WhenAll(_relays.Values.Select(r => r.DisposeAsync().AsTask()));
            _relays.Clear();

            _logger.LogInformation("All relays cleared");
        }

        /// <summary>
        /// Checks if a relay exists
        /// </summary>
        public bool Contains(Guid id)
        {
            return _relays.ContainsKey(id);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var relay in _relays.Values)
                        relay.Dispose();
                    _relays.Clear();
                }
                _disposed = true;
            }
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (!_disposed)
            {
                await ClearAsync();
                _disposed = true;
            }
        }
    }
}

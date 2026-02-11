using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CatHole.Core
{
    /// <summary>
    /// Core manager for managing multiple relay instances.
    /// Framework-agnostic and can be used in any .NET application.
    /// </summary>
    public class RelayManager : IDisposable, IAsyncDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<RelayManager> _logger;
        private readonly ConcurrentDictionary<string, Relay> _relays = new();
        private readonly Lock _managementLock = new();
        private bool _disposed;

        public RelayManager(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<RelayManager>();
        }

        /// <summary>
        /// Gets the total number of managed relays
        /// </summary>
        public int Count => _relays.Count;

        /// <summary>
        /// Gets all relay names
        /// </summary>
        public IEnumerable<string> RelayNames => _relays.Keys;

        /// <summary>
        /// Adds and starts a new relay
        /// </summary>
        public bool AddRelay(RelayOption option)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));

            if (string.IsNullOrWhiteSpace(option.Name))
                throw new ArgumentException("Relay name cannot be empty", nameof(option));

            lock (_managementLock)
            {
                if (_relays.ContainsKey(option.Name))
                {
                    _logger.LogWarning("Relay with name '{Name}' already exists", option.Name);
                    return false;
                }

                try
                {
                    var relayLogger = _loggerFactory.CreateLogger<Relay>();
                    var relay = new Relay(option, relayLogger);
                    
                    if (_relays.TryAdd(option.Name, relay))
                    {
                        relay.Start();
                        _logger.LogInformation("Added and started relay '{Name}': {ListenHost} -> {TargetHost}",
                            option.Name, option.ListenHost, option.TargetHost);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add relay '{Name}'", option.Name);
                    throw;
                }

                return false;
            }
        }

        /// <summary>
        /// Adds multiple relays
        /// </summary>
        public int AddRelays(IEnumerable<RelayOption> options)
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
        /// Removes and stops a relay by name
        /// </summary>
        public async Task<bool> RemoveRelayAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Relay name cannot be empty", nameof(name));

            if (_relays.TryRemove(name, out var relay))
            {
                _logger.LogInformation("Removing relay '{Name}'", name);
                await relay.StopAsync();
                _logger.LogInformation("Removed relay '{Name}'", name);
                return true;
            }

            _logger.LogWarning("Relay '{Name}' not found", name);
            return false;
        }

        /// <summary>
        /// Removes a relay synchronously
        /// </summary>
        public bool RemoveRelay(string name)
        {
            return RemoveRelayAsync(name).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets a relay by name
        /// </summary>
        public bool TryGetRelay(string name, out Relay? relay)
        {
            return _relays.TryGetValue(name, out relay);
        }

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

            await StopAllAsync();
            _relays.Clear();

            _logger.LogInformation("All relays cleared");
        }

        /// <summary>
        /// Checks if a relay exists
        /// </summary>
        public bool Contains(string name)
        {
            return _relays.ContainsKey(name);
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
                    StopAll();
                    _relays.Clear();
                }
                _disposed = true;
            }
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (!_disposed)
            {
                await StopAllAsync();
                _relays.Clear();
                _disposed = true;
            }
        }
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatHole.Core
{
    /// <summary>
    /// Core manager for managing multiple relay instances.
    /// Framework-agnostic and can be used in any .NET application.
    /// </summary>
    public class CatHoleRelayManager : IAsyncDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<CatHoleRelayManager> _logger;
        private readonly ConcurrentDictionary<Guid, CatHoleRelay> _relays = new();
        private readonly Lock _managementLock = new();
        private int _disposed;

        /// <summary>
        /// Initializes a new instance with no logging output.
        /// </summary>
        public CatHoleRelayManager() : this(NullLoggerFactory.Instance) { }

        public CatHoleRelayManager(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<CatHoleRelayManager>();
        }

        /// <summary>
        /// Gets the total number of managed relays
        /// </summary>
        public int Count => _relays.Count;

        /// <summary>
        /// Adds and starts a new relay.
        /// </summary>
        /// <exception cref="RelayAlreadyExistsException">A relay with the same Id already exists.</exception>
        public void AddRelay(CatHoleRelayOption option)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            ArgumentNullException.ThrowIfNull(option);
            CatHoleRelayFactory.ValidateOption(option);

            CatHoleRelay relay;
            lock (_managementLock)
            {
                if (_relays.ContainsKey(option.Id))
                    throw new RelayAlreadyExistsException(option.Name, option.Id);

                var relayLogger = _loggerFactory.CreateLogger<CatHoleRelay>();
                relay = new CatHoleRelay(option, relayLogger);
                _relays[option.Id] = relay;
            }

            // Start outside the lock: socket bind can be slow and should not block other management operations
            try
            {
                relay.Start();
            }
            catch (Exception ex)
            {
                _relays.TryRemove(option.Id, out _);
                _logger.LogError(ex, "Failed to start relay '{Name}' [{Id}]", option.Name, option.Id);
                throw;
            }
        }

        /// <summary>
        /// Adds multiple relays. Throws on the first failure.
        /// </summary>
        /// <returns>Number of relays successfully added.</returns>
        public int AddRelays(IEnumerable<CatHoleRelayOption> options)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            ArgumentNullException.ThrowIfNull(options);

            var count = 0;
            foreach (var option in options)
            {
                AddRelay(option);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Removes and stops a relay by id.
        /// </summary>
        /// <exception cref="RelayNotFoundException">No relay with the given Id exists.</exception>
        public async Task RemoveRelayAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            if (!_relays.TryRemove(id, out var relay))
            {
                _logger.LogWarning("Relay [{Id}] not found", id);
                throw new RelayNotFoundException(id);
            }

            await relay.StopAsync(cancellationToken).ConfigureAwait(false);
            await relay.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a relay by id without removing it.
        /// </summary>
        /// <exception cref="RelayNotFoundException">No relay with the given Id exists.</exception>
        public void StartRelay(Guid id)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            if (!_relays.TryGetValue(id, out var relay))
            {
                _logger.LogWarning("Relay [{Id}] not found", id);
                throw new RelayNotFoundException(id);
            }

            relay.Start();
        }

        /// <summary>
        /// Stops a relay by id without removing or disposing it.
        /// </summary>
        /// <exception cref="RelayNotFoundException">No relay with the given Id exists.</exception>
        public async Task StopRelayAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            if (!_relays.TryGetValue(id, out var relay))
            {
                _logger.LogWarning("Relay [{Id}] not found", id);
                throw new RelayNotFoundException(id);
            }

            await relay.StopAsync(cancellationToken).ConfigureAwait(false);
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
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            _logger.LogInformation("Starting all {Count} relays", _relays.Count);

            foreach (var relay in _relays.Values)
            {
                relay.Start();
            }
        }

        /// <summary>
        /// Stops all relays asynchronously
        /// </summary>
        public async Task StopAllAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            _logger.LogInformation("Stopping all {Count} relays", _relays.Count);

            var stopTasks = _relays.Values.Select(relay => relay.StopAsync(cancellationToken)).ToList();
            await Task.WhenAll(stopTasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes all relays
        /// </summary>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            await StopAllAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(_relays.Values.Select(r => r.DisposeAsync().AsTask())).ConfigureAwait(false);
            _relays.Clear();
        }

        /// <summary>
        /// Checks if a relay exists
        /// </summary>
        public bool Contains(Guid id)
        {
            return _relays.ContainsKey(id);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await ClearAsync().ConfigureAwait(false);
            }
        }
    }
}

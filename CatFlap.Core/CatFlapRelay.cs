using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace CatFlap.Core
{
    public class CatFlapRelay : IAsyncDisposable
    {
        private readonly CatFlapRelayOption _option;
        private readonly ILogger<CatFlapRelay> _logger;
        private readonly IPEndPoint _listenEndpoint;
        private readonly IPEndPoint _targetEndpoint;
        private CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<IPEndPoint, UdpTunnelInfo> _udpMap = new();
        private readonly List<Task> _activeTasks = new();
        private readonly Lock _activeTasksLock = new();
        private readonly Lock _stateLock = new();
        private readonly Stopwatch _uptimeStopwatch = new();
        private TimeSpan _cumulativeUptime = TimeSpan.Zero;
        private int _isRunning = 0;
        private int _disposed;
        private DateTime? _startTime;
        private int _tcpActiveConnections = 0;
        private long _tcpTotalConnections = 0;
        private long _tcpBytesSent = 0;
        private long _tcpBytesReceived = 0;
        private long _udpBytesSent = 0;
        private long _udpBytesReceived = 0;
        private long _connectionErrors = 0;
        private TcpListener? _tcpListener;
        private UdpClient? _udpListener;
        private Task? _tcpForwardingTask;
        private Task? _udpForwardingTask;
        private Task _stopTask = Task.CompletedTask;

        private static readonly TimeSpan s_disposeTimeout = TimeSpan.FromSeconds(30);

        public CatFlapRelay(CatFlapRelayOption option, ILogger<CatFlapRelay> logger)
        {
            ArgumentNullException.ThrowIfNull(option);
            ArgumentNullException.ThrowIfNull(logger);

            _option = option;
            _logger = logger;
            _listenEndpoint = IPEndPoint.Parse(_option.ListenHost);
            _targetEndpoint = IPEndPoint.Parse(_option.TargetHost);
        }

        public CatFlapRelayOption Option => _option;

        public TimeSpan Uptime => _cumulativeUptime + (_uptimeStopwatch.IsRunning ? _uptimeStopwatch.Elapsed : TimeSpan.Zero);

        public CatFlapRelayStatistics Statistics => new()
        {
            IsRunning = _isRunning == 1,
            StartTime = _startTime,
            Uptime = Uptime,
            TcpActiveConnections = _tcpActiveConnections,
            TcpTotalConnections = _tcpTotalConnections,
            UdpActiveTunnels = _udpMap.Count,
            TcpBytesSent = _tcpBytesSent,
            TcpBytesReceived = _tcpBytesReceived,
            UdpBytesSent = _udpBytesSent,
            UdpBytesReceived = _udpBytesReceived,
            ConnectionErrors = _connectionErrors
        };

        public void ResetUptime()
        {
            lock (_stateLock)
            {
                _cumulativeUptime = TimeSpan.Zero;
                if (_uptimeStopwatch.IsRunning)
                {
                    _uptimeStopwatch.Restart();
                }
                else
                {
                    _uptimeStopwatch.Reset();
                }
                _logger.LogDebug("Uptime reset for relay [{Name}]", _option.Name);
            }
        }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            lock (_stateLock)
            {
                if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
                {
                    _logger.LogWarning("Relay [{Name}] is already running", _option.Name);
                    return;
                }

                if (_cts.IsCancellationRequested)
                {
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();
                }

                var protocols = (_option.TCP, _option.UDP) switch
                {
                    (true, true) => "TCP+UDP",
                    (true, false) => "TCP",
                    _ => "UDP"
                };
                _logger.LogInformation("Relay [{Name}] starting: {ListenHost} -> {TargetHost} [{Protocols}]",
                    _option.Name, _option.ListenHost, _option.TargetHost, protocols);

                try
                {
                    if (_option.TCP)
                    {
                        _tcpListener = new TcpListener(_listenEndpoint.Address, _listenEndpoint.Port);
                        _tcpListener.Start();
                    }
                    if (_option.UDP)
                    {
                        _udpListener = new UdpClient(_listenEndpoint);
                    }
                }
                catch
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                    try { _tcpListener?.Stop(); } catch { }
                    _tcpListener = null;
                    try { _udpListener?.Close(); } catch { }
                    _udpListener = null;
                    throw;
                }

                _startTime = DateTime.UtcNow;
                _uptimeStopwatch.Restart();

                if (_option.TCP)
                {
                    _tcpForwardingTask = Task.Run(() => StartTCPForwarding(_cts.Token), _cts.Token);
                }
                if (_option.UDP)
                {
                    _udpForwardingTask = Task.Run(() => StartUDPForwarding(_cts.Token), _cts.Token);
                }
            }
        }

        /// <summary>
        /// Stops the relay gracefully. Concurrent callers all await the same underlying stop operation.
        /// The <paramref name="cancellationToken"/> controls how long each caller is willing to wait;
        /// the actual cleanup continues in the background regardless.
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 1)
                {
                    // We won the race — initiate stop
                    _logger.LogInformation("Stopping relay [{Name}] {ListenHost} -> {TargetHost}",
                        _option.Name, _option.ListenHost, _option.TargetHost);

                    if (_uptimeStopwatch.IsRunning)
                    {
                        _uptimeStopwatch.Stop();
                        _cumulativeUptime += _uptimeStopwatch.Elapsed;
                    }

                    _cts.Cancel();

                    _tcpListener?.Stop();
                    _udpListener?.Close();

                    _stopTask = StopCoreAsync();
                }
                else if (_stopTask.IsCompleted)
                {
                    // Not running and no stop in progress
                    _logger.LogWarning("Relay [{Name}] is not running", _option.Name);
                    return;
                }
                // else: stop already in progress — fall through to await
            }

            await _stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
                return;

            using var cts = new CancellationTokenSource(s_disposeTimeout);
            try
            {
                await StopAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Relay [{Name}] stop timed out during dispose", _option.Name);
            }

            _cts.Dispose();
        }

        /// <summary>
        /// Performs the actual async cleanup after cancellation has been signaled and listeners stopped.
        /// </summary>
        private async Task StopCoreAsync()
        {
            // Wait for main forwarding tasks
            var tasksToWait = new List<Task>(2);
            if (_tcpForwardingTask != null) tasksToWait.Add(_tcpForwardingTask);
            if (_udpForwardingTask != null) tasksToWait.Add(_udpForwardingTask);

            if (tasksToWait.Count > 0)
            {
                try
                {
                    await Task.WhenAll(tasksToWait).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Exception while waiting for forwarding tasks to complete");
                }
            }

            // Wait for all active client tasks
            Task[] activeTaskSnapshot;
            lock (_activeTasksLock)
            {
                activeTaskSnapshot = [.. _activeTasks];
            }

            if (activeTaskSnapshot.Length > 0)
            {
                _logger.LogDebug("Waiting for {Count} active client tasks to complete", activeTaskSnapshot.Length);
                try
                {
                    await Task.WhenAll(activeTaskSnapshot).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Exception while waiting for active tasks to complete");
                }
            }

            // Clean up UDP tunnels
            foreach (var tunnelInfo in _udpMap.Values)
            {
                tunnelInfo.Client.Close();
            }
            _udpMap.Clear();

            // Null-clear references for safe restart
            _tcpForwardingTask = null;
            _udpForwardingTask = null;
            _tcpListener = null;
            _udpListener = null;
            lock (_activeTasksLock)
            {
                _activeTasks.Clear();
            }

            _logger.LogInformation("Relay [{Name}] stopped successfully. Total uptime: {Uptime}",
                _option.Name, Uptime.ToString(@"dd\.hh\:mm\:ss"));
        }

        private async Task StartTCPForwarding(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = await _tcpListener!.AcceptTcpClientAsync(ct);
                    _logger.LogDebug("Accepted new TCP client from {RemoteEndPoint}", client.Client.RemoteEndPoint);

                    Interlocked.Increment(ref _tcpActiveConnections);
                    Interlocked.Increment(ref _tcpTotalConnections);

                    var clientTask = Task.Run(() => HandleTCPClient(client, ct), CancellationToken.None);
                    TrackActiveTask(clientTask);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Relay [{Name}] TCP forwarding stopped", _option.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Relay [{Name}] TCP listener failed unexpectedly", _option.Name);
            }
            finally
            {
                _tcpListener?.Stop();
            }
        }

        private async Task HandleTCPClient(TcpClient client, CancellationToken ct)
        {
            var remoteEndPoint = client.Client.RemoteEndPoint;
            TcpClient? targetClient = null;

            try
            {
                using (client)
                {
                    targetClient = new TcpClient();
                    using (targetClient)
                    {
                        try
                        {
                            await targetClient.ConnectAsync(_targetEndpoint.Address, _targetEndpoint.Port, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Relay [{Name}] failed to connect to target {TargetEndpoint}",
                                _option.Name, _targetEndpoint);
                            Interlocked.Increment(ref _connectionErrors);
                            return;
                        }

                        var socketTimeoutMs = (int)_option.SocketTimeout.TotalMilliseconds;
                        targetClient.ReceiveTimeout = socketTimeoutMs;
                        targetClient.SendTimeout = socketTimeoutMs;
                        client.ReceiveTimeout = socketTimeoutMs;
                        client.SendTimeout = socketTimeoutMs;

                        var clientToTarget = CopyStreamWithShutdownAsync(client, targetClient, true, ct);
                        var targetToClient = CopyStreamWithShutdownAsync(targetClient, client, false, ct);

                        await Task.WhenAll(clientToTarget, targetToClient);

                        _logger.LogDebug("TCP client disconnected: {RemoteEndPoint}", remoteEndPoint);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("TCP client connection cancelled: {RemoteEndPoint}", remoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Relay [{Name}] error on TCP client {RemoteEndPoint}", _option.Name, remoteEndPoint);
            }
            finally
            {
                Interlocked.Decrement(ref _tcpActiveConnections);
            }
        }

        private async Task CopyStreamWithShutdownAsync(
            TcpClient sourceClient,
            TcpClient targetClient,
            bool isClientToTarget,
            CancellationToken ct)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(_option.BufferSize);
            try
            {
                var sourceStream = sourceClient.GetStream();
                var targetStream = targetClient.GetStream();

                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, _option.BufferSize), ct)) > 0)
                {
                    await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

                    if (isClientToTarget)
                        Interlocked.Add(ref _tcpBytesSent, bytesRead);
                    else
                        Interlocked.Add(ref _tcpBytesReceived, bytesRead);
                }

                // Graceful shutdown: close write side
                try
                {
                    targetClient.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error during shutdown");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error copying stream");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task StartUDPForwarding(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var initDataBlock = await _udpListener!.ReceiveAsync(ct);
                    var clientEndpoint = initDataBlock.RemoteEndPoint;

                    if (!_udpMap.TryGetValue(clientEndpoint, out var tunnelInfo))
                    {
                        var newTunnel = new UdpTunnelInfo
                        {
                            Client = new UdpClient(),
                            LastActivity = DateTime.UtcNow
                        };

                        if (_udpMap.TryAdd(clientEndpoint, newTunnel))
                        {
                            tunnelInfo = newTunnel;
                            _logger.LogDebug("Started new UDP tunnel for {ClientEndpoint}", clientEndpoint);
                            var tunnelTask = Task.Run(() => HandleUdpTunnel(clientEndpoint, _udpListener!, tunnelInfo, ct), CancellationToken.None);
                            TrackActiveTask(tunnelTask);
                        }
                        else
                        {
                            // Another thread added it first; close our duplicate and use theirs
                            newTunnel.Client.Close();
                            _udpMap.TryGetValue(clientEndpoint, out tunnelInfo);
                        }
                    }

                    if (tunnelInfo is null)
                        continue;

                    // Update last activity
                    tunnelInfo.LastActivity = DateTime.UtcNow;

                    await tunnelInfo.Client.SendAsync(initDataBlock.Buffer, _targetEndpoint, ct);
                    Interlocked.Add(ref _udpBytesSent, initDataBlock.Buffer.Length);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Relay [{Name}] UDP forwarding stopped", _option.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Relay [{Name}] UDP forwarding failed unexpectedly", _option.Name);
            }
        }

        private async Task HandleUdpTunnel(IPEndPoint callbackEndpoint, UdpClient udpListener, UdpTunnelInfo tunnelInfo, CancellationToken ct)
        {
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Check for timeout
                    var idleTime = DateTime.UtcNow - tunnelInfo.LastActivity;
                    if (idleTime > _option.UdpTunnelTimeout)
                    {
                        _logger.LogDebug("UDP tunnel timeout for {CallbackEndpoint} after {IdleSeconds}s idle",
                            callbackEndpoint, (int)idleTime.TotalSeconds);
                        break;
                    }

                    try
                    {
                        // Wait with timeout
                        timeoutCts.CancelAfter(_option.UdpTunnelTimeout);
                        var receivedData = await tunnelInfo.Client.ReceiveAsync(timeoutCts.Token);

                        tunnelInfo.LastActivity = DateTime.UtcNow;

                        Interlocked.Add(ref _udpBytesReceived, receivedData.Buffer.Length);
                        await udpListener.SendAsync(receivedData.Buffer, callbackEndpoint, ct);

                        // Reset timeout for next receive
                        timeoutCts.Dispose();
                        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Timeout on receive, check if tunnel should be closed
                        var currentIdleTime = DateTime.UtcNow - tunnelInfo.LastActivity;
                        if (currentIdleTime > _option.UdpTunnelTimeout)
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("UDP tunnel cancelled for {CallbackEndpoint}", callbackEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Relay [{Name}] error in UDP tunnel for {CallbackEndpoint}", _option.Name, callbackEndpoint);
            }
            finally
            {
                timeoutCts.Dispose();
                tunnelInfo.Client.Close();
                _udpMap.TryRemove(callbackEndpoint, out _);
                _logger.LogDebug("UDP tunnel closed for {CallbackEndpoint}", callbackEndpoint);
            }
        }

        /// <summary>
        /// Tracks a task and periodically purges completed tasks to prevent unbounded growth.
        /// </summary>
        private void TrackActiveTask(Task task)
        {
            lock (_activeTasksLock)
            {
                // Purge completed tasks periodically to prevent unbounded memory growth
                if (_activeTasks.Count >= 64)
                {
                    _activeTasks.RemoveAll(t => t.IsCompleted);
                }
                _activeTasks.Add(task);
            }
        }

        private class UdpTunnelInfo
        {
            public required UdpClient Client { get; init; }
            public DateTime LastActivity { get; set; }
        }
    }
}

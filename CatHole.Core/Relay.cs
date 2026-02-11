using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace CatHole.Core
{
    public class Relay
    {
        private readonly RelayOption _option;
        private readonly ILogger<Relay> _logger;
        private readonly IPEndPoint _listenEndpoint;
        private readonly IPEndPoint _targetEndpoint;
        private CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<IPEndPoint, UdpTunnelInfo> _udpMap = new();
        private readonly ConcurrentBag<Task> _activeTasks = new();
        private readonly Lock _stateLock = new();
        private int _isRunning = 0;
        private TcpListener? _tcpListener;
        private UdpClient? _udpListener;
        private Task? _tcpForwardingTask;
        private Task? _udpForwardingTask;
        private const int UDP_TUNNEL_TIMEOUT_SECONDS = 60;

        public Relay(RelayOption option, ILogger<Relay> logger)
        {
            _option = option;
            _logger = logger;
            _listenEndpoint = IPEndPoint.Parse(_option.ListenHost);
            _targetEndpoint = IPEndPoint.Parse(_option.TargetHost);
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
                {
                    _logger.LogWarning("Relay already running");
                    return;
                }

                if (_cts.IsCancellationRequested)
                {
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();
                }

                _logger.LogInformation("Starting relay [{Name}] {ListenHost} -> {TargetHost}",
                    _option.Name, _option.ListenHost, _option.TargetHost);

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

        public async Task StopAsync()
        {
            lock (_stateLock)
            {
                if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 0)
                {
                    _logger.LogWarning("Relay is not running");
                    return;
                }

                _logger.LogInformation("Stopping relay [{Name}] {ListenHost} -> {TargetHost}",
                    _option.Name, _option.ListenHost, _option.TargetHost);

                _cts.Cancel();

                _tcpListener?.Stop();
                _udpListener?.Close();
            }

            // Wait for main forwarding tasks
            var tasksToWait = new List<Task>();
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
            if (!_activeTasks.IsEmpty)
            {
                _logger.LogDebug("Waiting for {Count} active client tasks to complete", _activeTasks.Count);
                try
                {
                    await Task.WhenAll(_activeTasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Exception while waiting for active tasks to complete");
                }
            }

            // Clean up UDP tunnels
            foreach (var tunnelInfo in _udpMap.Values)
            {
                tunnelInfo.Client?.Close();
            }
            _udpMap.Clear();

            _logger.LogInformation("Relay [{Name}] stopped successfully", _option.Name);
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public async Task StartTCPForwarding(CancellationToken ct)
        {
            _tcpListener = new TcpListener(_listenEndpoint.Address, _listenEndpoint.Port);
            _tcpListener.Start();
            _logger.LogInformation("TCP forwarding from [{Name}] {ListenEndpoint} to {TargetEndpoint}",
                _option.Name, _listenEndpoint, _targetEndpoint);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync(ct);
                    _logger.LogDebug("Accepted new TCP client from {RemoteEndPoint}", client.Client.RemoteEndPoint);

                    var clientTask = Task.Run(() => HandleTCPClient(client, ct), ct);
                    _activeTasks.Add(clientTask);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TCP forwarding cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TCP listener");
            }
            finally
            {
                _tcpListener.Stop();
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
                            _logger.LogWarning(ex, "Failed to connect to target {TargetEndpoint} for client {RemoteEndPoint}",
                                _targetEndpoint, remoteEndPoint);
                            return;
                        }

                        targetClient.ReceiveTimeout = _option.Timeout;
                        targetClient.SendTimeout = _option.Timeout;
                        client.ReceiveTimeout = _option.Timeout;
                        client.SendTimeout = _option.Timeout;

                        using var clientStream = client.GetStream();
                        using var targetStream = targetClient.GetStream();

                        var clientToTarget = CopyStreamWithShutdownAsync(client, clientStream, targetClient, targetStream, ct);
                        var targetToClient = CopyStreamWithShutdownAsync(targetClient, targetStream, client, clientStream, ct);

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
                _logger.LogWarning(ex, "Error handling TCP client {RemoteEndPoint}", remoteEndPoint);
            }
        }

        private async Task CopyStreamWithShutdownAsync(
            TcpClient client,
            NetworkStream clientStream,
            TcpClient targetClient,
            NetworkStream targetStream,
            CancellationToken ct)
        {
            try
            {
                byte[] buffer = new byte[_option.BufferSize];
                int bytesRead;
                long totalBytes = 0;

                while ((bytesRead = await clientStream.ReadAsync(buffer, ct)) > 0)
                {
                    await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalBytes += bytesRead;
                }

                // Graceful shutdown: close write side
                try
                {
                    targetClient.Client.Shutdown(SocketShutdown.Send);
                    _logger.LogDebug("Stream copy completed and shutdown sent, total bytes: {TotalBytes}", totalBytes);
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
        }

        public async Task StartUDPForwarding(CancellationToken ct)
        {
            _udpListener = new UdpClient(_listenEndpoint);
            _logger.LogInformation("UDP forwarding from {ListenEndpoint} to {TargetEndpoint}",
                _listenEndpoint, _targetEndpoint);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var initDataBlock = await _udpListener.ReceiveAsync(ct);
                    _logger.LogDebug("Received UDP packet: {Length} bytes from {RemoteEndPoint}",
                        initDataBlock.Buffer.Length, initDataBlock.RemoteEndPoint);

                    var clientEndpoint = initDataBlock.RemoteEndPoint;
                    var isNew = false;
                    var tunnelInfo = _udpMap.GetOrAdd(clientEndpoint, _ =>
                    {
                        isNew = true;
                        var info = new UdpTunnelInfo
                        {
                            Client = new UdpClient(),
                            LastActivity = DateTime.UtcNow
                        };
                        return info;
                    });

                    // Update last activity
                    tunnelInfo.LastActivity = DateTime.UtcNow;

                    if (isNew)
                    {
                        _logger.LogDebug("Started new UDP tunnel for {ClientEndpoint}", clientEndpoint);
                        var tunnelTask = Task.Run(() => HandleUdpTunnel(clientEndpoint, _udpListener, tunnelInfo, ct), ct);
                        _activeTasks.Add(tunnelTask);
                    }

                    await tunnelInfo.Client.SendAsync(initDataBlock.Buffer, _targetEndpoint, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("UDP forwarding cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UDP forwarding");
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
                    if (idleTime.TotalSeconds > UDP_TUNNEL_TIMEOUT_SECONDS)
                    {
                        _logger.LogDebug("UDP tunnel timeout for {CallbackEndpoint} after {IdleSeconds}s idle",
                            callbackEndpoint, (int)idleTime.TotalSeconds);
                        break;
                    }

                    try
                    {
                        // Wait with timeout
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(UDP_TUNNEL_TIMEOUT_SECONDS));
                        var receivedData = await tunnelInfo.Client.ReceiveAsync(timeoutCts.Token);

                        tunnelInfo.LastActivity = DateTime.UtcNow;

                        _logger.LogDebug("UDP tunnel: received {Length} bytes from {RemoteEndPoint}, sending to {CallbackEndpoint}",
                            receivedData.Buffer.Length, receivedData.RemoteEndPoint, callbackEndpoint);

                        await udpListener.SendAsync(receivedData.Buffer, callbackEndpoint, ct);

                        // Reset timeout for next receive
                        timeoutCts.Dispose();
                        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Timeout on receive, check if tunnel should be closed
                        var currentIdleTime = DateTime.UtcNow - tunnelInfo.LastActivity;
                        if (currentIdleTime.TotalSeconds > UDP_TUNNEL_TIMEOUT_SECONDS)
                        {
                            _logger.LogDebug("UDP tunnel idle timeout for {CallbackEndpoint}", callbackEndpoint);
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
                _logger.LogWarning(ex, "Error in UDP tunnel for {CallbackEndpoint}", callbackEndpoint);
            }
            finally
            {
                timeoutCts.Dispose();
                tunnelInfo.Client?.Close();
                _udpMap.TryRemove(callbackEndpoint, out _);
                _logger.LogDebug("UDP tunnel closed for {CallbackEndpoint}", callbackEndpoint);
            }
        }

        private class UdpTunnelInfo
        {
            public UdpClient Client { get; init; }
            public DateTime LastActivity { get; set; }
        }
    }
}

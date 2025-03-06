using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CatHole
{
    public class Relay
    {
        private readonly RelayOption _option;
        private TcpListener _tcpListener;
        private readonly Dictionary<IPEndPoint, IPEndPoint> _udpClientMap = new Dictionary<IPEndPoint, IPEndPoint>();

        private IPEndPoint _externalEndpoint;
        private IPEndPoint _internalEndpoint;
        private int _timeout;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Dictionary<IPEndPoint, UdpClient> _udpMap = new Dictionary<IPEndPoint, UdpClient>();

        public Relay(RelayOption option)
        {
            _option = option;
            _externalEndpoint = IPEndPoint.Parse(_option.ExternalHost);
            _internalEndpoint = IPEndPoint.Parse(_option.InternalHost);
            _timeout = _option.Timeout;
        }

        public void Start()
        {
            if (_option.TCP)
            {
                _ = StartTCPForwarding(_cts.Token);
            }
            if (_option.UDP)
            {
                _ = StartUDPForwarding(_externalEndpoint, _internalEndpoint, _cts.Token);
            }
        }

        public void Stop()
        {
            _cts.Cancel();
        }
        public async Task StartTCPForwarding(CancellationToken cancellationToken)
        {
            _tcpListener = new TcpListener(_externalEndpoint.Address, _externalEndpoint.Port);
            _tcpListener.Start();
            Console.WriteLine($"Forwarding from {_externalEndpoint.Address}:{_externalEndpoint.Port} to {_internalEndpoint.Address}:{_internalEndpoint.Port}");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                    Console.WriteLine($"Accept new tcp client from {client.Client.RemoteEndPoint}");
                    var taskTCP = HandleTCPClient(client, cancellationToken); 
                    _ = Task.Run(async () => await taskTCP, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in listener: {ex.Message}");
            }
            finally
            {
                _tcpListener.Stop();
            }
        }

        private async Task HandleTCPClient(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                using (client)
                using (var targetClient = new TcpClient())
                {
                    // Connect to the target host and port
                    await targetClient.ConnectAsync(_internalEndpoint.Address, _internalEndpoint.Port, cancellationToken);
                    targetClient.ReceiveTimeout = _timeout;
                    targetClient.SendTimeout = _timeout;
                    client.ReceiveTimeout = _timeout;
                    client.SendTimeout = _timeout;

                    // Get network streams for both connections
                    using var clientStream = client.GetStream();
                    using var targetStream = targetClient.GetStream();

                    // Start bidirectional copying of data
                    var clientToTarget = CopyStreamAsync(clientStream, targetStream, cancellationToken);
                    var targetToClient = CopyStreamAsync(targetStream, clientStream, cancellationToken);
                    await Task.WhenAny(clientToTarget, targetToClient);
                    Console.WriteLine($"Disconnect {client.Client.RemoteEndPoint}");
                    client.Close();
                    targetClient.Close();
                }
            }
            catch (Exception ex)
            {

                Console.WriteLine($"[HandleTCPClient] Error handling client: {ex.Message}");
            }
        }

        private async Task CopyStreamAsync(NetworkStream fromStream, NetworkStream toStream, CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[_option.BufferSize];
                int bytesRead;
                while ((bytesRead = await fromStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    Console.WriteLine($"{bytesRead}");
                    await toStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying stream: {ex.Message}");
            }
        }

        public async Task StartUDPForwarding(IPEndPoint externalEndpoint, IPEndPoint internalEndpoint, CancellationToken cancellationToken)
        {
            
            UdpClient udpListener = new UdpClient(externalEndpoint);
            
            Console.WriteLine($"UDP Forwarding from {externalEndpoint.Address}:{externalEndpoint.Port} to {internalEndpoint.Address}:{internalEndpoint.Port}");
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var initDataBlock = await udpListener.ReceiveAsync(cancellationToken);
                    Console.WriteLine(initDataBlock.Buffer.Length);
                    //新用户的连接
                    var Clientendpoint  = initDataBlock.RemoteEndPoint;
                    _udpMap.TryGetValue(Clientendpoint, out var tunnelUdpClient);
                    if (tunnelUdpClient == null)
                    {
                        tunnelUdpClient = new UdpClient();
                        Console.WriteLine($"Start new UDP tunnel for  {Clientendpoint}");
                        //targetUdpClient.Client.ReceiveTimeout = _timeout;
                        //targetUdpClient.Client.SendTimeout = _timeout;
                        _udpMap.Add(Clientendpoint, tunnelUdpClient);
                        _ = Task.Run(async () =>await  HandleUdpTunnel(Clientendpoint,udpListener,tunnelUdpClient,cancellationToken),cancellationToken);
                    }
                    await tunnelUdpClient.SendAsync(initDataBlock.Buffer, _internalEndpoint, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UDP forwarding: {ex.Message}");
            }
        }

        private async Task HandleUdpTunnel(IPEndPoint callbackEndpoint, UdpClient udpListener,UdpClient tunnelUdpClient, CancellationToken cancellationToken)
        {
            var respondTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    //try
                    {
                        var receivedData = await tunnelUdpClient.ReceiveAsync(cancellationToken);
                        Console.WriteLine($"Recieved from {receivedData.RemoteEndPoint}, send to {callbackEndpoint}");
                        await udpListener.SendAsync(receivedData.Buffer, callbackEndpoint, cancellationToken);
                    }
                    //catch (Exception ex)
                    {
                        //Console.WriteLine($"Error in receiving from client: {ex.Message}");
                        //break;
                    }
                }
            }, cancellationToken);
           
            await respondTask;
            tunnelUdpClient.Close();
            _udpMap.Remove(callbackEndpoint);
        }

    }
}

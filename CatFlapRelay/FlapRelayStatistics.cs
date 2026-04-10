namespace CatFlapRelay
{
    public class FlapRelayStatistics
    {
        public bool IsRunning { get; init; }
        public DateTime? StartTime { get; init; }
        public TimeSpan Uptime { get; init; }
        public int TcpActiveConnections { get; init; }
        public long TcpTotalConnections { get; init; }
        public int UdpActiveTunnels { get; init; }
        public long TcpBytesSent { get; init; }
        public long TcpBytesReceived { get; init; }
        public long UdpBytesSent { get; init; }
        public long UdpBytesReceived { get; init; }
        public long TotalBytesTransferred => TcpBytesSent + TcpBytesReceived + UdpBytesSent + UdpBytesReceived;
        public long ConnectionErrors { get; init; }
    }
}

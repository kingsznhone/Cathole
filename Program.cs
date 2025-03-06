using System.Net;
using System.Text.Json;

namespace CatHole
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            RelayOption optionIperf = new()
            {
                ExternalHost = "127.0.0.1:5201",
                InternalHost = "192.168.86.172:5201",
                TCP = true,
                UDP = true,
                BufferSize = 128 * 1024,
                Timeout = 1000
            };
            Relay relayIperf = new Relay(optionIperf);
            relayIperf.Start();
            RelayOption optionWeb = new()
            {
                ExternalHost = "127.0.0.1:88",
                InternalHost = "192.168.86.200:88",
                TCP = true,
                UDP = true,
                BufferSize = 128*1024
            };
            Relay relayWeb = new Relay(optionWeb);
            relayWeb.Start();

            RelayOption optionLogin = new()
            {
                ExternalHost = "127.0.0.1:5050",
                InternalHost = "192.168.86.200:5050",
                TCP = true,
                UDP = true,
                BufferSize = 128 * 1024
            };
            Relay relayLogin = new Relay(optionLogin);
            relayLogin.Start();

            RelayOption optionWorld = new()
            {
                ExternalHost = "127.0.0.1:6000",
                InternalHost = "192.168.86.200:6000",
                TCP = true,
                UDP = true,
                BufferSize = 8192
            };
            Relay relayWorld = new Relay(optionWorld);
            relayWorld.Start();

            Console.ReadLine();
            Console.WriteLine();
        }
    }
}

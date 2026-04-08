using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatHole.Core
{
    /// <summary>
    /// Factory for creating and configuring Relay instances
    /// </summary>
    public class CatHoleRelayFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Initializes a new instance with no logging output.
        /// </summary>
        public CatHoleRelayFactory() : this(NullLoggerFactory.Instance) { }

        public CatHoleRelayFactory(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// Creates a new Relay instance with the given options
        /// </summary>
        public CatHoleRelay CreateRelay(CatHoleRelayOption option)
        {
            ArgumentNullException.ThrowIfNull(option);

            ValidateOption(option);

            var logger = _loggerFactory.CreateLogger<CatHoleRelay>();
            return new CatHoleRelay(option, logger);
        }

        /// <summary>
        /// Creates a new Relay instance with a builder pattern
        /// </summary>
        public static RelayBuilder CreateBuilder(ILoggerFactory loggerFactory)
            => new(loggerFactory);

        /// <summary>
        /// Validates relay options
        /// </summary>
        public static void ValidateOption(CatHoleRelayOption option)
        {
            ArgumentNullException.ThrowIfNull(option);

            if (string.IsNullOrWhiteSpace(option.Name))
                throw new ArgumentException("Relay name cannot be empty", nameof(option));

            if (string.IsNullOrWhiteSpace(option.ListenHost))
                throw new ArgumentException("ListenHost cannot be empty", nameof(option));

            if (string.IsNullOrWhiteSpace(option.TargetHost))
                throw new ArgumentException("TargetHost cannot be empty", nameof(option));

            if (option.BufferSize <= 0)
                throw new ArgumentException("BufferSize must be positive", nameof(option));

            if (option.SocketTimeout < TimeSpan.Zero)
                throw new ArgumentException("SocketTimeout cannot be negative", nameof(option));

            if (option.UdpTunnelTimeout < TimeSpan.Zero)
                throw new ArgumentException("UdpTunnelTimeout cannot be negative", nameof(option));

            if (!option.TCP && !option.UDP)
                throw new ArgumentException("At least one of TCP or UDP must be enabled", nameof(option));

            // Validate endpoint formats
            try
            {
                System.Net.IPEndPoint.Parse(option.ListenHost);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"Invalid ListenHost format: {option.ListenHost}", nameof(option), ex);
            }

            try
            {
                System.Net.IPEndPoint.Parse(option.TargetHost);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"Invalid TargetHost format: {option.TargetHost}", nameof(option), ex);
            }
        }
    }

    /// <summary>
    /// Builder for fluent Relay configuration
    /// </summary>
    public class RelayBuilder
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly CatHoleRelayOption _option = new();
        private bool _built;

        public RelayBuilder(ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _loggerFactory = loggerFactory;
        }

        public RelayBuilder WithName(string name)
        {
            _option.Name = name;
            return this;
        }

        public RelayBuilder ListenOn(string host)
        {
            _option.ListenHost = host;
            return this;
        }

        public RelayBuilder ForwardTo(string host)
        {
            _option.TargetHost = host;
            return this;
        }

        public RelayBuilder WithBufferSize(int size)
        {
            _option.BufferSize = size;
            return this;
        }

        public RelayBuilder WithSocketTimeout(TimeSpan timeout)
        {
            _option.SocketTimeout = timeout;
            return this;
        }

        public RelayBuilder WithUdpTunnelTimeout(TimeSpan timeout)
        {
            _option.UdpTunnelTimeout = timeout;
            return this;
        }

        public RelayBuilder EnableTCP(bool enabled = true)
        {
            _option.TCP = enabled;
            return this;
        }

        public RelayBuilder EnableUDP(bool enabled = true)
        {
            _option.UDP = enabled;
            return this;
        }

        public RelayBuilder TCPOnly()
        {
            _option.TCP = true;
            _option.UDP = false;
            return this;
        }

        public RelayBuilder UDPOnly()
        {
            _option.TCP = false;
            _option.UDP = true;
            return this;
        }

        public CatHoleRelay Build()
        {
            if (_built)
                throw new InvalidOperationException("Build() has already been called. Create a new RelayBuilder for each relay.");

            CatHoleRelayFactory.ValidateOption(_option);
            _built = true;
            var logger = _loggerFactory.CreateLogger<CatHoleRelay>();
            return new CatHoleRelay(_option, logger);
        }
    }
}

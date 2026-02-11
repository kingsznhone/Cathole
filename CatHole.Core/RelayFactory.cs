using Microsoft.Extensions.Logging;

namespace CatHole.Core
{
    /// <summary>
    /// Factory for creating and configuring Relay instances
    /// </summary>
    public class RelayFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public RelayFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <summary>
        /// Creates a new Relay instance with the given options
        /// </summary>
        public Relay CreateRelay(RelayOption option)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));

            ValidateOption(option);

            var logger = _loggerFactory.CreateLogger<Relay>();
            return new Relay(option, logger);
        }

        /// <summary>
        /// Creates a new Relay instance with a builder pattern
        /// </summary>
        public static RelayBuilder CreateBuilder(ILoggerFactory loggerFactory)
        {
            return new RelayBuilder(loggerFactory);
        }

        /// <summary>
        /// Validates relay options
        /// </summary>
        public static void ValidateOption(RelayOption option)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));

            if (string.IsNullOrWhiteSpace(option.Name))
                throw new ArgumentException("Relay name cannot be empty", nameof(option));

            if (string.IsNullOrWhiteSpace(option.ListenHost))
                throw new ArgumentException("ListenHost cannot be empty", nameof(option));

            if (string.IsNullOrWhiteSpace(option.TargetHost))
                throw new ArgumentException("TargetHost cannot be empty", nameof(option));

            if (option.BufferSize <= 0)
                throw new ArgumentException("BufferSize must be positive", nameof(option));

            if (option.Timeout < 0)
                throw new ArgumentException("Timeout cannot be negative", nameof(option));

            if (!option.TCP && !option.UDP)
                throw new ArgumentException("At least one of TCP or UDP must be enabled", nameof(option));

            // Validate endpoint formats
            try
            {
                System.Net.IPEndPoint.Parse(option.ListenHost);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid ListenHost format: {option.ListenHost}", nameof(option), ex);
            }

            try
            {
                System.Net.IPEndPoint.Parse(option.TargetHost);
            }
            catch (Exception ex)
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
        private readonly RelayOption _option = new();

        public RelayBuilder(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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

        public RelayBuilder WithTimeout(int timeout)
        {
            _option.Timeout = timeout;
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

        public Relay Build()
        {
            RelayFactory.ValidateOption(_option);
            var logger = _loggerFactory.CreateLogger<Relay>();
            return new Relay(_option, logger);
        }
    }
}

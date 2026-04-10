// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatFlapRelay
{
    [JsonSerializable(typeof(FlapRelayOption))]
    internal partial class CatFlapRelayOptionContext : JsonSerializerContext { }

    public class FlapRelayOption
    {
        private static readonly CatFlapRelayOptionContext _jsonContext =
            new(new JsonSerializerOptions { WriteIndented = true });


        public Guid Id { get; init; } = Guid.NewGuid();

        public string Name { get; set; } = "Unnamed";

        public string ListenHost { get; set; } = "127.0.0.1:56789";

        public string TargetHost { get; set; } = "127.0.0.1:45678";

        public int BufferSize { get; set; } = 128 * 1024;

        public bool TCP { get; set; } = true;

        public bool UDP { get; set; } = true;

        public TimeSpan SocketTimeout { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan UdpTunnelTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// When true and the listen address is an IPv6 address, enables dual-stack mode so the
        /// socket accepts both IPv4 and IPv6 connections (sets IPV6_V6ONLY = false).
        /// Linux defaults to IPV6_V6ONLY = true (RFC 3493); set this explicitly when you want
        /// [::] to also accept IPv4 traffic on Linux.
        /// </summary>
        public bool DualMode { get; set; } = false;

        public override string ToString() => JsonSerializer.Serialize(this, _jsonContext.FlapRelayOption);
    }
}

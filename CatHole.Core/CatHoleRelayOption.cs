// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace CatHole.Core
{
    public class CatHoleRelayOption
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public Guid Id { get; init; } = Guid.NewGuid();

        public string Name { get; set; } = "Unnamed";

        public string ListenHost { get; set; } = "127.0.0.1:56789";

        public string TargetHost { get; set; } = "127.0.0.1:45678";

        public int BufferSize { get; set; } = 128 * 1024;

        public bool TCP { get; set; } = true;

        public bool UDP { get; set; } = true;

        public TimeSpan SocketTimeout { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan UdpTunnelTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public override string ToString() => JsonSerializer.Serialize(this, _jsonOptions);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace CatHole.Core
{
    public class CatHoleRelayOption
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public string Name { get; set; } = "Unnamed";

        public string ListenHost { get; set; } = "127.0.0.1:56789";

        public string TargetHost { get; set; } = "127.0.0.1:45678";

        public int BufferSize { get; set; } = 128 * 1024;

        public bool TCP { get; set; } = true;

        public bool UDP { get; set; } = true;

        public int Timeout { get; set; } = 1000; // miliseconds

        public int UdpTunnelTimeout { get; set; } = 60; // seconds

        public override string ToString() =>JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }
}

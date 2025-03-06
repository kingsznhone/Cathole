// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace CatHole.Core
{
    [JsonSerializable(typeof(RelayOption))]
    public class RelayOption
    {
        [JsonPropertyName("externalHost")]
        public string ExternalHost { get; set; } = "127.0.0.1:45678";

        [JsonPropertyName("internalHost")]
        public string InternalHost { get; set; } = "127.0.0.1:45678";

        [JsonPropertyName("bufferSize")]
        public int BufferSize { get; set; } = 128 * 1024;

        [JsonPropertyName("tcp")]
        public bool TCP { get; set; } = true;

        [JsonPropertyName("udp")]
        public bool UDP { get; set; } = true;

        [JsonPropertyName("timeout")]
        public int Timeout { get; set; } = 1000; // miliseconds
    }
}

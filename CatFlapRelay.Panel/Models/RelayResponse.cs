// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CatFlapRelay;

namespace CatFlapRelay.Panel.Models;

/// <summary>
/// Combined view of a relay's configuration and its current runtime statistics.
/// Statistics is null when the relay is not running.
/// </summary>
public record RelayResponse(
    Guid Id,
    string Name,
    string ListenHost,
    string TargetHost,
    bool TCP,
    bool UDP,
    int BufferSize,
    // <summary>Socket send/receive timeout in seconds.</summary>
    double SocketTimeout,
    // <summary>UDP session idle timeout in seconds.</summary>
    double UdpTunnelTimeout,
    FlapRelayStatistics? Statistics
);

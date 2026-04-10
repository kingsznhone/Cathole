// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace CatFlapRelay.Panel.Models;

/// <summary>
/// Request body used when creating or updating a relay.
/// </summary>
public class RelayRequest
{
    [Required]
    [StringLength(128)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(64)]
    [DefaultValue("127.0.0.1:15000")]
    public string ListenHost { get; init; } = string.Empty;

    [DefaultValue("127.0.0.1:25000")]
    [Required]
    [StringLength(64)]
    public string TargetHost { get; init; } = string.Empty;

    public bool TCP { get; init; } = true;

    public bool UDP { get; init; } = true;

    [DefaultValue(128 * 1024)]
    public int BufferSize { get; init; } = 128 * 1024;

    /// <summary>Socket send/receive timeout in seconds.</summary>
    [DefaultValue(1.0)]
    public double? SocketTimeout { get; init; } = 1.0;

    /// <summary>UDP session idle timeout in seconds.</summary>
    [DefaultValue(60.0)]
    public double? UdpTunnelTimeout { get; init; } = 60.0;
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CatHole.Panel.Data;

/// <summary>
/// EF Core entity that persists a <see cref="CatHole.Core.CatHoleRelayOption"/> to the database.
/// TimeSpan values are stored as ticks (long) because SQLite has no native interval type.
/// </summary>
[Index(nameof(Name),IsUnique =true)]
internal sealed class RelayEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }
    [Required]
    public string Name { get; set; } = "";
    [Required]
    public string ListenHost { get; set; } = "";
    [Required]
    public string TargetHost { get; set; } = "";
    public int BufferSize { get; set; }
    public bool Tcp { get; set; } = true;
    public bool Udp { get; set; } = true;
    public TimeSpan SocketTimeout { get; set; }
    public TimeSpan UdpTunnelTimeout { get; set; }
}

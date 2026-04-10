// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CatFlapRelay.Panel.Models;

public class PanelSettings
{
    public const string SectionName = "PanelSettings";

    /// <summary>Maximum number of relays that can be created.</summary>
    public int MaxRelays { get; set; } = 256;

    /// <summary>Maximum length of relay Name field.</summary>
    public int MaxNameLength { get; set; } = 128;

    /// <summary>Maximum length of ListenHost / TargetHost fields.</summary>
    public int MaxEndpointLength { get; set; } = 64;

    /// <summary>Maximum buffer size in bytes per relay connection (default 64 MB).</summary>
    public int MaxBufferSize { get; set; } = 64 * 1024 * 1024;
}

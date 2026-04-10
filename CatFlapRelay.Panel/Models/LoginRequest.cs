// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace CatFlapRelay.Panel.Models;

/// <summary>
/// Request body for obtaining a JWT access token.
/// </summary>
public class LoginRequest
{
    [Required]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    /// <summary>Token validity in days. Range: 1–3650. Defaults to 30.</summary>
    [DefaultValue(30)]
    [Range(1, 3650)]
    public int ExpiryDays { get; init; } = 30;
}

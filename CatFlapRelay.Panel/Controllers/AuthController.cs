// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Asp.Versioning;
using CatFlapRelay.Panel.Data;
using CatFlapRelay.Panel.Models;
using CatFlapRelay.Panel.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CatFlapRelay.Panel.Controllers;

/// <summary>
/// Issues JWT access tokens for authenticated users.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[Tags("Auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtService _jwtService;

    public AuthController(UserManager<ApplicationUser> userManager, JwtService jwtService)
    {
        _userManager = userManager;
        _jwtService = jwtService;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT access token.
    /// </summary>
    /// <param name="request">Username and password credentials.</param>
    /// <returns>A JWT token valid for 24 hours.</returns>
    [HttpPost("token")]
    [ProducesResponseType<TokenValidateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Token([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var token = _jwtService.BuildAccessToken(user, TimeSpan.FromDays(request.ExpiryDays));
        return Ok(new TokenValidateResponse { Success = true, Token = token });
    }
}

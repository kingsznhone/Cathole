// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Asp.Versioning;
using CatFlap.Core;
using CatFlap.Panel.Models;
using CatFlap.Panel.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatFlap.Panel.Controllers;

/// <summary>
/// Full CRUD and lifecycle management for relay configurations.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[Tags("Relay")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public class RelayController : ControllerBase
{
    private readonly RelayService _relayService;
    private readonly LinkGenerator _linkGenerator;

    public RelayController(RelayService relayService, LinkGenerator linkGenerator)
    {
        _relayService = relayService;
        _linkGenerator = linkGenerator;
    }

    /// <summary>
    /// Returns all configured relays together with their current runtime statistics.
    /// </summary>
    /// <remarks>Statistics is null for any relay that is not currently running.</remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RelayResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RelayResponse>>> GetAllAsync()
    {
        var options = await _relayService.GetAllOptions();
        return Ok(options.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Returns a single relay by id together with its current runtime statistics.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<RelayResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RelayResponse>> GetByIdAsync(Guid id)
    {
        var option = await _relayService.GetByIdAsync(id);
        if (option is null)
            return Problem(title: "Relay not found", detail: $"Relay [{id}] does not exist.", statusCode: StatusCodes.Status404NotFound);
        return Ok(ToResponse(option));
    }

    /// <summary>
    /// Creates and starts a new relay.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<RelayResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RelayResponse>> CreateAsync(RelayRequest request)
    {
        var option = ToOption(request);
        try
        {
            await _relayService.AddRelayAsync(option);
        }
        catch (RelayAlreadyExistsException ex)
        {
            return Problem(title: "Relay already exists", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: "Invalid relay configuration", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var location = _linkGenerator.GetPathByAction(HttpContext, nameof(GetByIdAsync), "Relay", new { id = option.Id });
        return Created(location, ToResponse(option));
    }

    /// <summary>
    /// Updates an existing relay. The relay is stopped and restarted with the new configuration.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<RelayResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RelayResponse>> UpdateAsync(Guid id, RelayRequest request)
    {
        var option = ToOption(request, id);
        try
        {
            await _relayService.UpdateRelayAsync(id, option);
        }
        catch (RelayNotFoundException ex)
        {
            return Problem(title: "Relay not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: "Invalid relay configuration", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(ToResponse(option));
    }

    /// <summary>
    /// Stops and removes a relay.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id)
    {
        try
        {
            await _relayService.RemoveRelayAsync(id);
        }
        catch (RelayNotFoundException ex)
        {
            return Problem(title: "Relay not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }

        return NoContent();
    }

    /// <summary>
    /// Starts a relay that is not currently running.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult Start(Guid id)
    {
        try
        {
            _relayService.StartRelay(id);
        }
        catch (RelayNotFoundException ex)
        {
            return Problem(title: "Relay not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }

        return NoContent();
    }

    /// <summary>
    /// Stops a relay without removing it.
    /// </summary>
    [HttpPost("{id:guid}/stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StopAsync(Guid id)
    {
        try
        {
            await _relayService.StopRelayAsync(id);
        }
        catch (RelayNotFoundException ex)
        {
            return Problem(title: "Relay not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }

        return NoContent();
    }

    /// <summary>
    /// Starts all relays that are not currently running.
    /// </summary>
    [HttpPost("start-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult StartAll()
    {
        _relayService.StartAll();
        return NoContent();
    }

    /// <summary>
    /// Stops all running relays without removing them from configuration.
    /// </summary>
    [HttpPost("stop-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> StopAllAsync()
    {
        await _relayService.StopAllAsync();
        return NoContent();
    }

    /// <summary>
    /// Stops and removes all relays from both runtime and configuration.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearAllAsync()
    {
        await _relayService.ClearAllAsync();
        return NoContent();
    }

    private RelayResponse ToResponse(CatFlapRelayOption o) => new(
        o.Id,
        o.Name,
        o.ListenHost,
        o.TargetHost,
        o.TCP,
        o.UDP,
        o.BufferSize,
        o.SocketTimeout.TotalSeconds,
        o.UdpTunnelTimeout.TotalSeconds,
        _relayService.GetStatistics(o.Id)
    );

    private static CatFlapRelayOption ToOption(RelayRequest request, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = request.Name,
        ListenHost = request.ListenHost,
        TargetHost = request.TargetHost,
        TCP = request.TCP,
        UDP = request.UDP,
        BufferSize = request.BufferSize,
        SocketTimeout = TimeSpan.FromSeconds(request.SocketTimeout ?? 1.0),
        UdpTunnelTimeout = TimeSpan.FromSeconds(request.UdpTunnelTimeout ?? 60.0),
    };
}

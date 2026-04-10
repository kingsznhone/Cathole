namespace CatFlap.Core;

/// <summary>
/// Thrown when a relay with the specified Id cannot be found in the manager.
/// </summary>
public sealed class RelayNotFoundException(Guid id)
    : KeyNotFoundException($"Relay [{id}] not found")
{
    /// <summary>The Id that was not found.</summary>
    public Guid RelayId { get; } = id;
}

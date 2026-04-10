namespace CatFlapRelay;

/// <summary>
/// Thrown when attempting to add a relay whose Id already exists in the manager.
/// </summary>
public sealed class RelayAlreadyExistsException(string name, Guid id)
    : InvalidOperationException($"Relay '{name}' [{id}] already exists")
{
    /// <summary>The name of the relay that caused the conflict.</summary>
    public string RelayName { get; } = name;

    /// <summary>The Id of the relay that caused the conflict.</summary>
    public Guid RelayId { get; } = id;
}
